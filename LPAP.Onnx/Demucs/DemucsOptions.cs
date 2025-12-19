namespace LPAP.Onnx.Demucs;

public sealed record DemucsOptions
{
    /// <summary>
    /// Path to the ONNX model. If null, uses env var LPAP_DEMUCS_MODEL, then fallback to D:\Models\htdemucs_6s.onnx
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// Expected sample rate for the model. Many Demucs models use 44100 Hz.
    /// </summary>
    public int ExpectedSampleRate { get; init; } = 44100;

    /// <summary>
    /// Expected channels (usually 2).
    /// </summary>
    public int ExpectedChannels { get; init; } = 2;

    /// <summary>
    /// Optional: fixed number of input frames (T) the model expects for one forward pass.
    /// If 0, the code will try to infer it from the ONNX input shape metadata and otherwise fall back to 6 seconds.
    /// </summary>
    public int FixedInputFrames { get; init; } = 0;

    /// <summary>
    /// Some ONNX exports accept raw waveform and output stems directly.
    /// This adapter assumes:
    ///  - one input tensor: float32 [1, C, T]
    ///  - one output tensor: float32 [1, S, C, T]  (S=4)
    /// If your model differs, adjust the model adapter.
    /// </summary>
    public string? InputName { get; init; } = null;
    public string? OutputName { get; init; } = null;

    /// <summary>
    /// Number of stems expected (usually 4: drums, bass, other, vocals).
    /// </summary>
    public int StemCount { get; init; } = 4;

    /// <summary>
    /// Human stem names in the expected order of the model output (index -> stem).
    /// Default follows Demucs convention: drums, bass, other, vocals.
    /// </summary>
    public string[] StemNames { get; init; } = new[] { "drums", "bass", "other", "vocals" };

    /// <summary>
    /// FFT size used by the hybrid STFT branch (e.g., 4096 for htdemucs). Set to 0 to auto-detect.
    /// </summary>
    public int HybridStftFftSize { get; init; } = 4096;

    /// <summary>
    /// Hop length for ISTFT reconstruction (e.g., 1024 for htdemucs). Set to 0 to derive from tensors.
    /// </summary>
    public int HybridStftHopLength { get; init; } = 1024;

    /// <summary>
    /// When true the ISTFT output is center-cropped to match the requested frame count.
    /// </summary>
    public bool HybridStftCenter { get; init; } = true;

    public static string ResolveModelPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        var env = Environment.GetEnvironmentVariable("LPAP_DEMUCS_MODEL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env!;
        }

        return @"D:\Models\htdemucs_6s.onnx";
    }
}
