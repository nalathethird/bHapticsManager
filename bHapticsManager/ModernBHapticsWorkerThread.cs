// ModernBHapticsWorkerThread.cs
// Replaces FrooxEngine's broken BHapticsDriver worker thread with a modern implementation
// Uses bHapticsLib (native .NET 9 WebSockets + MessagePack) instead of legacy SDK

using System.Reflection;
using System.Threading;
using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public class ModernBHapticsWorkerThread {
		private class HapticPointData {
			public HapticPoint Point { get; }
			public float TempPhi { get; set; }
			public float VibrationPhi { get; set; }
			
			public HapticPointData(HapticPoint point) {
				Point = point;
				TempPhi = 0f;
				VibrationPhi = 0f;
			}
		}
		
		private const int UPDATE_INTERVAL_MS = 10;
		private const int SUBMISSION_DURATION_MS = 40;
		
		private readonly InputInterface _inputInterface;
		private readonly CancellationTokenSource _cancellationToken;
		private readonly Thread _workerThread;
		private readonly Dictionary<LegacyBHaptics.PositionType, List<HapticPointData>> _hapticPointsByDevice;
		private readonly Dictionary<LegacyBHaptics.PositionType, string> _deviceKeys;
		
		private float _globalPainPhi = 0f;
		private int _frameCount = 0;
		private DateTime _lastStatsReport = DateTime.Now;
		
		public ModernBHapticsWorkerThread(InputInterface inputInterface) {
			_inputInterface = inputInterface;
			_cancellationToken = new CancellationTokenSource();
			_hapticPointsByDevice = new Dictionary<LegacyBHaptics.PositionType, List<HapticPointData>>();
			_deviceKeys = new Dictionary<LegacyBHaptics.PositionType, string>();
			
			_workerThread = new Thread(WorkerThreadLoop) {
				Priority = ThreadPriority.Highest,
				IsBackground = true,
				Name = "ModernBHapticsWorker"
			};
		}
		
		public void Start() {
			if (_workerThread.IsAlive) {
				ResoniteMod.Warn("Worker thread already running!");
				return;
			}
			
			if (!PopulateHapticPoints()) {
				ResoniteMod.Error("Failed to populate haptic points - worker thread not started");
				return;
			}
			
			ResoniteMod.Msg($"Starting worker thread with {GetTotalPointCount()} points across {_hapticPointsByDevice.Count} devices");
			_workerThread.Start();
		}
		
		public void Stop() {
			if (!_workerThread.IsAlive) {
				return;
			}
			
			_cancellationToken.Cancel();
			
			if (!_workerThread.Join(TimeSpan.FromSeconds(2))) {
				ResoniteMod.Warn("Worker thread did not stop gracefully, aborting...");
				_workerThread.Interrupt();
			}
		}
		
		private bool PopulateHapticPoints() {
			try {
				int totalPoints = _inputInterface.HapticPointCount;
				if (totalPoints == 0) {
					ResoniteMod.Warn("No haptic points registered in InputInterface");
					return false;
				}
				
				for (int i = 0; i < totalPoints; i++) {
					HapticPoint point = _inputInterface.GetHapticPoint(i);
					if (point == null) continue;
					
					LegacyBHaptics.PositionType deviceType = GetDeviceTypeFromPosition(point.Position);
					
					if (!_hapticPointsByDevice.ContainsKey(deviceType)) {
						_hapticPointsByDevice[deviceType] = new List<HapticPointData>();
						_deviceKeys[deviceType] = Guid.NewGuid().ToString();
					}
					
					_hapticPointsByDevice[deviceType].Add(new HapticPointData(point));
				}
				
				return true;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error populating haptic points: {ex.Message}");
				return false;
			}
		}
		
		private LegacyBHaptics.PositionType GetDeviceTypeFromPosition(HapticPointPosition position) {
			string typeName = position.GetType().Name;
			
			return typeName switch {
				"HeadHapticPointPosition" => LegacyBHaptics.PositionType.Head,
				"TorsoHapticPointPosition" => LegacyBHaptics.PositionType.Vest,
				"ArmHapticPosition" => GetArmSide(position),
				"LegHapticPosition" => GetLegSide(position),
				_ => LegacyBHaptics.PositionType.Vest
			};
		}
		
		private LegacyBHaptics.PositionType GetArmSide(HapticPointPosition position) {
			try {
				var sideProperty = position.GetType().GetProperty("Side");
				if (sideProperty != null) {
					var side = sideProperty.GetValue(position);
					if (side != null && side.ToString() == "Left") {
						return LegacyBHaptics.PositionType.ForearmL;
					}
				}
			}
			catch { }
			return LegacyBHaptics.PositionType.ForearmR;
		}
		
		private LegacyBHaptics.PositionType GetLegSide(HapticPointPosition position) {
			try {
				var sideProperty = position.GetType().GetProperty("Side");
				if (sideProperty != null) {
					var side = sideProperty.GetValue(position);
					if (side != null && side.ToString() == "Left") {
						return LegacyBHaptics.PositionType.FootL;
					}
				}
			}
			catch { }
			return LegacyBHaptics.PositionType.FootR;
		}
		
		private int GetTotalPointCount() {
			int count = 0;
			foreach (var group in _hapticPointsByDevice.Values) {
				count += group.Count;
			}
			return count;
		}
		
		private void WorkerThreadLoop() {
			var dotPoints = new List<LegacyBHaptics.DotPoint>();
			
			try {
				while (!_cancellationToken.IsCancellationRequested) {
					var frameStart = DateTime.Now;
					
					Thread.Sleep(UPDATE_INTERVAL_MS);
					
					float dt = UPDATE_INTERVAL_MS / 1000f;
					
					float maxPain = 0f;
					
					foreach (var deviceGroup in _hapticPointsByDevice.Values) {
						foreach (var pointData in deviceGroup) {
							pointData.Point.SampleSources();
							maxPain = MathX.Max(pointData.Point.Pain, maxPain);
						}
					}
					
					_globalPainPhi += MathF.PI * 2f * dt * MathX.Lerp(1.3333334f, 2.3333333f, maxPain);
					_globalPainPhi %= MathF.PI * 4f;
					
					foreach (var kvp in _hapticPointsByDevice) {
						LegacyBHaptics.PositionType deviceType = kvp.Key;
						List<HapticPointData> points = kvp.Value;
						string deviceKey = _deviceKeys[deviceType];
						
						dotPoints.Clear();
						int motorIndex = 0;
						
						foreach (var pointData in points) {
							HapticPoint point = pointData.Point;
							
							float intensity = point.Force;
							
							float painAmplitude = MathX.Pow(MathX.Abs(MathX.Sin(_globalPainPhi)), 2f) 
								* (float)MathX.Max(0, MathX.Sign(MathX.Sin(_globalPainPhi * 0.5f)));
							painAmplitude *= MathX.Pow(point.Pain, 0.5f);
							painAmplitude += RandomX.Value * MathX.Pow(point.Pain, 0.25f) * 0.1f;
							intensity = MathX.Max(intensity, painAmplitude);
							
							float normalizedTemp = MathX.Abs(point.Temperature / 100f);
							pointData.TempPhi += normalizedTemp * 4f;
							pointData.TempPhi %= 20000f;
							float tempAmplitude = normalizedTemp * MathX.SimplexNoise(pointData.TempPhi);
							intensity = MathX.Max(intensity, tempAmplitude);
							
							pointData.VibrationPhi += MathF.PI * 2f * dt * MathX.Lerp(0.1f, 10f, point.Vibration);
							pointData.VibrationPhi %= MathF.PI * 2f;
							float vibrationAmplitude = (MathX.Sin(pointData.VibrationPhi) * 0.5f + 0.5f) * point.Vibration;
							intensity = MathX.Max(intensity, vibrationAmplitude);
							
							int intensityInt = MathX.Clamp(MathX.RoundToInt(intensity * 100f), 0, 100);
							
							dotPoints.Add(new LegacyBHaptics.DotPoint(motorIndex++, intensityInt));
						}
						
						if (dotPoints.Count > 0) {
							LegacyCompatibilityLayer.SubmitFrame(deviceKey, deviceType, dotPoints, SUBMISSION_DURATION_MS);
						}
					}
					
					_frameCount++;
					if ((DateTime.Now - _lastStatsReport).TotalSeconds >= 10) {
						if (bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
							double avgFps = _frameCount / (DateTime.Now - _lastStatsReport).TotalSeconds;
							ResoniteMod.Msg($"Worker thread: {avgFps:F1} Hz avg, {_hapticPointsByDevice.Count} devices, {GetTotalPointCount()} points");
						}
						_frameCount = 0;
						_lastStatsReport = DateTime.Now;
					}
				}
			}
			catch (ThreadInterruptedException) {
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Worker thread error: {ex.Message}");
			}
		}
	}
}
