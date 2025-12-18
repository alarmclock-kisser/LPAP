namespace LPAP.Onnx.Demucs;

public sealed record DemucsOptions
{
    /// <summary>
    /// Path to the ONNX model. If null, uses env var LPAP_DEMUCS_MODEL, then fallback to D:\Models\htdemucs_6s.onnx
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// Expected sample rate for the model. Many Demucs models use 44100 Hz. If your model expects another SR,
    /// resample before calling.
    /// </summary>
    public int ExpectedSampleRate { get; init; } = 44100;

    /// <summary>
    /// Expected channels (usually 2).
    /// </summary>
    public int ExpectedChannels { get; init; } = 2;

    /// <summary>
    /// Some ONNX exports accept raw waveform and output stems directly.
    /// This adapter assumes:
    ///  - one input tensor: float32 [1, C, T]
    ///  - one output tensor: float32 [1, S, C, T]  (S=4)
    /// If your model differs, use ModelInspector output and adjust DemucsModelAdapter.
    /// </summary>
    public string? InputName { get; init; } = null;
    public string? OutputName { get; init; } = null;

    /// <summary>
    /// Number of stems expected (usually 4: drums, bass, other, vocals).
    /// </summary>
    public int StemCount { get; init; } = 4;

    public static string ResolveModelPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured!;
        var env = Environment.GetEnvironmentVariable("LPAP_DEMUCS_MODEL");
        if (!string.IsNullOrWhiteSpace(env)) return env!;
        return @"D:\Models\htdemucs_6s.onnx";
    }
}
