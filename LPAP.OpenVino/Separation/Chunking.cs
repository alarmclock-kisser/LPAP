namespace LPAP.OpenVino.Separation
{
	internal static class Chunking
	{
		public sealed record Chunk(int Index, int StartFrame, int LengthFrames);

		public static IReadOnlyList<Chunk> Build(int totalFrames, int chunkFrames, double overlapFraction)
		{
			if (totalFrames <= 0)
			{
				return Array.Empty<Chunk>();
			}

			if (chunkFrames <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(chunkFrames));
			}

			overlapFraction = Math.Clamp(overlapFraction, 0.0, 0.95);

			int overlap = (int) Math.Round(chunkFrames * overlapFraction);
			int hop = Math.Max(1, chunkFrames - overlap);

			var chunks = new List<Chunk>();
			int idx = 0;

			for (int start = 0; start < totalFrames; start += hop)
			{
				int len = Math.Min(chunkFrames, totalFrames - start);
				chunks.Add(new Chunk(idx++, start, len));
				if (start + len >= totalFrames)
				{
					break;
				}
			}

			return chunks;
		}
	}
}
