using LPAP.Audio;
using LPAP.OpenVino.Models;
using LPAP.OpenVino.Separation;
using LPAP.OpenVino.Util;
using OpenVinoSharp;

namespace LPAP.OpenVino
{
	public sealed class OpenVinoService : IDisposable
	{
		private readonly OpenVinoServiceOptions _opts;
		private readonly Core _core;

		// compiled model cache
		private readonly Dictionary<string, OpenVinoModelHandle> _cache = new(StringComparer.OrdinalIgnoreCase);
		private readonly LinkedList<string> _lru = new();

		public OpenVinoService(OpenVinoServiceOptions? options = null)
		{
			this._opts = options ?? new OpenVinoServiceOptions();
			this._core = new Core(); // official flow: using OpenVinoSharp; Core core = new Core(); :contentReference[oaicite:1]{index=1}
		}

		public void Dispose()
		{
			foreach (var kv in this._cache)
			{
				kv.Value.Dispose();
			}

			this._cache.Clear();
			this._lru.Clear();
			this._core.Dispose();
		}

		// ---------------------------
		// Devices
		// ---------------------------

		public IReadOnlyList<OpenVinoDeviceInfo> GetAvailableDeviceInfos()
		{
			var ids = this._core.get_available_devices();
			var list = new List<OpenVinoDeviceInfo>(ids.Count);

			foreach (var id in ids)
			{
				string? full = this.TryGetDevicePropertyString(id, "FULL_DEVICE_NAME");
				string? name = this.TryGetDevicePropertyString(id, "DEVICE_NAME");

				list.Add(new OpenVinoDeviceInfo(id, full, name));
			}

			return list;
		}

		public Task<IReadOnlyList<OpenVinoDeviceInfo>> GetAvailableDeviceInfosAsync(CancellationToken ct = default)
			=> Task.Run(() => { ct.ThrowIfCancellationRequested(); return this.GetAvailableDeviceInfos(); }, ct);

		private string? TryGetDevicePropertyString(string deviceId, string key)
		{
			try
			{
				// OpenVINO supports querying device properties at runtime. :contentReference[oaicite:1]{index=1}
				var v = this._core.get_property(deviceId, key);
				return v?.ToString();
			}
			catch
			{
				return null;
			}
		}

		// ---------------------------
		// Model catalog / infos
		// ---------------------------

		public IReadOnlyList<string> ListModels()
		{
			var models = ModelCatalog.Scan(this._opts.ModelsRootDirectory);
			return models.Select(m => m.name).ToArray();
		}

		public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
			=> Task.Run(() => { ct.ThrowIfCancellationRequested(); return this.ListModels(); }, ct);

		public OpenVinoModelInfo GetModelInfo(string modelNameOrPath)
		{
			var (name, xmlPath, binPath) = this.ResolveModel(modelNameOrPath);

			using var model = this._core.read_model(xmlPath);

			var inputs = ReadPorts(model.inputs());
			var outputs = ReadPorts(model.outputs());

			return new OpenVinoModelInfo(name, xmlPath, binPath, inputs, outputs);
		}

		public Task<OpenVinoModelInfo> GetModelInfoAsync(string modelNameOrPath, CancellationToken ct = default)
			=> Task.Run(() => { ct.ThrowIfCancellationRequested(); return this.GetModelInfo(modelNameOrPath); }, ct);

		// ---------------------------
		// Load / compile model
		// ---------------------------

