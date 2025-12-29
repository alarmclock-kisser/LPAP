using LPAP.Audio;
using LPAP.Torch.Adapters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TorchSharp;
using static TorchSharp.torch;

namespace LPAP.Torch
{
	public sealed class TorchService : IDisposable
	{
		public string ModelsPath { get; set; } = @"D:\Models\";

		public BindingList<TorchDeviceInfo> Devices { get; } = new();
		public BindingList<TorchModelInfo> Models { get; } = new();

		public TorchDeviceInfo? CurrentDeviceInfo { get; private set; }
		public torch.Device? CurrentDevice { get; private set; }

		public TorchModelInfo? CurrentModelInfo { get; private set; }
		public torch.jit.ScriptModule? CurrentModel { get; private set; }

		public IStemSeparationModelAdapter Adapter { get; set; }

		public TorchService()
		{
			// Default Adapter: "Demucs-like"
			this.Adapter = new DemucsLikeAdapter();
			this.RefreshDevices();
			this.RefreshModels();
		}

		public void RefreshDevices()
		{
			this.Devices.RaiseListChangedEvents = false;
			try
			{
				this.Devices.Clear();

				// CPU immer anbieten
				this.Devices.Add(new TorchDeviceInfo
				{
					Index = -1,
					Kind = "CPU",
					Name = "CPU",
					IsCuda = false
				});

				// CUDA devices (falls verfügbar)
				bool cudaAvail = false;
				try { cudaAvail = torch.cuda.is_available(); } catch { cudaAvail = false; }

				if (cudaAvail)
				{
					int count = 0;
					try { count = torch.cuda.device_count(); } catch { count = 0; }

					for (int i = 0; i < count; i++)
					{
						string name;
						try { name = $"CUDA:{i}"; } // TorchSharp bietet keine get_device_name-Methode
						catch { name = $"CUDA:{i}"; }

						this.Devices.Add(new TorchDeviceInfo
						{
							Index = i,
							Kind = "CUDA",
							Name = name,
							IsCuda = true
						});
					}
				}
			}
			finally
			{
				this.Devices.RaiseListChangedEvents = true;
				this.Devices.ResetBindings();
			}
		}

		public void RefreshModels()
		{
			this.Models.RaiseListChangedEvents = false;
			try
			{
				this.Models.Clear();

				Directory.CreateDirectory(this.ModelsPath);

				// TorchScript ist hier am sinnvollsten für TorchSharp (jit.load)
				// Typische Endungen: .pt .pth .ts .torchscript
				var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				{
					".pt", ".pth", ".ts", ".torchscript", ".zip"
				};

				foreach (var file in Directory.EnumerateFiles(this.ModelsPath, "*.*", SearchOption.TopDirectoryOnly))
				{
					var ext = Path.GetExtension(file);
					if (!exts.Contains(ext))
					{
						continue;
					}

					var fi = new FileInfo(file);
					this.Models.Add(new TorchModelInfo
					{
						Name = Path.GetFileName(file),
						FullPath = fi.FullName,
						SizeBytes = fi.Length,
						LastWriteTime = fi.LastWriteTime
					});
				}
			}
			finally
			{
				this.Models.RaiseListChangedEvents = true;
				this.Models.ResetBindings();
			}
		}

		public void InitDeviceByIndex(int deviceIndex)
		{
			// -1 => CPU
			if (deviceIndex < 0)
			{
				this.CurrentDevice = torch.CPU;
				this.CurrentDeviceInfo = this.Devices.FirstOrDefault(d => !d.IsCuda) ?? new TorchDeviceInfo { Index = -1, Kind = "CPU", Name = "CPU" };
				return;
			}

			// CUDA:i
			this.CurrentDevice = torch.device(DeviceType.CUDA, deviceIndex);
			this.CurrentDeviceInfo = this.Devices.FirstOrDefault(d => d.IsCuda && d.Index == deviceIndex)
				?? new TorchDeviceInfo { Index = deviceIndex, Kind = "CUDA", Name = $"CUDA:{deviceIndex}", IsCuda = true };
		}

		public void LoadModelByName(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName))
			{
				throw new ArgumentException("Model name is empty.", nameof(fileName));
			}

			// resolve path
			var match = this.Models.FirstOrDefault(m => string.Equals(m.Name, fileName, StringComparison.OrdinalIgnoreCase));
			var path = match?.FullPath ?? Path.Combine(this.ModelsPath, fileName);

			if (!File.Exists(path))
			{
				throw new FileNotFoundException("Model file not found.", path);
			}

			// dispose old
			this.CurrentModel?.Dispose();
			this.CurrentModel = null;

			// TorchScript load
			// (TorchSharp TorchScript support: torch.jit.load)
			this.CurrentModel = torch.jit.load(path);
			this.CurrentModel.eval();

			this.CurrentModelInfo = match ?? new TorchModelInfo
			{
				Name = Path.GetFileName(path),
				FullPath = path,
				SizeBytes = new FileInfo(path).Length,
				LastWriteTime = File.GetLastWriteTime(path)
			};

