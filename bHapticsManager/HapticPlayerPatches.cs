// HapticPlayerPatches.cs
// Harmony patches that allow the legacy HapticPlayer to initialize
// but prevent it from connecting to bHaptics Player (we handle connection ourselves)

using Elements.Core;
using HarmonyLib;
using ResoniteModLoader;
using System.Reflection;
using FrooxEngine;

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// <summary>
	/// CRITICAL: Blocks WebSocketSender creation to prevent duplicate connection!
	/// FrooxEngine's HapticPlayer would create its own connection, but we want only OUR connection.
	/// NOTE: This patch might fail if WebSocketSender is not accessible - that's OK, we have fallbacks.
	/// </summary>
	[HarmonyPatch]
	public class WebSocketSenderConstructorPatch {
		static MethodBase TargetMethod() {
			try {
				// Find WebSocketSender type (it's an inner class)
				var hapticPlayerType = typeof(LegacyBHaptics.HapticPlayer);
				
				// Try multiple possible names for the nested type
				Type senderType = null;
				foreach (var name in new[] { "WebSocketSender", "WebSocketConnection", "Sender", "_sender" }) {
					senderType = hapticPlayerType.GetNestedType(name, BindingFlags.NonPublic | BindingFlags.Public);
					if (senderType != null) {
						ResoniteMod.Msg($"Found nested type: {name}");
						break;
					}
				}
				
				if (senderType == null) {
					// Try to find it in the assembly
					var assembly = hapticPlayerType.Assembly;
					foreach (var type in assembly.GetTypes()) {
						if (type.Name.Contains("Sender") || type.Name.Contains("Socket")) {
							ResoniteMod.Msg($"Found potential sender type in assembly: {type.FullName}");
							senderType = type;
							break;
						}
					}
				}
				
				if (senderType == null) {
					ResoniteMod.Warn("Could not find WebSocketSender type - patch will be skipped (this is OK, we have fallbacks)");
					return null;
				}
				
				// Find constructor
				var constructor = senderType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
				if (constructor == null) {
					ResoniteMod.Warn($"Could not find constructor for {senderType.Name} - patch will be skipped (this is OK)");
					return null;
				}
				
				ResoniteMod.Msg($"? Found and will patch {senderType.Name} constructor");
				return constructor;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error finding WebSocketSender: {ex.Message}");
				return null;
			}
		}
		
		static bool Prefix() {
			// Block WebSocketSender creation completely!
			// This prevents FrooxEngine from creating its own connection to bHaptics Player
			ResoniteMod.Msg("? Blocked WebSocketSender constructor (preventing duplicate connection)");
			return false;
		}
		
		// ALSO add exception handler to ensure it fails gracefully
		static Exception Finalizer(Exception __exception) {
			if (__exception != null) {
				ResoniteMod.Msg("? WebSocketSender constructor threw exception (expected - we blocked it)");
				return null; // Suppress the exception
			}
			return __exception;
		}
	}
	
	/// <summary>
	/// CRITICAL: Patches BHapticsDriver.RegisterInputs to stop the worker thread!
	/// This is where FrooxEngine starts its competing worker thread.
	/// We need to stop it AFTER it initializes devices but BEFORE it starts the worker.
	/// </summary>
	[HarmonyPatch(typeof(BHapticsDriver), "RegisterInputs")]
	public class BHapticsDriverRegisterInputsPatch {
		static void Postfix(BHapticsDriver __instance) {
			try {
				// The worker field is created in InitializeBhaptics and started in RegisterInputs
				// We need to stop it using reflection
				var workerField = typeof(BHapticsDriver).GetField("worker", BindingFlags.NonPublic | BindingFlags.Instance);
				if (workerField != null) {
					var worker = workerField.GetValue(__instance);
					if (worker != null) {
						// Try to stop the worker
						var stopMethod = worker.GetType().GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
						if (stopMethod != null) {
							stopMethod.Invoke(worker, null);
							ResoniteMod.Msg("? Stopped FrooxEngine's BHapticsDriver worker thread");
						} else {
							ResoniteMod.Warn("Could not find Stop method on worker - trying Dispose");
							var disposeMethod = worker.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance);
							if (disposeMethod != null) {
								disposeMethod.Invoke(worker, null);
								ResoniteMod.Msg("? Disposed FrooxEngine's BHapticsDriver worker thread");
							}
						}
					} else {
						ResoniteMod.Msg("Worker field is null - FrooxEngine's worker didn't start (this is OK)");
					}
				} else {
					ResoniteMod.Warn("Could not find worker field on BHapticsDriver");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Warn($"Could not stop BHapticsDriver worker: {ex.Message}");
			}
		}
	}
	
	/// <summary>
	/// CRITICAL: Force _sender to null in ALL HapticPlayer constructors!
	/// This is the ultimate fallback if WebSocketSender constructor patch fails.
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(Action<bool>), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch {
		static void Postfix(LegacyBHaptics.HapticPlayer __instance) {
			try {
				// FORCE _sender to null
				var senderField = typeof(LegacyBHaptics.HapticPlayer).GetField("_sender", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (senderField != null) {
					senderField.SetValue(__instance, null);
					ResoniteMod.Msg("? HapticPlayer constructed - forced _sender to null (no duplicate connection)");
				} else {
					ResoniteMod.Warn("Could not find _sender field to nullify");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in HapticPlayer constructor patch: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// CRITICAL: Force _sender to null in overload constructor too!
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch2 {
		static void Postfix(LegacyBHaptics.HapticPlayer __instance) {
			try {
				var senderField = typeof(LegacyBHaptics.HapticPlayer).GetField("_sender", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (senderField != null) {
					senderField.SetValue(__instance, null);
					ResoniteMod.Msg("? HapticPlayer constructed (overload) - forced _sender to null");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in HapticPlayer constructor patch (overload): {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Allows Dispose() to run safely (nothing to dispose since sender is null)
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Dispose")]
	public class HapticPlayerDisposePatch {
		static bool Prefix() {
			// Let it run - there's nothing to dispose since sender is null
			return true;
		}
	}

	/// <summary>
	/// CRITICAL: Intercepts IsActive() to return modern device connection status!
	/// This is what FrooxEngine uses to determine which devices to initialize and send data to!
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
	public class HapticPlayerIsActivePatch {
		static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result) {
			try {
				// Map legacy position to modern
				var modernPosition = PositionMapper.MapLegacyToModern(type);
				
				// Check if device is connected using modern API
				__result = ModernBHaptics.bHapticsManager.IsDeviceConnected(modernPosition);
				
				return false; // Skip original method
			}
			catch {
				__result = false;
				return false;
			}
		}
	}
	
	/// <summary>
	/// CRITICAL: Intercepts Submit() to prevent the legacy SDK from trying to send data!
	/// Our ModernBHapticsWorkerThread handles all submission via LegacyCompatibilityLayer.
	/// This patch ensures FrooxEngine's worker thread doesn't call the broken WebSocket.
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Submit", new Type[] { 
		typeof(string), 
		typeof(LegacyBHaptics.PositionType), 
		typeof(System.Collections.Generic.List<LegacyBHaptics.DotPoint>), 
		typeof(int) 
	})]
	public class HapticPlayerSubmitPatch {
		static bool Prefix() {
			// Block the legacy Submit() - our ModernBHapticsWorkerThread handles all submission
			// FrooxEngine's worker thread will call this, but we ignore it
			return false;
		}
	}

	/// <summary>
	/// Intercepts TurnOff(key) to immediately stop specific haptic patterns.
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "TurnOff", new Type[] { typeof(string) })]
	public class HapticPlayerTurnOffKeyPatch {
		static bool Prefix(string key) {
			try {
				ModernBHaptics.bHapticsManager.StopPlaying(key);
				return false;
			}
			catch {
				return false;
			}
		}
	}

	/// <summary>
	/// Intercepts TurnOff() to immediately stop ALL haptic patterns.
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "TurnOff", new Type[0])]
	public class HapticPlayerTurnOffAllPatch {
		static bool Prefix() {
			try {
				ModernBHaptics.bHapticsManager.StopPlayingAll();
				return false;
			}
			catch {
				return false;
			}
		}
	}
}