		public OpenVinoModelHandle LoadModel(string modelNameOrPath, string? device = null)
		{
			device ??= this._opts.DefaultDevice;

			var (name, xmlPath, _) = this.ResolveModel(modelNameOrPath);
			string cacheKey = $"{xmlPath}||{device}";

			if (this._opts.CacheCompiledModels)
			{
				lock (this._cache)
				{
					if (this._cache.TryGetValue(cacheKey, out var cached))
					{
						this.Touch(cacheKey);
						return cached;
					}
				}
			}

			// Compile fresh
			var model = this._core.read_model(xmlPath);
			var compiled = this._core.compile_model(model, device); // core.compiled_model(model, "AUTO") :contentReference[oaicite:3]{index=3}

			var inputs = ReadPorts(model.inputs()).ToArray();
			var outputs = ReadPorts(model.outputs()).ToArray();

			var handle = new OpenVinoModelHandle(name, xmlPath, device, model, compiled, inputs, outputs);

			if (this._opts.CacheCompiledModels)
			{
				lock (this._cache)
				{
					this._cache[cacheKey] = handle;
					this.Touch(cacheKey);
					this.EnforceCacheLimit();
				}
			}

			return handle;
		}

		public Task<OpenVinoModelHandle> LoadModelAsync(string modelNameOrPath, string? device = null, CancellationToken ct = default)
			=> Task.Run(() => { ct.ThrowIfCancellationRequested(); return this.LoadModel(modelNameOrPath, device); }, ct);

		// ---------------------------
		// Separation (single entry)
		// ---------------------------

