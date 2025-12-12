// LPAP.Audio/AudioObj.IO.cs
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;

namespace LPAP.Audio
{
    public partial class AudioObj
    {
        public async Task LoadFromFileAsync(string? filePath = null, int? maxWorkers = null, CancellationToken ct = default)
        {
            filePath ??= this.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Audio file not found.", filePath);
            }

            this.FilePath = filePath;
            this.Name = Path.GetFileNameWithoutExtension(filePath);

            await Task.Run(() =>
            {
                using var reader = new AudioFileReader(filePath);
                var format = reader.WaveFormat;

                this.SampleRate = format.SampleRate;
                this.Channels = format.Channels;
                this.BitDepth = format.BitsPerSample; // 32

                int bytesPerSample = format.BitsPerSample / 8;
                long totalSamplesEstimate = reader.Length / bytesPerSample;
                if (totalSamplesEstimate <= 0)
                {
                    totalSamplesEstimate = 4096;
                }

                if (totalSamplesEstimate > int.MaxValue)
                {
                    throw new InvalidOperationException("Audio file is too large.");
                }

                int capacity = (int) totalSamplesEstimate;
                var data = new float[capacity];
                var buffer = new float[4096];
                int offset = 0;

                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    if (offset + read > data.Length)
                    {
                        int newSize = Math.Max(data.Length * 2, offset + read);
                        Array.Resize(ref data, newSize);
                    }

                    Array.Copy(buffer, 0, data, offset, read);
                    offset += read;
                }

                if (offset != data.Length)
                {
                    Array.Resize(ref data, offset);
                }

                this.Data = data;
            }, ct).ConfigureAwait(false);

