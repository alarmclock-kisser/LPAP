using LPAP.Audio;
using LPAP.Demucs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Forms.Adapters
{
    /// <summary>
    /// Adapter lives in LPAP.Forms (UI) to avoid circular references:
    /// Forms knows Audio + Onnx. Onnx must NOT know Audio.
    /// </summary>
    public sealed class DemucsAudioObjAdapter
    {
        private readonly DemucsService _service;

        public DemucsAudioObjAdapter(DemucsService service)
        {
            this._service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Chunked separation with accurate progress (chunk-based) and overlap-add using AudioObj.AggregateStretchedChunksAsync().
        ///
        /// Returns 4 AudioObj stems (drums, bass, other, vocals) with interleaved float[] data.
        /// </summary>
        public async Task<(AudioObj drums, AudioObj bass, AudioObj other, AudioObj vocals)> SeparateAsync(
            AudioObj input,
            double chunkSeconds = 6.0,
            float overlapFraction = 0.25f,
            IProgress<double>? progress = null,
            IProgress<(int done, int total)>? stepProgress = null,
            CancellationToken ct = default)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (input.Data is null || input.Data.Length == 0) throw new ArgumentException("AudioObj.Data is empty.", nameof(input));
            if (input.SampleRate <= 0) throw new ArgumentException("AudioObj.SampleRate must be > 0.", nameof(input));
            if (input.Channels <= 0) throw new ArgumentException("AudioObj.Channels must be > 0.", nameof(input));

            overlapFraction = Math.Clamp(overlapFraction, 0.0f, 0.95f);
            progress?.Report(0.0);

            // ---------- PREP: match model SR/Channels (auto) ----------
            // Work on a copy so original stays untouched
            var work = new AudioObj();
            work.CopyAudioObj(input);

            // reserve small progress slice for prep
            progress?.Report(0.01);

            int targetSr = this._service.Options.ExpectedSampleRate; // usually 44100
            int targetCh = this._service.Options.ExpectedChannels;   // usually 2

            if (work.Channels != targetCh)
            {
                await work.TransformChannelsAsync(targetCh).ConfigureAwait(false);
            }

            progress?.Report(0.02);

            if (work.SampleRate != targetSr)
            {
                await work.ResampleAsync(targetSr).ConfigureAwait(false);
            }

            progress?.Report(0.03);

            int sr = work.SampleRate;
            int channels = work.Channels;

            // ---------- CHUNK SIZE ----------
            int chunkFrames;
            if (this._service.Model.FixedInputFrames is int fixedT && fixedT > 0)
            {
                chunkFrames = fixedT;
            }
            else
            {
                chunkFrames = Math.Max(1, (int) Math.Round(chunkSeconds * sr));
            }

            int chunkSize = chunkFrames * channels;
            int overlapSize = (int) Math.Round(chunkSize * overlapFraction);

            // ---------- 1) Chunking ----------
            progress?.Report(0.035);

            var chunks = await work.GetChunksAsync(
                chunkSize,
                overlapFraction,
                maxWorkers: 0,
                keepData: true).ConfigureAwait(false);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException("Chunking produced no chunks.");
            }

            stepProgress?.Report((0, chunks.Count));
            progress?.Report(0.06);

            var drumsChunks = new List<float[]>(chunks.Count);
            var bassChunks = new List<float[]>(chunks.Count);
            var otherChunks = new List<float[]>(chunks.Count);
            var vocalsChunks = new List<float[]>(chunks.Count);

            static float[] GetStem(float[][] stems, int index)
            {
                if (stems is null) throw new InvalidOperationException("Model returned null stems.");
                if ((uint) index >= (uint) stems.Length) throw new InvalidOperationException($"Model returned only {stems.Length} stems, but index {index} was requested.");
                return stems[index] ?? throw new InvalidOperationException($"Model returned null for stem index {index}.");
            }

            // ---------- 2) Inference per chunk ----------
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                double p0 = 0.06 + 0.87 * (i / (double) chunks.Count);
                double p1 = 0.06 + 0.87 * ((i + 1) / (double) chunks.Count);
                progress?.Report(p0);

                float[][] stems = await this._service.SeparateInterleavedAsync(
                    chunks[i],
                    sr,
                    channels,
                    progress: null,
                    ct: ct).ConfigureAwait(false);

                // Convention (default): 0=drums, 1=bass, 2=other, 3=vocals
                drumsChunks.Add(GetStem(stems, 0));
                bassChunks.Add(GetStem(stems, 1));
                otherChunks.Add(GetStem(stems, 2));
                vocalsChunks.Add(GetStem(stems, 3));

                stepProgress?.Report((i + 1, chunks.Count));
                progress?.Report(p1);
            }

            // ---------- 3) Aggregate overlap-add ----------
            progress?.Report(0.93);

            var drums = CreateStem(input, "Drums", chunkSize, overlapFraction, overlapSize, targetSr, targetCh);
            var bass = CreateStem(input, "Bass", chunkSize, overlapFraction, overlapSize, targetSr, targetCh);
            var other = CreateStem(input, "Other", chunkSize, overlapFraction, overlapSize, targetSr, targetCh);
            var vocals = CreateStem(input, "Vocals", chunkSize, overlapFraction, overlapSize, targetSr, targetCh);

            drums.StretchFactor = bass.StretchFactor = other.StretchFactor = vocals.StretchFactor = 1.0;

            await drums.AggregateStretchedChunksAsync(drumsChunks, maxWorkers: 0, keepPointer: true).ConfigureAwait(false);
            progress?.Report(0.95);
            await bass.AggregateStretchedChunksAsync(bassChunks, maxWorkers: 0, keepPointer: true).ConfigureAwait(false);
            progress?.Report(0.97);
            await other.AggregateStretchedChunksAsync(otherChunks, maxWorkers: 0, keepPointer: true).ConfigureAwait(false);
            progress?.Report(0.99);
            await vocals.AggregateStretchedChunksAsync(vocalsChunks, maxWorkers: 0, keepPointer: true).ConfigureAwait(false);

            progress?.Report(1.0);
            return (drums, bass, other, vocals);
        }

        private static AudioObj CreateStem(AudioObj src, string suffix, int chunkSize, float overlapFraction, int overlapSize, int sr, int ch)
        {
            var ao = new AudioObj();
            ao.CopyAudioObj(src);

            ao.Name = $"{src.Name} - {suffix}";
            ao.Data = Array.Empty<float>();

            ao.SampleRate = sr;
            ao.Channels = ch;

            ao.ChunkSize = chunkSize;
            ao.Overlap = overlapFraction;
            ao.OverlapSize = overlapSize;

            return ao;
        }
    }
}
