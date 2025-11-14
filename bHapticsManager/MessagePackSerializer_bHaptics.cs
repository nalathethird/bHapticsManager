// MessagePackSerializer_bHaptics.cs
// Extension methods for serializing bHaptics protocol messages with MessagePack
// Replaces JSON serialization in bHapticsLib for 10x performance improvement

using MessagePack;
using MessagePack.Resolvers;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// <summary>
	/// MessagePack serialization helpers for bHaptics protocol.
	/// Provides optimized binary serialization for WebSocket communication.
	/// </summary>
	public static class MessagePackSerializer_bHaptics {
		
		// Cache serializer options for performance
		private static readonly MessagePackSerializerOptions _options;
		
		static MessagePackSerializer_bHaptics() {
			// Use high-performance resolver
			_options = MessagePackSerializerOptions.Standard
				.WithResolver(ContractlessStandardResolver.Instance)
				.WithCompression(MessagePackCompression.Lz4BlockArray); // Optional: 50-80% size reduction for large payloads
		}
		
		/// <summary>
		/// Serializes a frame submission request to MessagePack binary format.
		/// </summary>
		/// <param name="key">Pattern key (will be hashed for compactness)</param>
		/// <param name="durationMillis">Duration in milliseconds</param>
		/// <param name="position">Device position ID</param>
		/// <param name="dotPoints">Sparse motor representation (recommended for most cases)</param>
		/// <param name="pathPoints">Path points for animated effects (optional)</param>
		/// <param name="mirrorDirection">Mirror direction (optional)</param>
		/// <returns>Binary MessagePack payload (~40 bytes typical)</returns>
		public static byte[] SerializeSubmitFrame(
			string key,
			int durationMillis,
			ModernBHaptics.PositionID position,
			List<ModernBHaptics.DotPoint> dotPoints,
			List<ModernBHaptics.PathPoint> pathPoints = null,
			ModernBHaptics.MirrorDirection mirrorDirection = ModernBHaptics.MirrorDirection.None) {
			
			// Convert to optimized MessagePack structs
			var request = new bHapticsLib.SubmitFrameRequest {
				MessageType = (byte)bHapticsLib.MessageType.Frame,
				KeyHash = bHapticsLib.KeyHasher.Hash(key),
				DurationMillis = (ushort)Math.Min(durationMillis, 65535),
				PositionID = (byte)position,
				MirrorDirection = (byte)mirrorDirection
			};
			
			// Convert DotPoints (sparse representation - recommended)
			if (dotPoints != null && dotPoints.Count > 0) {
				request.DotPoints = new bHapticsLib.DotPoint[dotPoints.Count];
				for (int i = 0; i < dotPoints.Count; i++) {
					request.DotPoints[i] = new bHapticsLib.DotPoint {
						Index = (byte)dotPoints[i].Index,
						Intensity = (byte)Math.Min(dotPoints[i].Intensity, 200)
					};
				}
			}
			
			// Convert PathPoints (rare)
			if (pathPoints != null && pathPoints.Count > 0) {
				request.PathPoints = new bHapticsLib.PathPoint[pathPoints.Count];
				for (int i = 0; i < pathPoints.Count; i++) {
					request.PathPoints[i] = new bHapticsLib.PathPoint {
						X = (byte)(pathPoints[i].X * 255f),
						Y = (byte)(pathPoints[i].Y * 255f),
						Intensity = (byte)Math.Min(pathPoints[i].Intensity, 200),
						TimeOffsetMillis = (ushort)Math.Min(pathPoints[i].MotorCount, 65535) // Note: MotorCount repurposed as time offset
					};
				}
			}
			
			// Serialize to MessagePack
			return MessagePackSerializer.Serialize(request, _options);
		}
		
		/// <summary>
		/// Serializes a frame using dense motor array format (all motors).
		/// Use this when most motors are active (less common).
		/// </summary>
		public static byte[] SerializeSubmitFrameDense(
			string key,
			int durationMillis,
			ModernBHaptics.PositionID position,
			byte[] motors,
			ModernBHaptics.MirrorDirection mirrorDirection = ModernBHaptics.MirrorDirection.None) {
			
			var request = new bHapticsLib.SubmitFrameRequest {
				MessageType = (byte)bHapticsLib.MessageType.Frame,
				KeyHash = bHapticsLib.KeyHasher.Hash(key),
				DurationMillis = (ushort)Math.Min(durationMillis, 65535),
				PositionID = (byte)position,
				Motors = motors,
				MirrorDirection = (byte)mirrorDirection
			};
			
			return MessagePackSerializer.Serialize(request, _options);
		}
		
		/// <summary>
		/// Serializes a batch submission (multiple frames at once).
		/// Useful for pre-programmed sequences or network catch-up.
		/// </summary>
		public static byte[] SerializeBatch(params bHapticsLib.SubmitFrameRequest[] frames) {
			var batch = new bHapticsLib.BatchSubmitRequest {
				MessageType = (byte)bHapticsLib.MessageType.Batch,
				Frames = frames
			};
			
			return MessagePackSerializer.Serialize(batch, _options);
		}
		
		/// <summary>
		/// Serializes a stop playing command.
		/// </summary>
		public static byte[] SerializeStopPlaying(string key = null) {
			var request = new bHapticsLib.StopPlayingRequest {
				MessageType = (byte)bHapticsLib.MessageType.Stop,
				KeyHash = string.IsNullOrEmpty(key) ? (ushort)0 : bHapticsLib.KeyHasher.Hash(key),
				FullKey = key // Include full key for exact match
			};
			
			return MessagePackSerializer.Serialize(request, _options);
		}
		
		/// <summary>
		/// Deserializes device status response from bHaptics Player 2.
		/// </summary>
		public static bHapticsLib.DeviceStatusResponse DeserializeDeviceStatus(byte[] data) {
			return MessagePackSerializer.Deserialize<bHapticsLib.DeviceStatusResponse>(data, _options);
		}
		
		/// <summary>
		/// Extension method: Convert legacy DotPoint list to MessagePack format.
		/// </summary>
		public static bHapticsLib.DotPoint[] ToMessagePack(this List<LegacyBHaptics.DotPoint> legacyPoints) {
			if (legacyPoints == null || legacyPoints.Count == 0)
				return null;
			
			var result = new bHapticsLib.DotPoint[legacyPoints.Count];
			for (int i = 0; i < legacyPoints.Count; i++) {
				result[i] = new bHapticsLib.DotPoint {
					Index = (byte)legacyPoints[i].Index,
					Intensity = (byte)Math.Min(legacyPoints[i].Intensity, 200)
				};
			}
			return result;
		}
		
		/// <summary>
		/// Get estimated payload size for performance profiling.
		/// </summary>
		public static int EstimatePayloadSize(int activeDotPoints, bool hasPathPoints = false) {
			// Base overhead: ~10 bytes
			// Per dot point: ~3 bytes (index + intensity + overhead)
			// Path points: ~6 bytes each
			int baseSize = 10;
			int dotPointSize = activeDotPoints * 3;
			int pathPointSize = hasPathPoints ? 30 : 0; // Assume ~5 path points average
			
			return baseSize + dotPointSize + pathPointSize;
		}
	}
	
	/// <summary>
	/// Performance metrics for monitoring MessagePack efficiency.
	/// </summary>
	public static class MessagePackMetrics {
		private static long _totalBytesSent = 0;
		private static long _totalMessagesSent = 0;
		private static DateTime _lastReset = DateTime.Now;
		
		public static void RecordMessage(int bytesSent) {
			_totalBytesSent += bytesSent;
			_totalMessagesSent++;
		}
		
		public static (long bytes, long messages, double avgSize, double kbPerSec) GetStats() {
			double elapsed = (DateTime.Now - _lastReset).TotalSeconds;
			double avgSize = _totalMessagesSent > 0 ? (double)_totalBytesSent / _totalMessagesSent : 0;
			double kbPerSec = elapsed > 0 ? (_totalBytesSent / 1024.0) / elapsed : 0;
			
			return (_totalBytesSent, _totalMessagesSent, avgSize, kbPerSec);
		}
		
		public static void Reset() {
			_totalBytesSent = 0;
			_totalMessagesSent = 0;
			_lastReset = DateTime.Now;
		}
	}
}
