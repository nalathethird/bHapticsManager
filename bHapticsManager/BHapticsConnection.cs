// BHapticsConnection.cs
// Handles connection initialization to bHaptics Player

using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	public static class BHapticsConnection {
		public static readonly Dictionary<ModernBHaptics.PositionID, (bool isActive, DateTime lastCheck)> DeviceCache = new();
		
		private static ModernBHapticsWorkerThread _workerThread;
		private static bool _isInitialized = false;

		/// Initializes connection to bHaptics Player and subscribes to events.
		/// Called once during mod initialization.
		
		public static void Initialize() {
			if (_isInitialized) {
				ResoniteMod.Warn("Already initialized - skipping duplicate connection");
				return;
			}
			
			DeviceEventHandler.Subscribe();
			
			// Connect to bHaptics Player
			bool connected = ModernBHaptics.bHapticsManager.Connect("Resonite", "Resonite", true, 10);
			
			if (!connected) {
				ResoniteMod.Error("Failed to connect to bHaptics Player!");
				ResoniteMod.Error("Make sure bHaptics Player is running and try restarting Resonite.");
				return;
			}

			_isInitialized = true;
			ResoniteMod.Msg("bHapticsManager connected successfully!");
			
			// Log connected devices
			int deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
			ResoniteMod.Msg($"Connected devices: {deviceCount}");
			
			foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
				if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
					ResoniteMod.Msg($"  - {pos} device ready");
					// Add to cache
					DeviceCache[pos] = (true, DateTime.Now);
				}
			}
		}
		
		
		/// Starts the worker thread after FrooxEngine has initialized all haptic points.
		/// This should be called AFTER BHapticsDriver.InitializeBhaptics() completes.
		
		public static void StartWorkerThread() {
			try {
				var engine = Engine.Current;
				if (engine == null) {
					ResoniteMod.Error("Engine not ready - cannot start worker thread");
					return;
				}
				
				var inputInterface = engine.InputInterface;
				if (inputInterface == null) {
					ResoniteMod.Error("InputInterface not ready - cannot start worker thread");
					return;
				}
				
				if (inputInterface.HapticPointCount == 0) {
					ResoniteMod.Warn($"No haptic points registered yet - worker thread will be idle");
				}
				
				if (_workerThread != null) {
					ResoniteMod.Warn("Worker thread already exists - skipping duplicate start");
					return;
				}
				
				_workerThread = new ModernBHapticsWorkerThread(inputInterface);
				_workerThread.Start();
				
				ResoniteMod.Msg("Worker thread started successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to start worker thread: {ex.Message}");
			}
		}

		/// Shuts down the connection to bHaptics Player, stopping all patterns and clearing the device cache.
		
		public static void Shutdown() {
			try {
				_workerThread?.Stop();
				_workerThread = null;
				
				ModernBHaptics.bHapticsManager.StopPlayingAll();
				
				bool disconnected = ModernBHaptics.bHapticsManager.Disconnect();
				
				if (disconnected) {
					ResoniteMod.Msg("Disconnected successfully");
				} else {
					ResoniteMod.Warn("Disconnect returned false");
				}
				
				DeviceCache.Clear();
				_isInitialized = false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error during shutdown: {ex.Message}");
			}
		}
		
		
		/// Invalidates device cache for a specific device (called when device status changes)
		
		public static void InvalidateDeviceCache(ModernBHaptics.PositionID position, bool isConnected) {
			DeviceCache[position] = (isConnected, DateTime.Now);
		}
	}
}
