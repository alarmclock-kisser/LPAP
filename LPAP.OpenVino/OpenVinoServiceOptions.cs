using System.Collections.Concurrent;

namespace LPAP.OpenVino
{
	public sealed class OpenVinoServiceOptions
	{
		/// <summary>
		/// Default device name for compile/infer, e.g. "AUTO", "CPU", "GPU".
		/// </summary>
		public string DefaultDevice { get; set; } = "AUTO";

		/// <summary>
		/// Root directory where models are located. Default: {BaseDir}/Models
		/// </summary>
		public string ModelsRootDirectory { get; set; } =
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

		/// <summary>
		/// Cache compiled models by (modelPath + device + configKey).
		/// </summary>
		public bool CacheCompiledModels { get; set; } = true;

		/// <summary>
		/// Optional: max number of compiled-model cache entries.
		/// </summary>
		public int MaxCompiledModelCacheEntries { get; set; } = 8;

		internal readonly ConcurrentDictionary<string, object> Locks = new();
	}
}
