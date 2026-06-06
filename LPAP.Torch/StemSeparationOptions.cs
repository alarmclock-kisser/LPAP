namespace LPAP.Torch
{
    public sealed class StemSeparationOptions
    {
        // Chunking
        public int ChunkSize { get; set; } = 44100 * 6;   // ~6s @44.1kHz (interleaved bleibt bei dir im float[])
        public float Overlap { get; set; } = 0.5f;
        public int MaxWorkers { get; set; } = 0;
        public bool KeepSourceData { get; set; } = true;

        // Model IO
        public int ExpectedInputChannels { get; set; } = 2; // viele music-sep Modelle erwarten stereo
        public int TargetSampleRate { get; set; } = 44100;  // viele Demucs/HDemucs arbeiten mit 44.1k
        public bool AutoResampleIfNeeded { get; set; } = false; // optional (du hast ResampleAsync im AudioObj)
        public bool EnsureStereoIfNeeded { get; set; } = false; // optional (du hast TransformChannelsAsync)

        // Output
        public string[]? PreferredStemNames { get; set; } = new[] { "vocals", "drums", "bass", "other" };
    }
}
