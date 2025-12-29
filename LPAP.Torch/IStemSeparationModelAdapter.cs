using System;
using System.Collections.Generic;
using TorchSharp;

namespace LPAP.Torch
{
	/// <summary>
	/// Adapter kapselt: Input-Tensor Layout + Output Parsing.
	/// Weil "Demucs" / "UVR" / HF-Exports unterschiedlich rauskommen können.
	/// </summary>
	public interface IStemSeparationModelAdapter
	{
		string AdapterName { get; }

		/// <summary>Return stem names in output order (e.g. vocals/drums/bass/other).</summary>
		IReadOnlyList<string> StemNames { get; }

		/// <summary>
		/// Wandelt einen interleaved float[] chunk (AudioObj) in einen Modell-Input Tensor.
		/// Erwartet i.d.R. [1, C, T] float32.
		/// </summary>
		torch.Tensor CreateInputTensor(
			ReadOnlySpan<float> interleavedChunk,
			int channels,
			torch.Device device);

		/// <summary>
		/// Parst Model-Output zu interleaved float[] pro stem (in Chunk-Länge).
		/// </summary>
		Dictionary<string, float[]> ParseOutputToInterleaved(
			torch.Tensor modelOutput,
			int outChannels);

		/// <summary>
		/// Optional: manche Modelle wollen normalize/clamp.
		/// </summary>
		void PostProcessInPlace(Dictionary<string, float[]> stemsInterleaved);
	}
}