		public async Task<AudioObj[]> SeparateToStemsAsync(
			AudioObj input,
			string modelNameOrPath,
			MusicSeparationOptions? options = null,
			string? device = null,
			IProgress<double>? progress = null,
			CancellationToken ct = default)
		{
			Guard.NotNull(input, nameof(input));
			options ??= new MusicSeparationOptions();

			// Load/compile model (cached)
			var handle = await this.LoadModelAsync(modelNameOrPath, device, ct).ConfigureAwait(false);

			// Convert audio format if required
			AudioObj audio = input;
			if (options.AutoConvertAudioFormat)
			{
				// Avoid mutating original: clone shallow and replace Data if needed
				audio = await input.CloneAsync(keepId: false, ct).ConfigureAwait(false);

				if (audio.SampleRate != options.TargetSampleRate)
				{
					await audio.ResampleAsync(options.TargetSampleRate, maxWorkers: 0).ConfigureAwait(false);
				}

				if (audio.Channels != options.TargetChannels)
				{
					await audio.TransformChannelsAsync(options.TargetChannels, maxWorkers: 0).ConfigureAwait(false);
				}
			}

			ct.ThrowIfCancellationRequested();
			progress?.Report(0.01);

			return await Task.Run(() =>
			{
				ct.ThrowIfCancellationRequested();

				// 1) Determine expected input layout/frames from model input
				// Common for audio: [N,C,T] float32
				var in0 = handle.Inputs.FirstOrDefault();
				Guard.True(in0 is not null, "Model has no inputs.");

				var inputShape = in0?.Shape;
				Guard.True(inputShape?.Length is 3 or 4, $"Unexpected input rank={inputShape?.Length}. Expected 3 (NCT) or 4.");

				// If rank==4, some models use [N, C, F, T] (spectrogram-ish). This implementation is for waveform models.
				Guard.True(inputShape?.Length == 3, "This implementation expects waveform input [N,C,T]. If your model is [N,C,F,T], tell me and I adapt.");

				int n = (int) (inputShape?[0] ?? 1);
				int c = (int) (inputShape?[1] ?? 1);
				int t = (int) (inputShape?[2] ?? 1);

				Guard.True(c == audio.Channels, $"Model expects C={c} but audio has Channels={audio.Channels}.");
				int chunkFrames = options.ChunkFrames > 0 ? options.ChunkFrames : t;
				Guard.True(chunkFrames > 0, "chunkFrames must be > 0.");

				// 2) Prepare channel-major
				var chanMajor = AudioTensorConverter.Deinterleave(audio.Data, audio.Channels);
				int totalFrames = chanMajor[0].Length;

				// 3) Chunk plan
				var chunks = Chunking.Build(totalFrames, chunkFrames, options.OverlapFraction);
				Guard.True(chunks.Count > 0, "No chunks.");

				// 4) Infer in chunks (possibly batched)
				// Determine stems/channels/frames from output shape after first infer
				// We do one warmup infer on first chunk to discover output dims.
				progress?.Report(0.05);

				using var infer0 = handle.Compiled.create_infer_request(); // compiled_model.create_infer_request() :contentReference[oaicite:4]{index=4}
				var outSpec = InferOneChunkDiscover(infer0, c, chunkFrames, chanMajor, chunks[0], ct);

				int stems = outSpec.Stems;
				int outFramesPerChunk = outSpec.Frames; // usually == chunkFrames

				// Prepare collectors: for each stem, list of per-chunk [C][len]
				var stemChunks = new List<float[][]>[stems];
				for (int s = 0; s < stems; s++)
				{
					stemChunks[s] = new List<float[][]>(chunks.Count);
				}

				// First chunk results already computed:
				for (int s = 0; s < stems; s++)
				{
					stemChunks[s].Add(outSpec.ChunkStems[s]);
				}

				int doneChunks = 1;
				progress?.Report(0.10);

				// Batch if possible
				bool canBatch = options.EnableBatching && options.BatchSize > 1 && n != 1; // if model has fixed batch=1 then n==1
																						   // Note: some models have N=-1 dynamic; wrapper may expose -1. If so, allow batching.
				if ((inputShape?[0] ?? 1) <= 0)
				{
					canBatch = options.EnableBatching && options.BatchSize > 1;
				}

				if (canBatch)
				{
					// Batched processing: build batches of chunks and infer as [B,C,T]
					// Requires model to accept batch dimension !=1. If it doesn't, we fallback per-chunk.
					try
					{
						while (doneChunks < chunks.Count)
						{
							ct.ThrowIfCancellationRequested();

							int batchSize = Math.Min(options.BatchSize, chunks.Count - doneChunks);
							using var infer = handle.Compiled.create_infer_request();

							var batchSlices = new List<float[][]>(batchSize);
							for (int b = 0; b < batchSize; b++)
							{
								var ch = chunks[doneChunks + b];
								batchSlices.Add(AudioTensorConverter.SlicePad(chanMajor, ch.StartFrame, chunkFrames));
							}

							var flat = AudioTensorConverter.FlattenNct(batchSlices, batchSize, c, chunkFrames);

							// Set input tensor data
							var inTensor = infer.get_tensor(handle.Inputs[0].AnyName ?? "input");
							// Safer: use the returned tensor and set_data
							inTensor.set_data<float>(flat); // set_data<float>(input_data); :contentReference[oaicite:5]{index=5}

							infer.infer(); // synchronous infer :contentReference[oaicite:6]{index=6}

							// Get output and split per batch + stem
							var outTensor = infer.get_output_tensor(0);
							var outShape = outTensor.get_shape();
							var outSize = (int) outTensor.get_size();
							var outData = outTensor.get_data<float>(outSize); // get_data<float>(len) :contentReference[oaicite:7]{index=7}

							var parsed = ParseOutputBatched(outShape, outData, batchSize, stemsExpected: stems, channelsExpected: c);

							for (int b = 0; b < batchSize; b++)
							{
								for (int s = 0; s < stems; s++)
								{
									stemChunks[s].Add(parsed[b][s]);
								}
							}

							doneChunks += batchSize;

							double p = 0.10 + 0.85 * (doneChunks / (double) chunks.Count);
							progress?.Report(Math.Min(0.97, p));
						}
					}
					catch
					{
						// Fallback to sequential if batching fails due to shape mismatch
						canBatch = false;
						// Remove chunks added beyond first (keep first chunk already in lists)
						for (int s = 0; s < stems; s++)
						{
							stemChunks[s].RemoveRange(1, stemChunks[s].Count - 1);
						}

						doneChunks = 1;
					}
				}

				if (!canBatch)
				{
					// Sequential per chunk
					while (doneChunks < chunks.Count)
					{
						ct.ThrowIfCancellationRequested();
						using var infer = handle.Compiled.create_infer_request();

						var ch = chunks[doneChunks];
						var slice = AudioTensorConverter.SlicePad(chanMajor, ch.StartFrame, chunkFrames);

						var flat = AudioTensorConverter.FlattenNct(new[] { slice }, 1, c, chunkFrames);
						var inTensor = infer.get_tensor(handle.Inputs[0].AnyName ?? "input");
						inTensor.set_data<float>(flat);

						infer.infer();

						var outTensor = infer.get_output_tensor(0);
						var outShape = outTensor.get_shape();
						var outSize = (int) outTensor.get_size();
						var outData = outTensor.get_data<float>(outSize);

						var stemsChunk = ParseOutputSingle(outShape, outData, stemsExpected: stems, channelsExpected: c);

						for (int s = 0; s < stems; s++)
						{
							stemChunks[s].Add(stemsChunk[s]);
						}

						doneChunks++;
						double p = 0.10 + 0.85 * (doneChunks / (double) chunks.Count);
						progress?.Report(Math.Min(0.97, p));
					}
				}

				// 5) Stitch stems
				progress?.Report(0.98);

				var stemNames = options.StemNames;
				if (stemNames is null || stemNames.Length < stems)
				{
					stemNames = Enumerable.Range(0, stems).Select(i => $"stem{i}").ToArray();
				}

				var results = new AudioObj[stems];

				float inputPeak = options.MatchInputPeak ? AudioTensorConverter.PeakAbs(audio.Data) : 0f;

				for (int s = 0; s < stems; s++)
				{
					// For each chunk, we stored [C][chunkFrames] but last chunk may be shorter; we stored padded.
					// For stitching, we cut to each chunk.LengthFrames.
					var perChunk = new List<float[][]>(chunks.Count);
					for (int i = 0; i < chunks.Count; i++)
					{
						int len = chunks[i].LengthFrames;
						var buf = stemChunks[s][i];
						var cut = new float[c][];
						for (int cc = 0; cc < c; cc++)
						{
							cut[cc] = new float[len];
							Array.Copy(buf[cc], 0, cut[cc], 0, len);
						}
						perChunk.Add(cut);
					}

					var stitched = OverlapAdd.Stitch(c, totalFrames, chunks, perChunk, chunkFrames, options.OverlapFraction);
					var inter = AudioTensorConverter.Interleave(stitched);

					if (options.ClampOutput)
					{
						AudioTensorConverter.ClampInPlace(inter, -1f, 1f);
					}

					if (options.MatchInputPeak && inputPeak > 1e-8f)
					{
						float peak = AudioTensorConverter.PeakAbs(inter);
						if (peak > 1e-8f)
						{
							float gain = inputPeak / peak;
							for (int i = 0; i < inter.Length; i++)
							{
								inter[i] *= gain;
							}

							if (options.ClampOutput)
							{
								AudioTensorConverter.ClampInPlace(inter, -1f, 1f);
							}
						}
					}

					// Build AudioObj
					//var o = new AudioObj($"{audio.Name} [{stemNames[s]}]");
					//o.SampleRate = audio.SampleRate;
					//o.Channels = audio.Channels;
					//o.BitDepth = audio.BitDepth;
					//o.Data = inter;

					var o = AudioCollection.CreateNewEmpty(
						sampleRate: audio.SampleRate,
						channels: audio.Channels,
						bitDepth: audio.BitDepth,
						durationSeconds: inter.Length / (float) audio.SampleRate);
					o.Name = $"{audio.Name} [{stemNames[s]}]";
					o.Data = inter;

					results[s] = o;
				}

				progress?.Report(1.0);
				return results;

			}, ct).ConfigureAwait(false);
		}

