using LPAP.Onnx.Memory;

namespace LPAP.Onnx.Demucs;

public sealed class DemucsService : IAsyncDisposable
{
    private readonly DemucsModel _model;
    private readonly TensorPool _pool = new();

    public DemucsService(DemucsModel model)
    {
        this._model = model;
    }

    /// <summary>
    /// Separates an interleaved float buffer into 4 stems (drums, bass, other, vocals).
    /// Returns interleaved buffers per stem.
    /// </summary>
    public async Task<DemucsResult> SeparateInterleavedAsync(
    ReadOnlyMemory<float> inputInterleaved,
    int sampleRate,
    int channels,
    IProgress<double>? progress = null,
    CancellationToken ct = default)
    {
        progress?.Report(0.0);
        // output planar [S,C,T]
        var planar = await this._model.SeparateAsync(inputInterleaved, sampleRate, channels, ct).ConfigureAwait(false);

        // Determine shape. We assume S=StemCount, C=channels, T=frames.
        var frames = inputInterleaved.Length / channels;
        var S = this._model.Options.StemCount;
        var C = channels;
        var T = frames;

        // Convert planar stems -> interleaved per stem
        float[] StemToInterleaved(int stemIndex)
        {
            var dst = new float[T * C];
            // planar layout [S,C,T] => index = stem*C*T + c*T + t
            int stemBase = stemIndex * C * T;
            for (int t = 0; t < T; t++)
            {
                for (int c = 0; c < C; c++)
                {
                    dst[t * C + c] = planar[stemBase + c * T + t];
                }
            }
            return dst;
        }

        return new DemucsResult(
            Drums: StemToInterleaved(0),
            Bass: StemToInterleaved(1),
            Other: StemToInterleaved(2),
            Vocals: StemToInterleaved(3),
            SampleRate: sampleRate,
            Channels: channels);
    }

    public ValueTask DisposeAsync() => this._model.DisposeAsync();
}

public sealed record DemucsResult(float[] Drums, float[] Bass, float[] Other, float[] Vocals, int SampleRate, int Channels);
