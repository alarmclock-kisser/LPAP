#nullable enable
using LPAP.Audio;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace LPAP.Cuda
{
	public static class NvencVideoRenderer
	{
		public sealed record NvencOptions(
			string VideoCodec,            // "h264_nvenc" oder "hevc_nvenc"
			string Preset,                // z.B. "p5" (neuere ffmpeg/nvenc) oder "slow"/"medium" je nach Build
			int? VideoBitrateKbps,         // null => CRF/CQ (wenn gesetzt) oder ffmpeg default
			int? MaxBitrateKbps,           // optional
			int? BufferSizeKbps,           // optional
			int? ConstantQuality,          // CQ (0..51 h264, 0..63 hevc), null => bitrate mode
			string? Profile,              // z.B. "high" für h264
			bool FastStart                // -movflags +faststart
		)
		{
			public static NvencOptions H264Default => new(
				VideoCodec: "h264_nvenc",
				Preset: "p5",
				VideoBitrateKbps: null,
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: 20,
				Profile: "high",
				FastStart: true
			);

			public static NvencOptions HevcDefault => new(
				VideoCodec: "hevc_nvenc",
				Preset: "p5",
				VideoBitrateKbps: null,
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: 22,
				Profile: null,
				FastStart: true
			);
		}

		public static readonly string[] CommonResolutions =
		[
			"3840x2160",
			"1920x1080",
			"1280x720",
			"854x480",
			"640x360",
			"426x240",
			"256x144"
		];

		public static Task<string?> NvencRenderVideoAsync(Image[] frames, int width = 0, int height = 0,
			float frameRate = 20.0f, string? audioFilePath = null, string? outputFilePath = null,
			IProgress<double>? progress = null, CancellationToken? ct = null)
			=> NvencRenderVideoAsync(
				frames: frames,
				width: width,
				height: height,
				frameRate: frameRate,
				audioFilePath: audioFilePath,
				outputFilePath: outputFilePath,
				options: NvencOptions.H264Default,
				progress: progress,
				ct: ct
			);

		public static async Task<string?> NvencRenderVideoAsync(
			Image[] frames, int width, int height,
			float frameRate, string? audioFilePath, string? outputFilePath,
			NvencOptions options,
			IProgress<double>? progress = null, CancellationToken? ct = null)
		{
			if (frames is null || frames.Length == 0)
			{
				throw new ArgumentException("frames darf nicht null/leer sein.", nameof(frames));
			}

			var token = ct ?? CancellationToken.None;

			// 1) Dimensionen bestimmen
			if (width <= 0 || height <= 0)
			{
				GetMaxDimensions(frames, out var maxW, out var maxH);
				if (width <= 0)
				{
					width = maxW;
				}

				if (height <= 0)
				{
					height = maxH;
				}
			}
			if (width <= 0 || height <= 0)
			{
				throw new ArgumentOutOfRangeException("width/height müssen > 0 sein (oder aus Frames ableitbar).");
			}

			// 2) Audio prüfen + ggf. frameRate aus Audiolänge setzen
			TimeSpan? audioDuration = null;
			if (!string.IsNullOrWhiteSpace(audioFilePath))
			{
				audioFilePath = Path.GetFullPath(audioFilePath);
				if (!File.Exists(audioFilePath))
				{
					throw new FileNotFoundException("audioFilePath existiert nicht.", audioFilePath);
				}

				// NAudio: Audio lesen (nur Dauer)
				// Wenn du NAudio nicht referenzieren willst, kannst du alternativ ffprobe nutzen.
				try
				{
#if NAUDIO
                    using var afr = new NAudio.Wave.AudioFileReader(audioFilePath);
                    audioDuration = afr.TotalTime;
#else
					// Minimal-Fallback: keine Dauer (frameRate bleibt wie übergeben).
					// Empfehlung: NAudio definieren (Symbol NAUDIO) oder ffprobe implementieren.
					audioDuration = null;
#endif
				}
				catch
				{
					// Wenn Audio nicht ladbar: wir muxen es trotzdem ggf. via ffmpeg,
					// aber frameRate-Automatik nur wenn Dauer sicher.
					audioDuration = null;
				}

				if (frameRate <= 0 && audioDuration.HasValue && audioDuration.Value.TotalSeconds > 0.001)
				{
					frameRate = (float) (frames.Length / audioDuration.Value.TotalSeconds);
					if (frameRate < 1e-3f)
					{
						frameRate = 20.0f;
					}
				}
			}

			if (frameRate <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(frameRate), "frameRate muss > 0 sein, außer Audio-Dauer konnte ermittelt werden.");
			}

			// 3) Output-Pfad bestimmen
			var resolvedOutputPath = ResolveOutputPath(outputFilePath);

			Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

			// 4) Gesamtdauer für Progress
			var totalVideoSeconds = frames.Length / (double) frameRate;
			var totalSeconds = audioDuration.HasValue && audioDuration.Value.TotalSeconds > 0.001
				? Math.Max(totalVideoSeconds, audioDuration.Value.TotalSeconds)
				: totalVideoSeconds;

			progress?.Report(0.0);

			// 5) FFmpeg args
			// Wir pipen raw BGRA Frames auf stdin:
			// -f rawvideo -pix_fmt bgra -s WxH -r FPS -i pipe:0
			// Audio optional: -i "audio"
			// NVENC: -c:v h264_nvenc / hevc_nvenc
			// Output MP4
			var args = BuildFfmpegArgs(
				width, height, frameRate,
				audioFilePath,
				resolvedOutputPath,
				options);

			// 6) Process starten
			var psi = new ProcessStartInfo
			{
				FileName = "ffmpeg",
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true, // progress kommt hier rein
				RedirectStandardError = true,  // für Fehlerdiagnose
				CreateNoWindow = true
			};

			using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

			var stderrSb = new StringBuilder();

			try
			{
				if (!proc.Start())
				{
					throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");
				}

				// Asynchron stderr sammeln (für Debug bei Fail)
				var stderrTask = Task.Run(async () =>
				{
					try
					{
						string? line;
						while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
						{
							stderrSb.AppendLine(line);
						}
					}
					catch { /* ignore */ }
				}, token);

				// Progress parser (aus StandardOutput durch -progress pipe:1)
				var progressTask = Task.Run(async () =>
				{
					try
					{
						string? line;
						double lastReported = 0.0;

						while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
						{
							// Beispiel:
							// out_time_ms=1234567
							// progress=continue
							if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
							{
								var val = line["out_time_ms=".Length..].Trim();
								if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
								{
									var sec = outMs / 1_000_000.0;
									var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
									if (p < 0)
									{
										p = 0;
									}

									if (p > 1)
									{
										p = 1;
									}

									// etwas entprellen
									if (p >= lastReported + 0.002 || p >= 1.0)
									{
										lastReported = p;
										progress?.Report(p);
									}
								}
							}
						}
					}
					catch { /* ignore */ }
				}, token);

				// Frames schreiben (streaming)
				await WriteFramesToStdinAsync(proc, frames, width, height, progress, totalSeconds, frameRate, token)
					.ConfigureAwait(false);

				// stdin schließen => ffmpeg finalisiert
				try { proc.StandardInput.Close(); } catch { /* ignore */ }

				// Auf Exit warten
				await proc.WaitForExitAsync(token).ConfigureAwait(false);

				// Tasks beenden
				try { await Task.WhenAll(stderrTask, progressTask).ConfigureAwait(false); } catch { /* ignore */ }

				if (proc.ExitCode != 0)
				{
					// Wenn was schiefgeht, Output ggf. löschen
					SafeDelete(resolvedOutputPath);

					var err = stderrSb.ToString();
					throw new InvalidOperationException(
						$"FFmpeg exit code {proc.ExitCode}. Details:\n{err}");
				}

				progress?.Report(1.0);
				return resolvedOutputPath;
			}
			catch (OperationCanceledException)
			{
				TryKill(proc);
				SafeDelete(resolvedOutputPath);
				throw;
			}
			catch
			{
				TryKill(proc);
				SafeDelete(resolvedOutputPath);
				throw;
			}
		}

		private static async Task WriteFramesToStdinAsync(
			Process proc,
			Image[] frames,
			int targetW,
			int targetH,
			IProgress<double>? progress,
			double totalSeconds,
			float frameRate,
			CancellationToken token)
		{
			// BGRA = 4 bytes/pixel
			var frameBytes = checked(targetW * targetH * 4);

			// Pool buffer für eine Frame
			var buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

			try
			{
				using var stdin = proc.StandardInput.BaseStream;

				// Für höhere Stabilität: wir zeichnen jedes Image auf ein Bitmap exakt target size
				// und schreiben dann LockBits(BGRA) direkt in den Buffer.
				using var canvas = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);

				// Wir nutzen hier pro Frame ein Graphics-Objekt neu (sauber), du kannst es auch cachen.
				for (int i = 0; i < frames.Length; i++)
				{
					token.ThrowIfCancellationRequested();

					using (var g = Graphics.FromImage(canvas))
					{
						g.Clear(Color.Black);
						// Top-left platzieren; wenn du "fit/center" willst, kannst du hier skalieren.
						g.DrawImage(frames[i], 0, 0, frames[i].Width, frames[i].Height);
					}

					CopyBitmap32bppArgbToBgraBuffer(canvas, buffer, frameBytes);

					await stdin.WriteAsync(buffer.AsMemory(0, frameBytes), token).ConfigureAwait(false);

					// Optional: grober Progress auch während dem Schreiben (zusätzlich zu ffmpeg-progress)
					// Das hilft, falls -progress nicht feuert (z.B. ganz alte builds).
					if (progress != null && totalSeconds > 0)
					{
						var writtenSec = i / (double) frameRate;
						var p = writtenSec / totalSeconds;
						if (p < 0)
						{
							p = 0;
						}

						if (p > 0.999)
						{
							p = 0.999;
						}

						progress.Report(p);
					}
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		public static Task<string?> NvencRenderVideoAsync(
			Image[] frames,
			int width,
			int height,
			float frameRate,
			float[] audioData,
			int audioSampleRate,
			int audioChannels,
			string? outputFilePath = null,
			NvencOptions? options = null,
			IProgress<double>? progress = null,
			CancellationToken? ct = null)
		{
			if (audioData is null) throw new ArgumentNullException(nameof(audioData));
			if (audioSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(audioSampleRate));
			if (audioChannels <= 0) throw new ArgumentOutOfRangeException(nameof(audioChannels));

			return NvencRenderVideoWithRawAudioAsync(
				frames, width, height,
				frameRate,
				audioData, audioSampleRate, audioChannels,
				outputFilePath,
				options ?? NvencOptions.H264Default,
				progress,
				ct);
		}

		// Overload: AudioObj
		public static Task<string?> NvencRenderVideoAsync(
			Image[] frames, int width, int height,
			float frameRate,
			AudioObj audio,
			string? outputFilePath = null,
			NvencOptions? options = null,
			IProgress<double>? progress = null,
			CancellationToken? ct = null)
		{
			if (audio is null) throw new ArgumentNullException(nameof(audio));
			if (audio.Data is null) throw new ArgumentException("audio.Data ist null.", nameof(audio));

			return NvencRenderVideoWithRawAudioAsync(
				frames, width, height,
				frameRate,
				audio.Data, audio.SampleRate, audio.Channels,
				outputFilePath,
				options ?? NvencOptions.H264Default,
				progress,
				ct);
		}




		private static void CopyBitmap32bppArgbToBgraBuffer(Bitmap bmp, byte[] dst, int expectedBytes)
		{
			if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
			{
				throw new ArgumentException("Bitmap muss Format32bppArgb sein.");
			}

			var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
			var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				// In Windows GDI+: Format32bppArgb liegt in Memory typischerweise als BGRA (little-endian).
				// Wir können daher direkt kopieren.
				var stride = data.Stride;
				var rowBytes = bmp.Width * 4;

				if (rowBytes * bmp.Height != expectedBytes)
				{
					throw new InvalidOperationException("Frame byte size mismatch.");
				}

				unsafe
				{
					byte* srcBase = (byte*) data.Scan0;
					int di = 0;

					for (int y = 0; y < bmp.Height; y++)
					{
						Buffer.MemoryCopy(
							source: srcBase + (y * stride),
							destination: UnsafeAsPtr(dst, di),
							destinationSizeInBytes: rowBytes,
							sourceBytesToCopy: rowBytes);

						di += rowBytes;
					}
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}

			static unsafe byte* UnsafeAsPtr(byte[] arr, int offset)
			{
				fixed (byte* p = arr)
				{
					return p + offset;
				}
			}
		}

		private static string BuildFfmpegArgs(
			int width,
			int height,
			float frameRate,
			string? audioFilePath,
			string outputPath,
			NvencOptions options)
		{
			// Wichtig: -progress pipe:1 => machine readable
			// -nostats hält es sauberer
			// -loglevel error => stderr nur bei Fehlern
			// Audio: -i "audio" und dann -shortest, damit nichts überläuft.
			// Pixel format Output: yuv420p für maximale Kompatibilität.
			var sb = new StringBuilder();

			sb.Append("-hide_banner -nostats ");
			sb.Append("-loglevel error ");
			sb.Append("-progress pipe:1 ");

			// Video input: raw BGRA piped
			sb.Append(CultureInfo.InvariantCulture,
				$"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

			if (!string.IsNullOrWhiteSpace(audioFilePath))
			{
				sb.Append($"-i \"{audioFilePath}\" ");
			}

			// Video codec NVENC
			sb.Append($"-c:v {options.VideoCodec} ");

			// Preset
			if (!string.IsNullOrWhiteSpace(options.Preset))
			{
				sb.Append($"-preset {options.Preset} ");
			}

			// Rate control: CQ bevorzugt wenn gesetzt
			if (options.ConstantQuality.HasValue)
			{
				// NVENC: -cq:v oder -qp? ffmpeg supports -cq:v in neueren builds
				sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
				// Optional: VBR HQ kann man je nach Build setzen (rc)
				// sb.Append("-rc vbr_hq ");
			}
			else if (options.VideoBitrateKbps.HasValue)
			{
				sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
				if (options.MaxBitrateKbps.HasValue)
				{
					sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
				}

				if (options.BufferSizeKbps.HasValue)
				{
					sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
				}
			}

			if (!string.IsNullOrWhiteSpace(options.Profile))
			{
				sb.Append($"-profile:v {options.Profile} ");
			}

			// Pixel format für MP4 Player-Kompatibilität
			sb.Append("-pix_fmt yuv420p ");

			// Audio: wenn vorhanden, kopieren oder neu encoden?
			// Sicherer Default: AAC encode (fast, kompatibel)
			if (!string.IsNullOrWhiteSpace(audioFilePath))
			{
				sb.Append("-c:a aac -b:a 192k -shortest ");
			}

			if (options.FastStart)
			{
				sb.Append("-movflags +faststart ");
			}

			// Overwrite
			sb.Append("-y ");

			sb.Append($"\"{outputPath}\"");

			return sb.ToString();
		}

		private static string BuildFfmpegArgsWithRawAudioPipe(
			int width,
			int height,
			float frameRate,
			string audioPipePath,
			int audioSampleRate,
			int audioChannels,
			string outputPath,
			NvencOptions options)
		{
			var sb = new StringBuilder();

			sb.Append("-hide_banner -nostats ");
			sb.Append("-loglevel error ");
			sb.Append("-progress pipe:1 ");

			// Video input: raw BGRA piped auf stdin
			sb.Append(CultureInfo.InvariantCulture,
				$"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

			// Audio input: raw PCM16LE von Named Pipe (aus RAM gestreamt)
			sb.Append(CultureInfo.InvariantCulture,
				$"-f s16le -ar {audioSampleRate} -ac {audioChannels} -i \"{audioPipePath}\" ");

			// Video codec NVENC
			sb.Append($"-c:v {options.VideoCodec} ");

			if (!string.IsNullOrWhiteSpace(options.Preset))
				sb.Append($"-preset {options.Preset} ");

			if (options.ConstantQuality.HasValue)
			{
				sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
			}
			else if (options.VideoBitrateKbps.HasValue)
			{
				sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
				if (options.MaxBitrateKbps.HasValue) sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
				if (options.BufferSizeKbps.HasValue) sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
			}

			if (!string.IsNullOrWhiteSpace(options.Profile))
				sb.Append($"-profile:v {options.Profile} ");

			sb.Append("-pix_fmt yuv420p ");

			// Audio encode (aus raw PCM -> AAC)
			sb.Append("-c:a aac -b:a 192k -shortest ");

			if (options.FastStart)
				sb.Append("-movflags +faststart ");

			sb.Append("-y ");
			sb.Append($"\"{outputPath}\"");

			return sb.ToString();
		}


		private static void GetMaxDimensions(Image[] frames, out int maxW, out int maxH)
		{
			maxW = 0; maxH = 0;
			for (int i = 0; i < frames.Length; i++)
			{
				if (frames[i] is null)
				{
					continue;
				}

				if (frames[i].Width > maxW)
				{
					maxW = frames[i].Width;
				}

				if (frames[i].Height > maxH)
				{
					maxH = frames[i].Height;
				}
			}
		}

		private static string ResolveOutputPath(string? outputFilePath)
		{
			// Base: My Videos\NVenc_Output
			string baseDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
				"NVenc_Output"
			);

			Directory.CreateDirectory(baseDir);

			string defaultFileName =
				"NVenc_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".mp4";

			// 1) Kein Pfad angegeben → Default
			if (string.IsNullOrWhiteSpace(outputFilePath))
			{
				return Path.Combine(baseDir, defaultFileName);
			}

			outputFilePath = Path.GetFullPath(outputFilePath);

			// 2) Existierender Ordner → dort rein
			if (Directory.Exists(outputFilePath))
			{
				return Path.Combine(outputFilePath, defaultFileName);
			}

			// 3) Pfad endet auf Slash → als Ordner behandeln
			if (outputFilePath.EndsWith(Path.DirectorySeparatorChar) ||
				outputFilePath.EndsWith(Path.AltDirectorySeparatorChar))
			{
				Directory.CreateDirectory(outputFilePath);
				return Path.Combine(outputFilePath, defaultFileName);
			}

			// 4) Explizite Datei
			string dir = Path.GetDirectoryName(outputFilePath)!;
			if (!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}

			if (!outputFilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
			{
				outputFilePath += ".mp4";
			}

			return outputFilePath;
		}


		private static void TryKill(Process p)
		{
			try
			{
				if (!p.HasExited)
				{
					p.Kill(entireProcessTree: true);
				}
			}
			catch { /* ignore */ }
		}

		private static void SafeDelete(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch { /* ignore */ }
		}

		private static async Task<string?> NvencRenderVideoWithRawAudioAsync(
			Image[] frames,
			int width,
			int height,
			float frameRate,
			float[] audioData,
			int audioSampleRate,
			int audioChannels,
			string? outputFilePath,
			NvencOptions options,
			IProgress<double>? progress,
			CancellationToken? ct)
		{
			if (frames is null || frames.Length == 0)
				throw new ArgumentException("frames darf nicht null/leer sein.", nameof(frames));

			var token = ct ?? CancellationToken.None;

			// 1) Dimensionen bestimmen (wie bisher)
			if (width <= 0 || height <= 0)
			{
				GetMaxDimensions(frames, out var maxW, out var maxH);
				if (width <= 0) width = maxW;
				if (height <= 0) height = maxH;
			}
			if (width <= 0 || height <= 0)
				throw new ArgumentOutOfRangeException("width/height müssen > 0 sein (oder aus Frames ableitbar).");

			// 2) Audio-Dauer (aus RAM)
			double audioSeconds = 0;
			if (audioData.Length > 0 && audioSampleRate > 0 && audioChannels > 0)
				audioSeconds = (audioData.Length / (double) audioChannels) / audioSampleRate;

			// frameRate <= 0 => an Audio anpassen
			if (frameRate <= 0 && audioSeconds > 0.001)
			{
				frameRate = (float) (frames.Length / audioSeconds);
				if (frameRate < 1e-3f) frameRate = 20.0f;
			}
			if (frameRate <= 0)
				throw new ArgumentOutOfRangeException(nameof(frameRate), "frameRate muss > 0 sein (oder Audio muss gültig sein).");

			// 3) Output-Pfad
			var resolvedOutputPath = ResolveOutputPath(outputFilePath);
			Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

			// 4) Progress Gesamtdauer
			var totalVideoSeconds = frames.Length / (double) frameRate;
			var totalSeconds = audioSeconds > 0.001 ? Math.Max(totalVideoSeconds, audioSeconds) : totalVideoSeconds;
			progress?.Report(0.0);

			// 5) Named Pipe für Audio
			string pipeName = "lpap_nvenc_audio_" + Guid.NewGuid().ToString("N");
			string pipePath = $@"\\.\pipe\{pipeName}";

			// 6) FFmpeg args: Video von stdin + Audio von named pipe als s16le
			var args = BuildFfmpegArgsWithRawAudioPipe(
				width, height, frameRate,
				pipePath, audioSampleRate, audioChannels,
				resolvedOutputPath,
				options);

			var psi = new ProcessStartInfo
			{
				FileName = "ffmpeg",
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardInput = true,   // Video
				RedirectStandardOutput = true,  // -progress pipe:1
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
			var stderrSb = new StringBuilder();

			// NamedPipeServerStream: ffmpeg verbindet sich als Client
			using var audioPipe = new System.IO.Pipes.NamedPipeServerStream(
				pipeName,
				System.IO.Pipes.PipeDirection.Out,
				1,
				System.IO.Pipes.PipeTransmissionMode.Byte,
				System.IO.Pipes.PipeOptions.Asynchronous);

			try
			{
				if (!proc.Start())
					throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");

				// stderr sammeln
				var stderrTask = Task.Run(async () =>
				{
					try
					{
						string? line;
						while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
							stderrSb.AppendLine(line);
					}
					catch { }
				}, token);

				// progress lesen
				var progressTask = Task.Run(async () =>
				{
					try
					{
						string? line;
						double lastReported = 0.0;

						while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
						{
							if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
							{
								var val = line["out_time_ms=".Length..].Trim();
								if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
								{
									var sec = outMs / 1_000_000.0;
									var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
									if (p < 0) p = 0;
									if (p > 1) p = 1;

									if (p >= lastReported + 0.002 || p >= 1.0)
									{
										lastReported = p;
										progress?.Report(p);
									}
								}
							}
						}
					}
					catch { }
				}, token);

				// Audio writer: wartet bis ffmpeg die Pipe öffnet, dann schreibt PCM
				var audioTask = Task.Run(async () =>
				{
					token.ThrowIfCancellationRequested();

					await audioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);

					// Float32 [-1..1] -> PCM16LE streamen (chunked, ohne riesige Zwischenarrays)
					const int floatsPerChunk = 8192; // pro chunk
					var byteBuf = ArrayPool<byte>.Shared.Rent(floatsPerChunk * 2); // max shorts *2 (wir schreiben samples als short)

					try
					{
						int i = 0;
						while (i < audioData.Length)
						{
							token.ThrowIfCancellationRequested();

							int count = Math.Min(floatsPerChunk, audioData.Length - i);
							int bi = 0;

							for (int k = 0; k < count; k++)
							{
								float f = audioData[i + k];

								// clamp
								if (f > 1f) f = 1f;
								else if (f < -1f) f = -1f;

								short s = (short) Math.Round(f * 32767f);

								// little endian
								byteBuf[bi++] = (byte) (s & 0xFF);
								byteBuf[bi++] = (byte) ((s >> 8) & 0xFF);
							}

							await audioPipe.WriteAsync(byteBuf.AsMemory(0, bi), token).ConfigureAwait(false);
							i += count;
						}

						await audioPipe.FlushAsync(token).ConfigureAwait(false);
					}
					finally
					{
						ArrayPool<byte>.Shared.Return(byteBuf);
						try { audioPipe.Dispose(); } catch { }
					}
				}, token);

				// Video frames streamen (dein vorhandener Code)
				await WriteFramesToStdinAsync(proc, frames, width, height, progress, totalSeconds, frameRate, token)
					.ConfigureAwait(false);

				try { proc.StandardInput.Close(); } catch { }

				await proc.WaitForExitAsync(token).ConfigureAwait(false);

				// tasks beenden (audioTask kann schon durch sein)
				try { await Task.WhenAll(stderrTask, progressTask, audioTask).ConfigureAwait(false); } catch { }

				if (proc.ExitCode != 0)
				{
					SafeDelete(resolvedOutputPath);
					throw new InvalidOperationException(
						$"FFmpeg exit code {proc.ExitCode}. Details:\n{stderrSb}");
				}

				progress?.Report(1.0);
				return resolvedOutputPath;
			}
			catch (OperationCanceledException)
			{
				TryKill(proc);
				SafeDelete(resolvedOutputPath);
				throw;
			}
			catch
			{
				TryKill(proc);
				SafeDelete(resolvedOutputPath);
				throw;
			}
		}





		public static Task<Image?> BuildImageFromBytesAsync(byte[] data, int width, int height,
			string channelCode = "BGRA", CancellationToken ct = default)
		{
			if (data is null)
			{
				throw new ArgumentNullException(nameof(data));
			}

			if (width <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(width));
			}

			if (height <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(height));
			}

			if (string.IsNullOrWhiteSpace(channelCode))
			{
				throw new ArgumentException("channelCode darf nicht leer sein.", nameof(channelCode));
			}

			channelCode = channelCode.Trim().ToUpperInvariant();

			return Task.Run(() =>
			{
				ct.ThrowIfCancellationRequested();

				int channels = channelCode.Length;

				// Support: 1 (GRAY), 3 (RGB/BGR), 4 (RGBA/BGRA/ARGB/ABGR)
				if (channels is not (1 or 3 or 4))
				{
					return (Image?) null;
				}

				long expected = (long) width * height * channels;
				if (data.LongLength != expected)
				{
					return (Image?) null;
				}

				// PixelFormat bestimmen
				PixelFormat pf = channels switch
				{
					4 => PixelFormat.Format32bppArgb, // wir legen als 32bpp ab (Memory typischerweise BGRA)
					3 => PixelFormat.Format24bppRgb,  // 24bpp BGR in Memory
					1 => PixelFormat.Format8bppIndexed,
					_ => throw new InvalidOperationException()
				};

				Bitmap bmp = new(width, height, pf);

				try
				{
					if (channels == 1)
					{
						// 8bpp braucht Palette
						var pal = bmp.Palette;
						for (int i = 0; i < 256; i++)
						{
							pal.Entries[i] = Color.FromArgb(i, i, i);
						}

						bmp.Palette = pal;
					}

					var rect = new Rectangle(0, 0, width, height);
					var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, pf);

					try
					{
						ct.ThrowIfCancellationRequested();

						int dstStride = bd.Stride;
						int srcStride = width * channels;

						unsafe
						{
							byte* dstBase = (byte*) bd.Scan0;

							fixed (byte* srcBase0 = data)
							{
								// Row-by-row copy with channel mapping if needed
								for (int y = 0; y < height; y++)
								{
									ct.ThrowIfCancellationRequested();

									byte* dstRow = dstBase + (y * dstStride);
									byte* srcRow = srcBase0 + (y * srcStride);

									if (channels == 4)
									{
										// Ziel im Bitmap: BGRA Layout (für Format32bppArgb in Memory üblich)
										// Wir mappen je nach channelCode.
										for (int x = 0; x < width; x++)
										{
											byte c0 = srcRow[x * 4 + 0];
											byte c1 = srcRow[x * 4 + 1];
											byte c2 = srcRow[x * 4 + 2];
											byte c3 = srcRow[x * 4 + 3];

											(byte b, byte g, byte r, byte a) = Map4(channelCode, c0, c1, c2, c3);

											int di = x * 4;
											dstRow[di + 0] = b;
											dstRow[di + 1] = g;
											dstRow[di + 2] = r;
											dstRow[di + 3] = a;
										}
									}
									else if (channels == 3)
									{
										// Ziel im Bitmap: BGR Layout (Format24bppRgb)
										for (int x = 0; x < width; x++)
										{
											byte c0 = srcRow[x * 3 + 0];
											byte c1 = srcRow[x * 3 + 1];
											byte c2 = srcRow[x * 3 + 2];

											(byte b, byte g, byte r) = Map3(channelCode, c0, c1, c2);

											int di = x * 3;
											dstRow[di + 0] = b;
											dstRow[di + 1] = g;
											dstRow[di + 2] = r;
										}
									}
									else // channels == 1
									{
										// 8bpp: direkt kopieren, stride beachten
										Marshal.Copy(data, y * srcStride, (IntPtr) dstRow, srcStride);
									}
								}
							}
						}
					}
					finally
					{
						bmp.UnlockBits(bd);
					}

					// Wichtig: KEIN using hier — wir geben Bitmap zurück
					return (Image) bmp;
				}
				catch
				{
					bmp.Dispose();
					return null;
				}

			}, ct);

			static (byte b, byte g, byte r, byte a) Map4(string code, byte c0, byte c1, byte c2, byte c3)
			{
				// input order per code -> output BGRA
				// Supported: BGRA, RGBA, ARGB, ABGR
				return code switch
				{
					"BGRA" => (c0, c1, c2, c3),
					"RGBA" => (c2, c1, c0, c3),
					"ARGB" => (c3, c2, c1, c0),
					"ABGR" => (c1, c2, c3, c0),
					_ => (c0, c1, c2, c3)
				};
			}

			static (byte b, byte g, byte r) Map3(string code, byte c0, byte c1, byte c2)
			{
				// Supported: BGR, RGB
				return code switch
				{
					"BGR" => (c0, c1, c2),
					"RGB" => (c2, c1, c0),
					_ => (c0, c1, c2)
				};
			}
		}

		public static async Task<Image[]> LoadImagesParallelAsync(
			string[] imageFilePaths, string? imagesDir = null,
			int maxWorkers = 0, IProgress<double>? progress = null, CancellationToken ct = default)
		{
			if (imageFilePaths is null) throw new ArgumentNullException(nameof(imageFilePaths));
			if (imageFilePaths.Length == 0) return Array.Empty<Image>();

			maxWorkers = maxWorkers <= 0
				? Environment.ProcessorCount
				: Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

			string? baseDir = string.IsNullOrWhiteSpace(imagesDir) ? null : Path.GetFullPath(imagesDir);

			// Pfade normalisieren
			var paths = imageFilePaths
				.Where(p => !string.IsNullOrWhiteSpace(p))
				.Select(p =>
				{
					var trimmed = p.Trim();

					if (Path.IsPathRooted(trimmed))
						return Path.GetFullPath(trimmed);

					if (baseDir != null)
						return Path.GetFullPath(Path.Combine(baseDir, trimmed));

					return Path.GetFullPath(trimmed);
				})
				.ToArray();

			int total = paths.Length;
			if (total == 0) return Array.Empty<Image>();

			progress?.Report(0);

			var gate = new SemaphoreSlim(maxWorkers, maxWorkers);
			var results = new Image?[total];
			int done = 0;

			// Pro Pfad genau 1 Task, aber gleichzeitige I/O wird begrenzt via gate.
			var tasks = new Task[total];

			for (int i = 0; i < total; i++)
			{
				int idx = i;
				string path = paths[idx];

				tasks[idx] = Task.Run(async () =>
				{
					await gate.WaitAsync(ct).ConfigureAwait(false);
					try
					{
						ct.ThrowIfCancellationRequested();

						// Wenn Datei fehlt -> null
						if (!File.Exists(path))
							return;

						// Wichtig: Image.FromFile hält File-Handle offen.
						// Daher: FileStream + Clone, dann Stream schließen.
						using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
						using var tmp = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);

						// Clone erzeugt unabhängiges Image (keine Stream-Abhängigkeit)
						results[idx] = (Image) tmp.Clone();
					}
					catch
					{
						// Fehler => null (wir skippen)
					}
					finally
					{
						gate.Release();
						int now = Interlocked.Increment(ref done);
						progress?.Report(now / (double) total);
					}
				}, ct);
			}

			await Task.WhenAll(tasks).ConfigureAwait(false);

			// nulls rausfiltern, Reihenfolge der erfolgreichen bleibt stabil
			return results.Where(img => img != null).Cast<Image>().ToArray();
		}
	}
}
