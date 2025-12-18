#if LPAP_AUDIO
using LPAP.Audio;

namespace LPAP.Onnx.Demucs;

public static class DemucsAudioObjAdapter
{
    public static async Task<(AudioObj drums, AudioObj bass, AudioObj other, AudioObj vocals)> SeparateAsync(
        this DemucsService svc,
        AudioObj input,
        CancellationToken ct = default)
    {
        if (input.Data is null || input.Data.Length == 0)
            throw new ArgumentException("AudioObj.Data is empty. Ensure the audio is loaded into memory.");

        var res = await svc.SeparateInterleavedAsync(input.Data, input.SampleRate, input.Channels, ct).ConfigureAwait(false);

        AudioObj CloneWith(string nameSuffix, float[] data)
        {
            var ao = input.CopyAudioObj(); // your helper copies meta but keeps it independent
            ao.Name = $"{input.Name} - {nameSuffix}";
            ao.SampleRate = res.SampleRate;
            ao.Channels = res.Channels;
            ao.BitDepth = input.BitDepth;
            ao.Data = data;
            return ao;
        }

        return (
            CloneWith("Drums", res.Drums),
            CloneWith("Bass", res.Bass),
            CloneWith("Other", res.Other),
            CloneWith("Vocals", res.Vocals)
        );
    }
}
#endif
