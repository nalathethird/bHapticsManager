using System.Threading;
using HarmonyLib;
using ResoniteModLoader;
using Elements.Core;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public class bHapticsManager : ResoniteMod {
		internal const string VERSION_CONSTANT = "1.0.1";
		public override string Name => "bHapticsManager";
		public override string Author => "NalaTheThird";
		public override string Version => VERSION_CONSTANT;
		public override string Link => "https://github.com/nalathethird/bHapticsManager";

		public static ModConfiguration Config;
		private static bool _workerThreadStarted = false;

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_HOTPLUG =
			new("enable_hotplug", "Allow devices to connect/disconnect without restarting Resonite", () => true);

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_SELF_HAPTICS =
			new("enable_self_haptics", "Allow your own touches to trigger your haptics (experimental)", () => false);

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_DIAGNOSTIC_LOGGING =
			new("enable_diagnostic_logging", "Enable detailed diagnostic logging for debugging (causes spam)", () => false);

		internal const int CONNECTION_TIMEOUT_MS = 10000;
		internal const int DEVICE_CHECK_CACHE_MS = 1000;
		internal const int MAX_RETRIES = 10;
		internal const bool AUTO_RECONNECT = true;

		public override void OnEngineInit() {
			Config = GetConfiguration();
			Config.Save(true);

			BHapticsConnection.Initialize();

			var harmony = new Harmony("com.nalathethird.bHapticsManager");
			harmony.PatchAll();
			
			if (!Config.GetValue(ENABLE_HOTPLUG)) {
				Warn("Hot-plug is DISABLED - devices must be connected before starting Resonite");
			}
			
			if (Config.GetValue(ENABLE_DIAGNOSTIC_LOGGING)) {
				Msg("Diagnostic logging ENABLED - expect verbose output");
			}
			
			FrooxEngine.Engine.Current.OnShutdown += OnEngineShutdown;
			
			Task.Run(async () => {
				try {
					if (Config.GetValue(ENABLE_DIAGNOSTIC_LOGGING)) {
						Msg("Worker thread startup task executing - waiting for haptic points...");
					}
					
					int retries = 0;
					const int maxRetries = 100;
					
					while (retries < maxRetries) {
						await Task.Delay(100);
						
						var engine = FrooxEngine.Engine.Current;
						if (engine?.InputInterface != null && engine.InputInterface.HapticPointCount > 0) {
							if (Config.GetValue(ENABLE_DIAGNOSTIC_LOGGING)) {
								Msg($"Haptic points registered (count: {engine.InputInterface.HapticPointCount}) after {retries * 100}ms");
							}
							break;
						}
						
						retries++;
					}
					
					if (retries >= maxRetries) {
						Warn("Timed out waiting for haptic points - worker thread may not function correctly");
					}
					
					await Task.Delay(500);
					
					if (_workerThreadStarted) {
						return;
					}
					
					BHapticsConnection.StartWorkerThread();
					_workerThreadStarted = true;
				}
				catch (Exception ex) {
					Error($"Error in worker thread startup task: {ex.Message}");
				}
			});
		}

		private void OnEngineShutdown() {
			try {
				try {
					ModernBHaptics.bHapticsManager.StopPlayingAll();
				} catch { }
				
				try {
					BHapticsConnection.Shutdown();
				} catch { }
				
				Thread.Sleep(100);
			}
			catch (Exception ex) {
				Error("Error during shutdown: " + ex.Message);
			}
		}

		public static void Error(Exception ex) {
			ResoniteMod.Error(ex);
		}
	}
}
