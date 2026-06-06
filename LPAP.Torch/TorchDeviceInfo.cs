using System;

namespace LPAP.Torch
{
    public sealed class TorchDeviceInfo
    {
        public int Index { get; init; }
        public string Kind { get; init; } = "CPU"; // "CUDA" | "CPU"
        public string Name { get; init; } = "CPU";
        public bool IsCuda { get; init; }
        public override string ToString() => $"{this.Index}: {this.Kind} - {this.Name}";
    }
}
