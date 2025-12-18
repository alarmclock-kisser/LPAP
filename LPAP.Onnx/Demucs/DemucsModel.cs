using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using LPAP.Onnx.Runtime;

namespace LPAP.Onnx.Demucs;

public sealed class DemucsModel : IAsyncDisposable
{
    public InferenceSession Session { get; }
    public OnnxRunner Runner { get; }
    public DemucsOptions Options { get; }

    public string InputName { get; }
    public string OutputName { get; }

    public DemucsModel(DemucsOptions demucsOptions, OnnxOptions onnxOptions)
    {
        this.Options = demucsOptions;

        var modelPath = DemucsOptions.ResolveModelPath(demucsOptions.ModelPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Demucs ONNX model not found: {modelPath}");

        var so = OnnxSessionFactory.CreateSessionOptions(onnxOptions);
        this.Session = new InferenceSession(modelPath, so);

        this.Runner = new OnnxRunner(this.Session, onnxOptions.WorkerCount, onnxOptions.QueueCapacity);

        // Auto-detect names if not specified
        this.InputName = demucsOptions.InputName ?? this.Session.InputMetadata.Keys.First();
        this.OutputName = demucsOptions.OutputName ?? this.Session.OutputMetadata.Keys.First();
    }

    /// <summary>
    /// Run Demucs inference. Assumes input shape [1,C,T] float32 and output [1,S,C,T] float32.
    /// </summary>
    public async Task<float[]> SeparateAsync(
        ReadOnlyMemory<float> interleavedInput,
        int sampleRate,
        int channels,
        CancellationToken ct = default)
    {
        if (sampleRate != this.Options.ExpectedSampleRate)
            throw new ArgumentException($"SampleRate mismatch. Expected {this.Options.ExpectedSampleRate}, got {sampleRate}. Resample first.");
        if (channels != this.Options.ExpectedChannels)
            throw new ArgumentException($"Channels mismatch. Expected {this.Options.ExpectedChannels}, got {channels}. Convert first.");

        var frames = interleavedInput.Length / channels;
        if (frames <= 0) throw new ArgumentException("Empty audio.");

        // Convert interleaved [T*C] to planar [C,T] expected by typical Demucs ONNX exports
        var planar = new float[channels * frames];
        var src = interleavedInput.Span;
        for (int t = 0; t < frames; t++)
        {
            for (int c = 0; c < channels; c++)
            {
                planar[c * frames + t] = src[t * channels + c];
            }
        }

        // Build tensor [1,C,T]
        var tensor = new DenseTensor<float>(planar, new[] { 1, channels, frames });

        var input = NamedOnnxValue.CreateFromTensor(this.InputName, tensor);
        using var outputs = await this.Runner.RunAsync(new[] { input }, ct).ConfigureAwait(false);

        var outVal = outputs.First(o => o.Name == this.OutputName);
        var outTensor = outVal.AsTensor<float>();

        // Flatten to array. We'll return planar stems as a flat array in layout [S,C,T] (planar).
        // Some ORT versions give direct buffer; ToArray ensures a stable copy.
        return outTensor.ToArray();
    }

    public ValueTask DisposeAsync() => this.Runner.DisposeAsync();
}