		// ---------------------------
		// Internals
		// ---------------------------

		private static (int Stems, int Channels, int Frames, float[][][] ChunkStems) InferOneChunkDiscover(
			InferRequest infer,
			int channels,
			int chunkFrames,
			float[][] channelMajor,
			Chunking.Chunk chunk,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();

			var slice = AudioTensorConverter.SlicePad(channelMajor, chunk.StartFrame, chunkFrames);
			var flat = AudioTensorConverter.FlattenNct(new[] { slice }, 1, channels, chunkFrames);

			// get input tensor by name (if exact name is unknown, "input" may fail)
			// In OpenVinoSharp samples they use infer_request.get_tensor("image") then set_data. :contentReference[oaicite:8]{index=8}
			// We'll rely on get_input_tensor(0) if name mismatch is an issue in your model.
			Tensor inTensor;
			try
			{
				inTensor = infer.get_input_tensor(0);
			}
			catch
			{
				inTensor = infer.get_tensor("input");
			}

			inTensor.set_data<float>(flat);
			infer.infer();

			var outTensor = infer.get_output_tensor(0);
			var outShape = outTensor.get_shape();
			var outSize = (int) outTensor.get_size();
			var outData = outTensor.get_data<float>(outSize);

			// Parse: assume [N, S, C, T] or [N, C, T] (no stems) etc.
			// For music separation it’s typically stems. We’ll support both.
			if (outShape.data_size() == 4)
			{
				int n = (int) outShape[0];
				int s = (int) outShape[1];
				int c = (int) outShape[2];
				int t = (int) outShape[3];

				Guard.True(n == 1, "Expected N=1 in discovery infer.");
				Guard.True(c == channels, $"Output channels mismatch: {c} vs {channels}.");

				var stems = new float[s][][];
				int o = 0;
				for (int si = 0; si < s; si++)
				{
					stems[si] = new float[c][];
					for (int cc = 0; cc < c; cc++)
					{
						stems[si][cc] = new float[t];
						Array.Copy(outData, o, stems[si][cc], 0, t);
						o += t;
					}
				}
				return (s, c, t, stems);
			}
			else if (outShape.data_size() == 3)
			{
				// [N,C,T] => treat as 1 stem
				int n = (int) outShape[0];
				int c = (int) outShape[1];
				int t = (int) outShape[2];
				Guard.True(n == 1, "Expected N=1 in discovery infer.");
				Guard.True(c == channels, $"Output channels mismatch: {c} vs {channels}.");

				var stems = new float[1][][];
				stems[0] = new float[c][];
				int o = 0;
				for (int cc = 0; cc < c; cc++)
				{
					stems[0][cc] = new float[t];
					Array.Copy(outData, o, stems[0][cc], 0, t);
					o += t;
				}
				return (1, c, t, stems);
			}

			throw new NotSupportedException($"Unsupported output rank={outShape.data_size()} for audio separation.");
		}

