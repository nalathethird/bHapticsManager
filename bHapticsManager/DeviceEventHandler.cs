// DeviceEventHandler.cs
// Handles all bHaptics device events (connect, disconnect, battery changes)

using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	public static class DeviceEventHandler {
		
		public static void Subscribe() {
			ModernBHaptics.bHapticsManager.DeviceStatusChanged += OnDeviceStatusChanged;
			ModernBHaptics.bHapticsManager.ConnectionEstablished += OnConnectionEstablished;
			ModernBHaptics.bHapticsManager.ConnectionLost += OnConnectionLost;
			ModernBHaptics.bHapticsManager.StatusChanged += OnStatusChanged;
			
			ResoniteMod.Msg("Event handlers subscribed successfully");
		}

		public static void OnDeviceStatusChanged(object sender, ModernBHaptics.DeviceStatusChangedEventArgs e) {
			try {
				string status = e.IsConnected ? "CONNECTED" : "DISCONNECTED";
				ResoniteMod.Msg($"[Event] Device {e.Position} {status}");
				
				BHapticsConnection.InvalidateDeviceCache(e.Position, e.IsConnected);
				
				var engine = FrooxEngine.Engine.Current;
				if (engine == null) {
					ResoniteMod.Warn("Engine not ready for device registration - will retry on next connection");
					return;
				}
				
				var config = bHapticsManager.Config;
				if (config == null) {
					ResoniteMod.Warn("Config not ready - skipping event handling");
					return;
				}
				
				if (e.IsConnected) {
					var legacyPosition = PositionMapper.MapModernToLegacy(e.Position);
					LegacyCompatibilityLayer.ResetDevice(legacyPosition);
					
					if (config.GetValue(bHapticsManager.ENABLE_HOTPLUG)) {
						_ = Task.Run(async () => {
							try {
								bool success = await DeviceRegistration.TryRegisterDeviceAsync(e.Position);
								if (success) {
									ResoniteMod.Msg($"Device {e.Position} registered and ready");
								} else {
									ResoniteMod.Warn($"Device {e.Position} registration failed");
								}
							}
							catch (Exception ex) {
								ResoniteMod.Error($"Error registering device {e.Position}: {ex.Message}");
							}
						});
					}
					else {
						ResoniteMod.Warn($"Device {e.Position} connected, but hot-plug is disabled in config");
					}
				}
				else {
					_ = Task.Run(async () => {
						try {
							await DeviceRegistration.UnregisterDeviceAsync(e.Position);
							
							try {
								ModernBHaptics.bHapticsManager.StopPlayingAll();
							} catch { }
							
							var legacyPosition = PositionMapper.MapModernToLegacy(e.Position);
							LegacyCompatibilityLayer.CleanupDevice(legacyPosition);
							
							ResoniteMod.Msg($"Device {e.Position} disconnected and cleaned up");
						}
						catch (Exception ex) {
							ResoniteMod.Error($"Error during device {e.Position} cleanup: {ex.Message}");
						}
					});
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnDeviceStatusChanged: {ex.Message}");
			}
		}

		public static void OnConnectionEstablished(object sender, EventArgs e) {
			try {
				ResoniteMod.Msg("Connection to bHaptics Player ESTABLISHED");
				var deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
				ResoniteMod.Msg($"Detected {deviceCount} device(s)");
				
				foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
					if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
						ResoniteMod.Msg($"  - {pos}");
					}
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnConnectionEstablished: {ex.Message}");
			}
		}

		public static void OnConnectionLost(object sender, EventArgs e) {
			try {
				ResoniteMod.Warn("Connection to bHaptics Player LOST");
				ResoniteMod.Warn("Haptics will not work until connection is re-established");
				if (bHapticsManager.AUTO_RECONNECT) {
					ResoniteMod.Warn("Auto-reconnect is enabled - waiting for reconnection...");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnConnectionLost: {ex.Message}");
			}
		}

		public static void OnStatusChanged(object sender, ModernBHaptics.ConnectionStatusChangedEventArgs e) {
			try {
				if (bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
					ResoniteMod.Msg($"Status changed: {e.PreviousStatus} -> {e.NewStatus}");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnStatusChanged: {ex.Message}");
			}
		}
	}
}
