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
		public async Task ExportWavAsync(string outputPath, CancellationToken ct = default)
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

				WaveFileWriter.CreateWaveFile(outputPath, sampleToWave);
			}, ct).ConfigureAwait(false);
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
