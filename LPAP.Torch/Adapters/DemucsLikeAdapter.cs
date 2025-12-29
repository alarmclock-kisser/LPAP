using System;
using System.Collections.Generic;
using System.Linq;
using TorchSharp;
using static TorchSharp.torch;

namespace LPAP.Torch.Adapters
{
	public sealed class DemucsLikeAdapter : IStemSeparationModelAdapter
	{
		public string AdapterName => "DemucsLikeAdapter";

		private readonly string[] _stemNames;

		public DemucsLikeAdapter(string[]? stemNames = null)
		{
			this._stemNames = (stemNames != null && stemNames.Length > 0)
				? stemNames
				: new[] { "vocals", "drums", "bass", "other" };
		}

		public IReadOnlyList<string> StemNames => this._stemNames;

		public Tensor CreateInputTensor(ReadOnlySpan<float> interleavedChunk, int channels, Device device)
		{
			// interleaved: [T * C]
			if (channels <= 0)
			{
				channels = 2;
			}

			int frames = interleavedChunk.Length / channels;
			if (frames <= 0)
			{
				frames = 1;
			}

			// Build tensor [1, C, T]
			// TorchSharp needs arrays; for perf you can pool buffers later.
			var perChannel = new float[channels * frames];

			// de-interleave into [C,T] contiguous
			// layout: ch-major
			for (int f = 0; f < frames; f++)
			{
				int srcBase = f * channels;
				for (int c = 0; c < channels; c++)
				{
					perChannel[c * frames + f] = interleavedChunk[srcBase + c];
				}
			}

			var t = torch.tensor(perChannel, dtype: ScalarType.Float32).reshape(1, channels, frames);
			return t.to(device);
		}

		public Dictionary<string, float[]> ParseOutputToInterleaved(Tensor modelOutput, int outChannels)
		{
			// Erwartete Formen:
			// [1,S,C,T] oder [S,C,T] oder [S,T] (mono) oder [1,C,T] (single output)
			var result = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

			using var cpu = modelOutput.detach().to(DeviceType.CPU);

			var shape = cpu.shape;
			if (shape.Length == 4)
			{
				// [B,S,C,T]
				int b = (int) shape[0];
				int s = (int) shape[1];
				int c = (int) shape[2];
				int t = (int) shape[3];

				int stems = s;
				int channels = c;

				// flatten to managed
				var data = cpu.contiguous().data<float>().ToArray();

				for (int si = 0; si < stems; si++)
				{
					string stemName = si < this._stemNames.Length ? this._stemNames[si] : $"stem{si}";
					result[stemName] = ToInterleaved(data, bIndex: 0, stemIndex: si, stems: stems, channels: channels, frames: t);
				}

				return result;
			}

			if (shape.Length == 3)
			{
				// [S,C,T] oder [B,C,T]
				int a0 = (int) shape[0];
				int a1 = (int) shape[1];
				int a2 = (int) shape[2];

				var data = cpu.contiguous().data<float>().ToArray();

				// Heuristik: wenn a0 <= 8 => stems
				if (a0 <= 8 && a0 > 1)
				{
					int stems = a0;
					int channels = a1;
					int frames = a2;

					for (int si = 0; si < stems; si++)
					{
						string stemName = si < this._stemNames.Length ? this._stemNames[si] : $"stem{si}";
						result[stemName] = ToInterleaved_3D_SCT(data, stemIndex: si, stems: stems, channels: channels, frames: frames);
					}

					return result;
				}
				else
				{
					// [B,C,T] => single output "other" (oder "mixture")
					int batch = a0;
					int channels = a1;
					int frames = a2;

					result["output"] = ToInterleaved_3D_BCT(data, bIndex: 0, batch: batch, channels: channels, frames: frames);
					return result;
				}
			}

			if (shape.Length == 2)
			{
				// [S,T] mono stems
				int stems = (int) shape[0];
				int frames = (int) shape[1];

				var data = cpu.contiguous().data<float>().ToArray();

				for (int si = 0; si < stems; si++)
				{
					string stemName = si < this._stemNames.Length ? this._stemNames[si] : $"stem{si}";
					// mono -> stereo-ish interleaved? Wir lassen mono als 1ch interleaved.
					var interleaved = new float[frames];
					Array.Copy(data, si * frames, interleaved, 0, frames);
					result[stemName] = interleaved;
				}
				return result;
			}

			// Fallback: alles als "output"
			result["output"] = cpu.contiguous().data<float>().ToArray();
			return result;
		}

		public void PostProcessInPlace(Dictionary<string, float[]> stemsInterleaved)
		{
			// defensiv: clamp auf [-1,1]
			foreach (var kv in stemsInterleaved)
			{
				var a = kv.Value;
				for (int i = 0; i < a.Length; i++)
				{
					float v = a[i];
					if (v > 1f)
					{
						a[i] = 1f;
					}
					else if (v < -1f)
					{
						a[i] = -1f;
					}
				}
			}
		}

		private static float[] ToInterleaved(float[] data, int bIndex, int stemIndex, int stems, int channels, int frames)
		{
			// data layout: [B,S,C,T] contiguous in row-major => (((b*S + s)*C + c)*T + t)
			var outInterleaved = new float[channels * frames];
			for (int t = 0; t < frames; t++)
			{
				int dstBase = t * channels;
				for (int c = 0; c < channels; c++)
				{
					int idx = (((bIndex * stems + stemIndex) * channels + c) * frames + t);
					outInterleaved[dstBase + c] = data[idx];
				}
			}
			return outInterleaved;
		}

		private static float[] ToInterleaved_3D_SCT(float[] data, int stemIndex, int stems, int channels, int frames)
		{
			// [S,C,T] => ((s*C + c)*T + t)
			var outInterleaved = new float[channels * frames];
			for (int t = 0; t < frames; t++)
			{
				int dstBase = t * channels;
				for (int c = 0; c < channels; c++)
				{
					int idx = ((stemIndex * channels + c) * frames + t);
					outInterleaved[dstBase + c] = data[idx];
				}
			}
			return outInterleaved;
		}

		private static float[] ToInterleaved_3D_BCT(float[] data, int bIndex, int batch, int channels, int frames)
		{
			// [B,C,T] => ((b*C + c)*T + t)
			var outInterleaved = new float[channels * frames];
			for (int t = 0; t < frames; t++)
			{
				int dstBase = t * channels;
				for (int c = 0; c < channels; c++)
				{
					int idx = ((bIndex * channels + c) * frames + t);
					outInterleaved[dstBase + c] = data[idx];
				}
			}
			return outInterleaved;
		}
	}
}