            this.DataChanged();
        }


        public AudioObj Clone(bool keepId = false)
        {
            var clone = new AudioObj();

            // Copy settable properties via reflection
            var props = typeof(AudioObj)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanWrite && p.GetSetMethod(true) != null);

            foreach (var p in props)
            {
                // Skip problematic properties that should not be copied
                if (string.Equals(p.Name, nameof(this.Id), StringComparison.Ordinal))
                {
                    continue; // Id is readonly
                }

                if (string.Equals(p.Name, nameof(this.PlaybackTracking), StringComparison.Ordinal))
                {
                    continue; // internal runtime hookup
                }

                var value = p.GetValue(this);

                // Deep copy for float[] Data
                if (string.Equals(p.Name, nameof(this.Data), StringComparison.Ordinal) && value is float[] src)
                {
                    var copy = new float[src.Length];
                    Array.Copy(src, copy, src.Length);
                    p.SetValue(clone, copy);
                    continue;
                }

                // For other reference types, perform shallow copy
                p.SetValue(clone, value);
            }

            // Optionally preserve Id by setting backing field via reflection
            if (keepId)
            {
                var idField = typeof(AudioObj).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (idField != null)
                {
                    idField.SetValue(clone, this.Id);
                }
            }

            return clone;
        }

        public async Task<AudioObj> CloneAsync(bool keepId = false, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return this.Clone(keepId);
            }, ct).ConfigureAwait(false);
        }

        public AudioObj CopyFromSelection(long startSample, long endSample)
        {
            if (this.Data == null || this.Data.Length == 0)
            {
                throw new InvalidOperationException("No audio data to copy from.");
            }
            if (startSample < 0 || endSample > this.Data.Length || startSample >= endSample)
            {
                throw new ArgumentOutOfRangeException("Invalid selection range.");
            }

            var copy = new AudioObj
            {
                SampleRate = this.SampleRate,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Name = this.Name + "_Copy"
            };
            
            long length = endSample - startSample;
            copy.Data = new float[length];
            Array.Copy(this.Data, startSample, copy.Data, 0, length);
            
            return copy;
        }

        public async Task<AudioObj> CopyFromSelectionAsync(long startSample = 0, long endSample = 0, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return this.CopyFromSelection(startSample, endSample);
            }, ct).ConfigureAwait(false);
        }



		// WAV-Export (16-bit PCM)
		public async Task ExportWavAsync(string outputPath, int bits = 24, CancellationToken ct = default)
		{
			if (this.Data == null || this.Data.Length == 0)
			{
				throw new InvalidOperationException("No audio data to export.");
			}

			Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

			// Unterstützt: 8, 16, 24, 32 (bei 32 wird IEEE Float geschrieben)
			bits = bits switch
			{
				8 => 8,
				16 => 16,
				24 => 24,
				32 => 32,
				_ => 24
			};

			await Task.Run(() =>
			{
				try
				{
					ct.ThrowIfCancellationRequested();

					var channels = Math.Max(1, this.Channels);
					var sampleRate = Math.Max(8000, this.SampleRate);
					var src = this.Data; // Snapshot

					if (bits == 32)
					{
						// 32-bit IEEE Float WAV (direkt schreiben, da Daten bereits float sind)
						var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
						using var writer = new WaveFileWriter(outputPath, format);

						// In Bytes umwandeln
						var buffer = new byte[src.Length * sizeof(float)];
						Buffer.BlockCopy(src, 0, buffer, 0, buffer.Length);

						// Schreiben mit Rücksicht auf Cancellation
						const int chunk = 64 * 1024;
						int offset = 0;
						while (offset < buffer.Length)
						{
							ct.ThrowIfCancellationRequested();
							int count = Math.Min(chunk, buffer.Length - offset);
							writer.Write(buffer, offset, count);
							offset += count;
						}
						return;
					}

					// Integer PCM für 8/16/24 Bit
					var intPcmFormat = new WaveFormat(sampleRate, bits, channels);
					using var intWriter = new WaveFileWriter(outputPath, intPcmFormat);

					// Interleaving wird vorausgesetzt: Data enthält bereits alle Kanäle (float -1..+1)
					// Konvertierung abhängig von Ziel-Bittiefe
					const int framesChunk = 64 * 1024; // Anzahl Samples (Mono-Samples) pro Chunk
					int totalSamples = src.Length;
					int sampleIndex = 0;

					if (bits == 8)
					{
						// 8-bit PCM: unsigned (0..255), 128 = 0.0
						var byteBuf = new byte[Math.Min(framesChunk, totalSamples)];
						while (sampleIndex < totalSamples)
						{
							ct.ThrowIfCancellationRequested();
							int count = Math.Min(byteBuf.Length, totalSamples - sampleIndex);
							for (int i = 0; i < count; i++)
							{
								// Clamp und Map
								float f = MathF.Max(-1f, MathF.Min(1f, src[sampleIndex + i]));
								// Skala -1..1 -> 0..255
								int u = (int) MathF.Round((f * 0.5f + 0.5f) * 255f);
								byteBuf[i] = (byte) Math.Clamp(u, 0, 255);
							}
							intWriter.Write(byteBuf, 0, count);
							sampleIndex += count;
						}
					}
					else if (bits == 16)
					{
						// 16-bit PCM: signed little-endian
						var byteBuf = new byte[Math.Min(framesChunk, totalSamples) * 2];
						while (sampleIndex < totalSamples)
						{
							ct.ThrowIfCancellationRequested();
							int samplesThis = Math.Min(framesChunk, totalSamples - sampleIndex);
							int outOfs = 0;
							for (int i = 0; i < samplesThis; i++)
							{
								float f = MathF.Max(-1f, MathF.Min(1f, src[sampleIndex + i]));
								short s = (short) Math.Clamp((int) MathF.Round(f * short.MaxValue), short.MinValue, short.MaxValue);
								byteBuf[outOfs++] = (byte) (s & 0xFF);
								byteBuf[outOfs++] = (byte) ((s >> 8) & 0xFF);
							}
							intWriter.Write(byteBuf, 0, outOfs);
							sampleIndex += samplesThis;
						}
					}
					else // 24-bit
					{
						// 24-bit PCM: signed little-endian (3 Bytes pro Sample)
						var byteBuf = new byte[Math.Min(framesChunk, totalSamples) * 3];
						while (sampleIndex < totalSamples)
						{
							ct.ThrowIfCancellationRequested();
							int samplesThis = Math.Min(framesChunk, totalSamples - sampleIndex);
							int outOfs = 0;
							for (int i = 0; i < samplesThis; i++)
							{
								float f = MathF.Max(-1f, MathF.Min(1f, src[sampleIndex + i]));
								// Skala auf 24-bit Range
								int s = (int) Math.Clamp(MathF.Round(f * 8388607f), -8388608f, 8388607f); // 2^23-1
																										  // Little-Endian 3 Bytes
								byteBuf[outOfs++] = (byte) (s & 0xFF);
								byteBuf[outOfs++] = (byte) ((s >> 8) & 0xFF);
								byteBuf[outOfs++] = (byte) ((s >> 16) & 0xFF);
							}
							intWriter.Write(byteBuf, 0, outOfs);
							sampleIndex += samplesThis;
						}
					}
				}
				catch (OperationCanceledException)
				{
					// still und nicht-blockierend: Export abgebrochen
				}
				catch
				{
					// still und nicht-blockierend: Fehler beim Export
					// Optional: Logging/Telemetry hier einfügen
				}
			}).ConfigureAwait(false);
		}

		// MP3-Export mit LAME
		public async Task ExportMp3Async(string outputPath, int bitrateKbps = 192, CancellationToken ct = default)
        {
            if (this.Data == null || this.Data.Length == 0)
            {
                throw new InvalidOperationException("No audio data to export.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var format = WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels);
                var rawProvider = new ArraySampleProvider(this.Data, format);
                var sampleToWave = new SampleToWaveProvider16(rawProvider);

                using var mp3Writer = new LameMP3FileWriter(outputPath, sampleToWave.WaveFormat, bitrateKbps);
                var buffer = new byte[4096];
                int read;

                // SampleToWaveProvider16 liefert Wave, wir ziehen es gleich aus mp3Writer.WaveFormat nicht – wir streamen
                var waveProvider = new Wave16ToByteProvider(sampleToWave);

                while ((read = waveProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    mp3Writer.Write(buffer, 0, read);
                }
            }, ct).ConfigureAwait(false);
        }

        private sealed class Wave16ToByteProvider : IWaveProvider
        {
            private readonly IWaveProvider _source;

            public Wave16ToByteProvider(IWaveProvider source) => this._source = source;

            public WaveFormat WaveFormat => this._source.WaveFormat;

            public int Read(byte[] buffer, int offset, int count)
            {
                return this._source.Read(buffer, offset, count);
            }
        }
    }
}
