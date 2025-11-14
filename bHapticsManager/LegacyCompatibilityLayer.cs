// LegacyCompatibilityLayer.cs
// Provides exact Bhaptics.Tact behavior using modern bHapticsLib

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public static class LegacyCompatibilityLayer {
		
		private static readonly Dictionary<string, DateTime> _lastSubmissionTime = new();
		private static readonly object _submissionLock = new object();
		private const int MIN_SUBMISSION_INTERVAL_MS = 10;
		
		public static void SubmitFrame(string key, LegacyBHaptics.PositionType position, 
			List<LegacyBHaptics.DotPoint> dotPoints, int durationMillis) {
			
			if (position == LegacyBHaptics.PositionType.Vest && dotPoints != null && dotPoints.Count > 0) {
				bool hasVestFront = ModernBHaptics.bHapticsManager.IsDeviceConnected(ModernBHaptics.PositionID.VestFront);
				bool hasVestBack = ModernBHaptics.bHapticsManager.IsDeviceConnected(ModernBHaptics.PositionID.VestBack);
				
				if (hasVestFront || hasVestBack) {
					var frontPoints = new List<LegacyBHaptics.DotPoint>();
					var backPoints = new List<LegacyBHaptics.DotPoint>();
					
					foreach (var point in dotPoints) {
						if (point.Index < 20) {
							frontPoints.Add(point);
						} else {
							backPoints.Add(new LegacyBHaptics.DotPoint(point.Index - 20, point.Intensity));
						}
					}
					
					if (frontPoints.Count > 0 && hasVestFront) {
						SubmitFrame(key + "_front", LegacyBHaptics.PositionType.VestFront, frontPoints, durationMillis);
					}
					if (backPoints.Count > 0 && hasVestBack) {
						SubmitFrame(key + "_back", LegacyBHaptics.PositionType.VestBack, backPoints, durationMillis);
					}
					
					return;
				}
			}
			
			lock (_submissionLock) {
				string deviceKey = $"{position}_{key}";
				
				if (_lastSubmissionTime.TryGetValue(deviceKey, out DateTime lastTime)) {
					double timeSinceLastMs = (DateTime.Now - lastTime).TotalMilliseconds;
					if (timeSinceLastMs < MIN_SUBMISSION_INTERVAL_MS) {
						return;
					}
				}
				_lastSubmissionTime[deviceKey] = DateTime.Now;
			}
			
			var modernPosition = PositionMapper.MapLegacyToModern(position);
			
			if (!ModernBHaptics.bHapticsManager.IsDeviceConnected(modernPosition)) {
				return;
			}
			
			int motorCount = modernPosition switch {
				ModernBHaptics.PositionID.Vest => 40,
				ModernBHaptics.PositionID.VestFront => 20,
				ModernBHaptics.PositionID.VestBack => 20,
				ModernBHaptics.PositionID.Head => 20,
				ModernBHaptics.PositionID.ArmLeft => 6,
				ModernBHaptics.PositionID.ArmRight => 6,
				ModernBHaptics.PositionID.HandLeft => 6,
				ModernBHaptics.PositionID.HandRight => 6,
				ModernBHaptics.PositionID.FootLeft => 3,
				ModernBHaptics.PositionID.FootRight => 3,
				_ => 20
			};
			
			int[] motors = new int[motorCount];
			
			if (dotPoints != null) {
				foreach (var legacy in dotPoints) {
					if (legacy.Intensity > 0 && legacy.Index < motorCount) {
						motors[legacy.Index] = Math.Min(100, legacy.Intensity);
					}
				}
			}
			
			int extendedDuration = Math.Max(40, durationMillis);
			
			try {
				ModernBHaptics.bHapticsManager.PlayMotors(
					key, 
					extendedDuration, 
					modernPosition, 
					motors
				);
			}
			catch (Exception ex) {
				ResoniteModLoader.ResoniteMod.Warn($"Failed to submit for {position}: {ex.Message}");
			}
		}
		
		public static void SubmitFrame(string key, LegacyBHaptics.PositionType position, 
			byte[] motorBytes, int durationMillis) {
			
			List<LegacyBHaptics.DotPoint> dotPoints = new List<LegacyBHaptics.DotPoint>();
			
			for (int i = 0; i < motorBytes.Length; i++) {
				if (motorBytes[i] > 0) {
					dotPoints.Add(new LegacyBHaptics.DotPoint(i, motorBytes[i]));
				}
			}
			
			SubmitFrame(key, position, dotPoints, durationMillis);
		}
		
		public static void CleanupDevice(LegacyBHaptics.PositionType position) {
			lock (_submissionLock) {
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => kvp.Key.StartsWith($"{position}_"))
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
			}
		}
		
		public static void ResetDevice(LegacyBHaptics.PositionType position) {
			lock (_submissionLock) {
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => kvp.Key.StartsWith($"{position}_"))
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
			}
		}
		
		public static void CleanupOldSubmissions() {
			lock (_submissionLock) {
				var now = DateTime.Now;
				
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => (now - kvp.Value).TotalSeconds > 5)
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
			}
		}
	}
}
