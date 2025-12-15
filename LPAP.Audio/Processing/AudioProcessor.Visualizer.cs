using System;
using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LPAP.Audio.Processing
{
	public static partial class AudioProcessor
	{
		public static async Task<Image[]> RenderVisualizerImagesAsync(
			AudioObj audio,
			int width,
			int height,
			float frameRate = 20.0f,
			string? outputFilePath = null,
			int maxWorkers = 0,
			IProgress<double>? progress = null,
			CancellationToken? ct = null)
		{
			_ = outputFilePath; // bewusst ungenutzt (RAM-Variante). Param bleibt für API-Kompatibilität.

			if (audio is null)
			{
				throw new ArgumentNullException(nameof(audio));
			}

			if (audio.Data is null)
			{
				throw new ArgumentException("audio.Data ist null.", nameof(audio));
			}

			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width));
			}

			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(height));
			}

			var token = ct ?? CancellationToken.None;

			if (frameRate <= 0)
			{
				frameRate = 20.0f;
			}

			maxWorkers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			int channels = Math.Max(1, audio.Channels);
			int sampleRate = Math.Max(1, audio.SampleRate);

			// Frame count aus Duration (wenn vorhanden), sonst aus Datenlänge
			double durationSeconds = audio.Duration.TotalSeconds > 0
				? audio.Duration.TotalSeconds
				: EstimateDurationSeconds(audio.Data.Length, sampleRate, channels);

			int frameCount = Math.Max(1, (int) Math.Ceiling(durationSeconds * frameRate));
			progress?.Report(0.0);

			var images = new Image[frameCount];
			int done = 0;

			var opts = new ParallelOptions
			{
				MaxDegreeOfParallelism = maxWorkers,
				CancellationToken = token
			};

			await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
			{
				innerCt.ThrowIfCancellationRequested();

				// Frame-Zeitbereich
				double t0 = i / (double) frameRate;
				double t1 = (i + 1) / (double) frameRate;

				// Samples (pro Channel) in Indexbereich übersetzen
				long startSample = (long) Math.Floor(t0 * sampleRate);
				long endSample = (long) Math.Floor(t1 * sampleRate);

				// clamp auf verfügbares Material
				long availableSamplesPerChannel = (audio.LengthSamples > 0)
					? audio.LengthSamples
					: (audio.Data.LongLength / channels);

				if (startSample < 0)
				{
					startSample = 0;
				}

				if (endSample < startSample + 1)
				{
					endSample = startSample + 1;
				}

				if (startSample > availableSamplesPerChannel)
				{
					startSample = availableSamplesPerChannel;
				}

				if (endSample > availableSamplesPerChannel)
				{
					endSample = availableSamplesPerChannel;
				}

				var bmp = RenderWaveformFrame(audio.Data, channels, width, height, startSample, endSample);

				images[i] = bmp;

				int now = Interlocked.Increment(ref done);
				progress?.Report(now / (double) frameCount);

				await Task.CompletedTask.ConfigureAwait(false);
			}).ConfigureAwait(false);

			return images;
		}

		public static async Task<string?> RenderVisualizerImagesToTempDirAsync(
			AudioObj audio,
			int width,
			int height,
			float frameRate = 20.0f,
			string? outputFilePath = null,
			int maxWorkers = 0,
			IProgress<double>? progress = null,
			CancellationToken? ct = null)
		{
			if (audio is null)
			{
				throw new ArgumentNullException(nameof(audio));
			}

			if (audio.Data is null)
			{
				throw new ArgumentException("audio.Data ist null.", nameof(audio));
			}

			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width));
			}

			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(height));
			}

			var token = ct ?? CancellationToken.None;

			if (frameRate <= 0)
			{
				frameRate = 20.0f;
			}

			maxWorkers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			int channels = Math.Max(1, audio.Channels);
			int sampleRate = Math.Max(1, audio.SampleRate);

			double durationSeconds = audio.Duration.TotalSeconds > 0
				? audio.Duration.TotalSeconds
				: EstimateDurationSeconds(audio.Data.Length, sampleRate, channels);

			int frameCount = Math.Max(1, (int) Math.Ceiling(durationSeconds * frameRate));

			// TempDir bestimmen (oder aus outputFilePath ableiten)
			string dir = ResolveOutputDirForFrames(outputFilePath);
			Directory.CreateDirectory(dir);

			// Leading zeros
			int digits = Math.Max(3, frameCount.ToString(CultureInfo.InvariantCulture).Length);
			string ext = ".png";

			progress?.Report(0.0);

			int done = 0;

			var opts = new ParallelOptions
			{
				MaxDegreeOfParallelism = maxWorkers,
				CancellationToken = token
			};

			try
			{
				await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
				{
					innerCt.ThrowIfCancellationRequested();

					double t0 = i / (double) frameRate;
					double t1 = (i + 1) / (double) frameRate;

					long startSample = (long) Math.Floor(t0 * sampleRate);
					long endSample = (long) Math.Floor(t1 * sampleRate);

					long availableSamplesPerChannel = (audio.LengthSamples > 0)
						? audio.LengthSamples
						: (audio.Data.LongLength / channels);

					if (startSample < 0)
					{
						startSample = 0;
					}

					if (endSample < startSample + 1)
					{
						endSample = startSample + 1;
					}

					if (startSample > availableSamplesPerChannel)
					{
						startSample = availableSamplesPerChannel;
					}

					if (endSample > availableSamplesPerChannel)
					{
						endSample = availableSamplesPerChannel;
					}

					using var bmp = RenderWaveformFrame(audio.Data, channels, width, height, startSample, endSample);

					string name = i.ToString("D" + digits, CultureInfo.InvariantCulture) + ext;
					string path = Path.Combine(dir, name);

					// Save (PNG)
					bmp.Save(path, ImageFormat.Png);

					int now = Interlocked.Increment(ref done);
					progress?.Report(now / (double) frameCount);

					await Task.CompletedTask.ConfigureAwait(false);
				}).ConfigureAwait(false);

				return dir;
			}
			catch
			{
				// Optional: Bei Fehler/Cancel kannst du das Dir löschen. Ich lasse es bewusst stehen (Debug/Partial Output).
				throw;
			}
		}

		public readonly struct FramePacket
		{
			public FramePacket(int index, byte[] buffer, int length)
			{
				this.Index = index;
				this.Buffer = buffer;
				this.Length = length;
			}
			public int Index { get; }
			public byte[] Buffer { get; }   // BGRA
			public int Length { get; }
		}

        public static (ChannelReader<FramePacket> reader, int frameCount) RenderVisualizerFramesBgraChannel(
    AudioObj audio,
    int width,
    int height,
    float frameRate = 20.0f,
    float amplification = 0.8f,   // <<< NEU
    int maxWorkers = 0,
    int channelCapacity = 0,
    IProgress<double>? progress = null,
    CancellationToken? ct = null)
        {
            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data null", nameof(audio));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var token = ct ?? CancellationToken.None;

            if (frameRate <= 0)
            {
                frameRate = 20f;
            }

            maxWorkers = maxWorkers <= 0
                ? Environment.ProcessorCount
                : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (channelCapacity <= 0)
            {
                channelCapacity = Math.Max(2, maxWorkers * 2);
            }

            int channels = Math.Max(1, audio.Channels);
            int sampleRate = Math.Max(1, audio.SampleRate);

            double duration = audio.Duration.TotalSeconds > 0
                ? audio.Duration.TotalSeconds
                : (audio.Data.Length / (double) channels) / sampleRate;

            int frameCount = Math.Max(1, (int) Math.Ceiling(duration * frameRate));
            int frameBytes = checked(width * height * 4);

            var channel = Channel.CreateBounded<FramePacket>(
                new BoundedChannelOptions(channelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = false
                });

            progress?.Report(0.0);
            int produced = 0;

            long availableSamplesPerChannel =
                (audio.LengthSamples > 0)
                    ? audio.LengthSamples
                    : (audio.Data.LongLength / channels);

            // Producer Task
            _ = Task.Run(async () =>
            {
                var opts = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers,
                    CancellationToken = token
                };

                try
                {
                    await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
                    {
                        innerCt.ThrowIfCancellationRequested();

                        double t0 = i / (double) frameRate;
                        double t1 = (i + 1) / (double) frameRate;

                        long s0 = (long) Math.Floor(t0 * sampleRate);
                        long s1 = (long) Math.Floor(t1 * sampleRate);
                        if (s1 <= s0)
                        {
                            s1 = s0 + 1;
                        }

                        // CLAMP auf verfügbares Material (pro Channel)
                        if (s0 < 0)
                        {
                            s0 = 0;
                        }

                        if (s1 < 1)
                        {
                            s1 = 1;
                        }

                        if (s0 > availableSamplesPerChannel)
                        {
                            s0 = availableSamplesPerChannel;
                        }

                        if (s1 > availableSamplesPerChannel)
                        {
                            s1 = availableSamplesPerChannel;
                        }

                        if (s1 <= s0)
                        {
                            s1 = Math.Min(availableSamplesPerChannel, s0 + 1);
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

                        try
                        {
                            RenderWaveformToBgra(
								audio.Data, channels,
								width, height,
								s0, s1,
								buffer, amplification);


                            await channel.Writer.WriteAsync(new FramePacket(i, buffer, frameBytes), innerCt)
                                .ConfigureAwait(false);

                            // Ownership geht an Consumer -> nicht zurückgeben
                            buffer = null!;

                            int now = Interlocked.Increment(ref produced);
                            progress?.Report(now / (double) frameCount);
                        }
                        finally
                        {
                            if (buffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, token);

            return (channel.Reader, frameCount);
        }


        // -------------------------
        // CORE: Waveform → BGRA
        // -------------------------
        private static void RenderWaveformToBgra(
    float[] interleaved,
    int channels,
    int width,
    int height,
    long startSample,
    long endSample,
    byte[] dst,
    float amplification)

        {
            int midY = height / 2;
			int frameSamples = (int) Math.Max(1, endSample - startSample);

			// clear black
			Array.Clear(dst, 0, width * height * 4);

			for (int x = 0; x < width; x++)
			{
				long a = startSample + (long) (x / (double) width * frameSamples);
				long b = startSample + (long) ((x + 1) / (double) width * frameSamples);
				if (b <= a)
				{
					b = a + 1;
				}

				float min = 1f, max = -1f;

				for (long s = a; s < b; s++)
				{
					long baseIdx = s * channels;
					if (baseIdx >= interleaved.LongLength)
					{
						break;
					}

					float mono = 0f;
					int ccount = 0;

					for (int c = 0; c < channels; c++)
					{
						long idx = baseIdx + c;
						if (idx < interleaved.LongLength)
						{
							mono += interleaved[idx];
							ccount++;
						}
					}

					if (ccount > 0)
					{
						mono /= ccount;
					}

					if (mono < min)
					{
						min = mono;
					}

					if (mono > max)
					{
						max = mono;
					}
				}

                // Anzeige-Gain (nur visuell!)
                min *= amplification;
                max *= amplification;

                // danach clampen
                min = Math.Clamp(min, -1f, 1f);
                max = Math.Clamp(max, -1f, 1f);

                int y0 = midY - (int) (max * (midY - 1));
				int y1 = midY - (int) (min * (midY - 1));
				if (y0 > y1)
				{
					(y0, y1) = (y1, y0);
				}

				for (int y = y0; y <= y1; y++)
				{
					int i = (y * width + x) * 4;
					dst[i + 0] = 0;     // B
					dst[i + 1] = 255;   // G
					dst[i + 2] = 0;     // R
					dst[i + 3] = 255;   // A
				}
			}
		}

		private static Bitmap RenderWaveformFrame(
			float[] interleaved,
			int channels,
			int width,
			int height,
			long startSamplePerChannel,
			long endSamplePerChannel)
		{
			// Wir rendern pro X-Spalte min/max der Amplitude im entsprechenden Samplebereich.
			// Mono-Mix = avg über Channels.
			var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

			// Background + waveform
			using var g = Graphics.FromImage(bmp);
			g.Clear(Color.Black);

			int midY = height / 2;
			using var axisPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
			using var wavePen = new Pen(Color.Lime, 1f);

			// Mittelachse
			g.DrawLine(axisPen, 0, midY, width - 1, midY);

			long frameSamples = Math.Max(1, endSamplePerChannel - startSamplePerChannel);

			// Mapping: x -> [s0,s1)
			for (int x = 0; x < width; x++)
			{
				double a = x / (double) width;
				double b = (x + 1) / (double) width;

				long s0 = startSamplePerChannel + (long) Math.Floor(a * frameSamples);
				long s1 = startSamplePerChannel + (long) Math.Floor(b * frameSamples);
				if (s1 <= s0)
				{
					s1 = s0 + 1;
				}

				float min = 1f;
				float max = -1f;

				for (long s = s0; s < s1; s++)
				{
					// interleaved index: (s * channels + c)
					long baseIdx = s * channels;
					if (baseIdx < 0 || baseIdx >= interleaved.LongLength)
					{
						break;
					}

					float mono = 0f;
					int cCount = 0;

					for (int c = 0; c < channels; c++)
					{
						long idx = baseIdx + c;
						if (idx >= 0 && idx < interleaved.LongLength)
						{
							mono += interleaved[idx];
							cCount++;
						}
					}

					if (cCount > 0)
					{
						mono /= cCount;
					}

					if (mono < min)
					{
						min = mono;
					}

					if (mono > max)
					{
						max = mono;
					}
				}

				// clamp
				if (min < -1f)
				{
					min = -1f;
				}

				if (max > 1f)
				{
					max = 1f;
				}

				int y0 = midY - (int) (max * (midY - 1));
				int y1 = midY - (int) (min * (midY - 1));

				if (y0 > y1)
				{
					(y0, y1) = (y1, y0);
				}

				g.DrawLine(wavePen, x, y0, x, y1);
			}

			return bmp;
		}

		private static double EstimateDurationSeconds(long dataLength, int sampleRate, int channels)
		{
			if (sampleRate <= 0)
			{
				return 0;
			}

			if (channels <= 0)
			{
				channels = 1;
			}

			double samplesPerChannel = dataLength / (double) channels;
			return samplesPerChannel / sampleRate;
		}

		private static string ResolveOutputDirForFrames(string? outputFilePath)
		{
			// Ziel: immer ein Directory zurückgeben, in das wir numerierte PNGs schreiben.
			// - null/empty => Temp\LPAP_Vis_yyyyMMdd_HHmmss\
			// - existierender Ordner => Ordner\LPAP_Vis_...\
			// - Pfad endet mit slash => genau der Ordner
			// - Pfad ist Datei => Ordner der Datei + Subdir LPAP_Vis_... (damit wir sauber "TempDirPath" zurückgeben)
			string stamp = "LPAP_Vis_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

			if (string.IsNullOrWhiteSpace(outputFilePath))
			{
				return Path.Combine(Path.GetTempPath(), stamp);
			}

			outputFilePath = Path.GetFullPath(outputFilePath);

			if (Directory.Exists(outputFilePath))
			{
				return Path.Combine(outputFilePath, stamp);
			}

			if (outputFilePath.EndsWith(Path.DirectorySeparatorChar) || outputFilePath.EndsWith(Path.AltDirectorySeparatorChar))
			{
				return outputFilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			}

			// Datei-Pfad -> dessen Ordner nehmen
			string? dir = Path.GetDirectoryName(outputFilePath);
			if (string.IsNullOrWhiteSpace(dir))
			{
				return Path.Combine(Path.GetTempPath(), stamp);
			}

			return Path.Combine(dir, stamp);
		}
	}
}
