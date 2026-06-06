using System;
using System.Collections.Generic;

namespace LPAP.Torch
{
    public sealed class StemSeparationResult
    {
        public string ModelName { get; init; } = "";
        public string DeviceName { get; init; } = "";

        // stem -> list of chunks (each chunk is interleaved float[] like your AudioObj.Data)
        public Dictionary<string, List<float[]>> StemChunks { get; } = new(StringComparer.OrdinalIgnoreCase);

        // optional: timing/metrics
        public Dictionary<string, double> MetricsSeconds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