		private static float[][][] ParseOutputSingle(Shape outShape, float[] outData, int stemsExpected, int channelsExpected)
		{
			if (outShape.data_size() == 4)
			{
				int n = (int) outShape[0];
				int s = (int) outShape[1];
				int c = (int) outShape[2];
				int t = (int) outShape[3];

				Guard.True(n == 1, "Expected N=1.");
				Guard.True(c == channelsExpected, "Channel mismatch.");
				Guard.True(s == stemsExpected, $"Stems mismatch: model={s}, expected={stemsExpected}");

				var stems = new float[s][][];
				int o = 0;
				for (int si = 0; si < s; si++)
				{
					stems[si] = new float[c][];
					for (int cc = 0; cc < c; cc++)
					{
						stems[si][cc] = new float[t];
						Array.Copy(outData, o, stems[si][cc], 0, t);
						o += t;
					}
				}
				return stems;
			}
			else if (outShape.data_size() == 3)
			{
				int n = (int) outShape[0];
				int c = (int) outShape[1];
				int t = (int) outShape[2];
				Guard.True(n == 1, "Expected N=1.");
				Guard.True(c == channelsExpected, "Channel mismatch.");
				Guard.True(stemsExpected == 1, "Expected 1 stem.");

				var stems = new float[1][][];
				stems[0] = new float[c][];
				int o = 0;
				for (int cc = 0; cc < c; cc++)
				{
					stems[0][cc] = new float[t];
					Array.Copy(outData, o, stems[0][cc], 0, t);
					o += t;
				}
				return stems;
			}

			throw new NotSupportedException("Unsupported output rank.");
		}

