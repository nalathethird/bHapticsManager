// DiagnosticPatches.cs
// Diagnostic patches to help debug VestBack and haptic triggering issues
// These patches are ONLY active if enable_diagnostic_logging is true in config

using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	/// <summary>
	/// Patch BHapticsDriver.InitializeBhaptics to log detailed device detection info
	/// </summary>
	[HarmonyPatch(typeof(BHapticsDriver), "InitializeBhaptics")]
	public class BHapticsDriverInitPatch {
		static void Postfix(BHapticsDriver __instance) {
			// Only log if diagnostic logging is enabled
			if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
				return;
			}
			
			try {
				// Log all detected devices with their PositionID mapping
				ResoniteMod.Msg("=== bHaptics Device Detection ===");
				
				foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
					bool connected = ModernBHaptics.bHapticsManager.IsDeviceConnected(pos);
					if (connected) {
						var legacy = PositionMapper.MapModernToLegacy(pos);
						ResoniteMod.Msg($"? {pos} (Legacy: {legacy}) - CONNECTED");
					}
				}
				
				// Specifically check VestBack mapping
				bool vestBackModern = ModernBHaptics.bHapticsManager.IsDeviceConnected(ModernBHaptics.PositionID.VestBack);
				bool vestBackLegacy = ModernBHaptics.bHapticsManager.IsDeviceConnected(
					PositionMapper.MapLegacyToModern(LegacyBHaptics.PositionType.VestBack)
				);
				
				ResoniteMod.Msg($"VestBack check: Modern={vestBackModern}, Legacy mapped={vestBackLegacy}");
				ResoniteMod.Msg("=================================");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in device detection diagnostic: {ex.Message}");
			}
		}
	}
	
	/// <summary>
	/// Patch HapticPoint.SampleSources to log when sources are sampled (for debugging weird triggers)
	/// </summary>
	[HarmonyPatch(typeof(HapticPoint), "SampleSources")]
	public class HapticPointSampleDiagnosticPatch {
		private static DateTime _lastLog = DateTime.MinValue;
		private static readonly Dictionary<int, (float force, float temp, float pain, float vib)> _lastValues = new();
		
		static void Postfix(HapticPoint __instance) {
			// Only log if diagnostic logging is enabled
			if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
				return;
			}
			
			try {
				// Only log when values change significantly (to avoid spam)
				int index = __instance.Index;
				float force = __instance.Force;
				float temp = __instance.Temperature;
				float pain = __instance.Pain;
				float vib = __instance.Vibration;
				
				// Check if values changed
				bool changed = false;
				if (_lastValues.TryGetValue(index, out var last)) {
					float deltaF = Math.Abs(force - last.force);
					float deltaT = Math.Abs(temp - last.temp);
					float deltaP = Math.Abs(pain - last.pain);
					float deltaV = Math.Abs(vib - last.vib);
					
					// Log if any value changed by more than 0.1
					changed = deltaF > 0.1f || deltaT > 0.1f || deltaP > 0.1f || deltaV > 0.1f;
				} else {
					changed = force > 0 || temp != 0 || pain > 0 || vib > 0;
				}
				
				if (changed) {
					_lastValues[index] = (force, temp, pain, vib);
					
					// Only log occasionally to prevent spam
					if ((DateTime.Now - _lastLog).TotalSeconds > 2) {
						ResoniteMod.Msg($"[Haptic#{index}] F={force:F2} T={temp:F2} P={pain:F2} V={vib:F2} | Position={__instance.Position}");
						_lastLog = DateTime.Now;
					}
				}
			}
			catch {
				// Silent fail - this is just diagnostics
			}
		}
	}
	
	/// <summary>
	/// Patch DirectTagHapticSource.GetIntensity to log when it provides haptic data
	/// This helps debug TipTouchSource issues
	/// </summary>
	[HarmonyPatch(typeof(DirectTagHapticSource), "GetIntensity")]
	public class DirectTagHapticSourceDiagnosticPatch {
		private static DateTime _lastLog = DateTime.MinValue;
		
		static void Postfix(DirectTagHapticSource __instance, SensationClass sensation, float __result) {
			// Only log if diagnostic logging is enabled
			if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
				return;
			}
			
			try {
				// Only log when there's actual intensity and occasionally
				if (__result > 0f && (DateTime.Now - _lastLog).TotalSeconds > 2) {
					ResoniteMod.Msg($"[DirectTag] Tag='{__instance.HapticTag.Value}' Sensation={sensation} Intensity={__result:F2}");
					_lastLog = DateTime.Now;
				}
			}
			catch {
				// Silent fail
			}
		}
	}
}