			// Optional: wenn Device schon gesetzt => model to device (TorchScript Module unterstützt .to(device))
			if (this.CurrentDevice != null)
			{
				try { this.CurrentModel.to(this.CurrentDevice); } catch { /* manche Exports blocken .to */ }
			}
		}

		public async Task<StemSeparationResult> SeparateAsync(
			AudioObj audio,
			StemSeparationOptions? options = null,
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			if (audio == null)
			{
				throw new ArgumentNullException(nameof(audio));
			}

			options ??= new StemSeparationOptions();

			if (this.CurrentModel == null)
			{
				throw new InvalidOperationException("No model loaded. Call LoadModelByName() first.");
			}

			if (this.CurrentDevice == null)
			{
				this.InitDeviceByIndex(-1);
			}

			var swTotal = Stopwatch.StartNew();
			var result = new StemSeparationResult
			{
				ModelName = this.CurrentModelInfo?.Name ?? "model",
				DeviceName = this.CurrentDeviceInfo?.ToString() ?? "device"
			};

			// optional preprocessing: resample/channel (deaktiviert per default, weil du es evtl. bewusst nicht willst)
			if (options.AutoResampleIfNeeded && audio.SampleRate > 0 && audio.SampleRate != options.TargetSampleRate)
			{
				await audio.ResampleAsync(options.TargetSampleRate, options.MaxWorkers).ConfigureAwait(false);
			}
			if (options.EnsureStereoIfNeeded && audio.Channels > 0 && audio.Channels != options.ExpectedInputChannels)
			{
				await audio.TransformChannelsAsync(options.ExpectedInputChannels, options.MaxWorkers).ConfigureAwait(false);
			}

			// chunk
			var swChunk = Stopwatch.StartNew();
			List<float[]> chunks = await audio.GetChunksAsync(
				chunkSize: options.ChunkSize,
				overlap: options.Overlap,
				maxWorkers: options.MaxWorkers,
				keepData: options.KeepSourceData
			).ConfigureAwait(false);
			swChunk.Stop();

			if (chunks.Count == 0)
			{
				return result;
			}

			// inference loop
			var swInf = Stopwatch.StartNew();

			// ensure model on device (best-effort)
			try { this.CurrentModel.to(this.CurrentDevice!); } catch { }

			// init stem lists
			var stemNames = this.Adapter.StemNames;
			foreach (var sn in stemNames)
			{
				result.StemChunks[sn] = new List<float[]>(capacity: chunks.Count);
			}

			for (int i = 0; i < chunks.Count; i++)
			{
				ct.ThrowIfCancellationRequested();

				// 0..1 overall
				progress?.Report(i / (double) Math.Max(1, chunks.Count));

				var chunk = chunks[i];

				// run Torch ops off UI thread (TorchSharp ist sync)
				var stemsForChunk = await Task.Run(() =>
				{
					using var noGrad = torch.no_grad();

					using var input = this.Adapter.CreateInputTensor(
						interleavedChunk: chunk,
						channels: Math.Max(1, audio.Channels),
						device: this.CurrentDevice!);

					// forward
					// TorchScript Module call: model.forward(IValue)
					// TorchSharp returns Tensor or IValue depending on model
					var output = this.CurrentModel!.forward(input);
					if (output == null)
					{
						throw new InvalidOperationException("Model forward returned null.");
					}

					Tensor outTensor = output as Tensor ?? throw new InvalidOperationException("Model forward gibt keinen Tensor zurück.");

					var parsed = this.Adapter.ParseOutputToInterleaved(outTensor, outChannels: Math.Max(1, audio.Channels));
					this.Adapter.PostProcessInPlace(parsed);

					return parsed;

				}, ct).ConfigureAwait(false);

				// append to result (keep output order)
				foreach (var kv in stemsForChunk)
				{
					if (!result.StemChunks.TryGetValue(kv.Key, out var list))
					{
						list = new List<float[]>(capacity: chunks.Count);
						result.StemChunks[kv.Key] = list;
					}
					list.Add(kv.Value);
				}
			}

			swInf.Stop();
			swTotal.Stop();
			progress?.Report(1.0);

			result.MetricsSeconds["Chunk"] = swChunk.Elapsed.TotalSeconds;
			result.MetricsSeconds["Inference"] = swInf.Elapsed.TotalSeconds;
			result.MetricsSeconds["Total"] = swTotal.Elapsed.TotalSeconds;

			return result;
		}

		public async Task<Dictionary<string, AudioObj>> BuildStemAudioObjectsAsync(
			AudioObj source,
			StemSeparationResult separation,
			int maxWorkers = 0,
			CancellationToken ct = default)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			if (separation == null)
			{
				throw new ArgumentNullException(nameof(separation));
			}

			var outDict = new Dictionary<string, AudioObj>(StringComparer.OrdinalIgnoreCase);

			foreach (var kv in separation.StemChunks)
			{
				ct.ThrowIfCancellationRequested();

				var stemName = kv.Key;
				var stemChunks = kv.Value;

				var stem = new AudioObj
				{
					Name = $"{source.Name} [{stemName}]",
					SampleRate = source.SampleRate,
					Channels = source.Channels,
					BitDepth = source.BitDepth,
					StretchFactor = 1.0,
					ChunkSize = source.ChunkSize,
					Overlap = source.Overlap,
					OverlapSize = source.OverlapSize
				};

				// nutzt dein Overlap-Add Aggregation
				await stem.AggregateStretchedChunksAsync(stemChunks, maxWorkers: maxWorkers, keepPointer: false).ConfigureAwait(false);
				stem.DataChanged();

				outDict[stemName] = stem;
			}

			return outDict;
		}

		public void Dispose()
		{
			try { this.CurrentModel?.Dispose(); } catch { }
			this.CurrentModel = null;
		}
	}
}