		private static float[][][][] ParseOutputBatched(Shape outShape, float[] outData, int batchSize, int stemsExpected, int channelsExpected)
		{
			// Expect [B,S,C,T]
			Guard.True(outShape.data_size() == 4, "Batched parse expects rank 4 [B,S,C,T].");
			int b = (int) outShape[0];
			int s = (int) outShape[1];
			int c = (int) outShape[2];
			int t = (int) outShape[3];

			Guard.True(b == batchSize, $"Batch mismatch {b} vs {batchSize}");
			Guard.True(s == stemsExpected, $"Stems mismatch {s} vs {stemsExpected}");
			Guard.True(c == channelsExpected, $"Channels mismatch {c} vs {channelsExpected}");

			var result = new float[b][][][];
			int o = 0;

			for (int bi = 0; bi < b; bi++)
			{
				result[bi] = new float[s][][];
				for (int si = 0; si < s; si++)
				{
					result[bi][si] = new float[c][];
					for (int cc = 0; cc < c; cc++)
					{
						result[bi][si][cc] = new float[t];
						Array.Copy(outData, o, result[bi][si][cc], 0, t);
						o += t;
					}
				}
			}

			return result;
		}

		private (string name, string xmlPath, string? binPath) ResolveModel(string modelNameOrPath)
		{
			Guard.NotNullOrWhiteSpace(modelNameOrPath, nameof(modelNameOrPath));

			// If user passed a path
			if (File.Exists(modelNameOrPath) && modelNameOrPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
			{
				var name = Path.GetFileNameWithoutExtension(modelNameOrPath);
				var bin = Path.ChangeExtension(modelNameOrPath, ".bin");
				return (name, modelNameOrPath, File.Exists(bin) ? bin : null);
			}

			// Otherwise search in catalog by name
			var models = ModelCatalog.Scan(this._opts.ModelsRootDirectory);
			var hit = models.FirstOrDefault(m => string.Equals(m.name, modelNameOrPath, StringComparison.OrdinalIgnoreCase));
			if (hit == default)
			{
				throw new FileNotFoundException($"Model '{modelNameOrPath}' not found in '{this._opts.ModelsRootDirectory}'.");
			}

			return hit;
		}

		private static IReadOnlyList<OpenVinoPortInfo> ReadPorts(dynamic ports)
		{
			// ports is a vector-like from wrapper; each port exposes:
			// - get_any_name()
			// - get_shape()
			// - get_element_type()
			// Wrapper mirrors OpenVINO C++ port API. :contentReference[oaicite:9]{index=9}
			var list = new List<OpenVinoPortInfo>();
			foreach (var p in ports)
			{
				string? anyName = null;
				try { anyName = p.get_any_name(); } catch { }

				long[] shape;
				try
				{
					var sh = p.get_shape();
					shape = new long[sh.size()];
					for (int i = 0; i < shape.Length; i++)
					{
						shape[i] = (long) sh[i];
					}
				}
				catch
				{
					shape = Array.Empty<long>();
				}

				string et = "unknown";
				try { et = p.get_element_type().ToString(); } catch { }

				list.Add(new OpenVinoPortInfo(anyName, shape, et));
			}
			return list;
		}

		private void Touch(string key)
		{
			// LRU
			var node = this._lru.Find(key);
			if (node is not null)
			{
				this._lru.Remove(node);
			}

			this._lru.AddFirst(key);
		}

		private void EnforceCacheLimit()
		{
			while (this._cache.Count > this._opts.MaxCompiledModelCacheEntries && this._lru.Last is not null)
			{
				var key = this._lru.Last.Value;
				this._lru.RemoveLast();

				if (this._cache.Remove(key, out var handle))
				{
					handle.Dispose();
				}
			}
		}
	}
}
