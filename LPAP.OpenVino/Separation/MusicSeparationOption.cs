namespace LPAP.OpenVino.Separation
{
	public sealed class MusicSeparationOptions
	{
		/// <summary>Target SR for demucs-like music separation models.</summary>
		public int TargetSampleRate { get; set; } = 44100;

		/// <summary>Target channel count (usually 2).</summary>
		public int TargetChannels { get; set; } = 2;

		/// <summary>
		/// If true: auto-resample/auto-channel-transform before infer.
		/// </summary>
		public bool AutoConvertAudioFormat { get; set; } = true;

		/// <summary>
		/// Frames per chunk (time axis length T). If 0 => derived from model input shape.
		/// </summary>
		public int ChunkFrames { get; set; } = 0;

		/// <summary>
		/// Overlap for chunk stitching (0..0.95). 0.25..0.5 recommended.
		/// </summary>
		public double OverlapFraction { get; set; } = 0.5;

		/// <summary>
		/// Batch size for batching chunks (only used if model input batch dimension supports it).
		/// </summary>
		public int BatchSize { get; set; } = 1;

		/// <summary>
		/// If true: try to batch chunks into one infer call when possible.
		/// </summary>
		public bool EnableBatching { get; set; } = true;

		/// <summary>
		/// Names of stems (used for output naming). If null => "stem0..".
		/// </summary>
		public string[]? StemNames { get; set; } = ["drums", "bass", "other", "vocals"];

		/// <summary>
		/// Optional: clamp outputs to [-1..1]
		/// </summary>
		public bool ClampOutput { get; set; } = true;

		/// <summary>
		/// Optional: normalize each stem peak to original peak (lightweight).
		/// </summary>
		public bool MatchInputPeak { get; set; } = false;
	}
}
