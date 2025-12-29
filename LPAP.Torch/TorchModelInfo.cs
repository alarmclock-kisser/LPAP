using System;
using System.IO;

namespace LPAP.Torch
{
	public sealed class TorchModelInfo
	{
		public string Name { get; init; } = "";
		public string FullPath { get; init; } = "";
		public long SizeBytes { get; init; }
		public DateTime LastWriteTime { get; init; }

		public string Extension => Path.GetExtension(this.FullPath);
		public override string ToString() => $"{this.Name} ({this.Extension}, {this.SizeBytes / (1024.0 * 1024.0):0.#} MB)";
	}
}
