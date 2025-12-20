#nullable enable
namespace LPAP.Cuda
{
	public static partial class NvencVideoRenderer
	{
		public static class EditingMovPresets
		{
			// Apple ProRes (MOV)
			public static NvencOptions ProRes_422LT => new(
				VideoCodec: "prores_ks",
				Preset: "",                 // prores nutzt kein -preset wie x264/nvenc
				VideoBitrateKbps: null,
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: null,
				Crf: null,                  // <- CPU-CRF irrelevant für ProRes
				Profile: "1",               // prores_ks: 0=proxy 1=lt 2=standard 3=hq 4=4444 5=4444xq
				FastStart: true
			);

			public static NvencOptions ProRes_422HQ => new(
				VideoCodec: "prores_ks",
				Preset: "",
				VideoBitrateKbps: null,
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: null,
				Crf: null,
				Profile: "3",
				FastStart: true
			);

			public static NvencOptions ProRes_4444 => new(
				VideoCodec: "prores_ks",
				Preset: "",
				VideoBitrateKbps: null,
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: null,
				Crf: null,
				Profile: "4",
				FastStart: true
			);

			// Avid DNxHD (MOV) – guter Schnitt-Intermediate
			public static NvencOptions DNxHD_145_1080p => new(
				VideoCodec: "dnxhd",
				Preset: "",
				VideoBitrateKbps: 145000,   // 145 Mbit/s
				MaxBitrateKbps: null,
				BufferSizeKbps: null,
				ConstantQuality: null,
				Crf: null,                  // <- irrelevant bei DNxHD (wir fahren Bitrate)
				Profile: null,
				FastStart: true
			);
		}
	}
}
