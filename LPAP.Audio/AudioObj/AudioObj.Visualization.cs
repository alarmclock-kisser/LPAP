// LPAP.Audio/AudioObj.Visualization.cs
using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
		[SupportedOSPlatform("windows")]
		public async Task<Bitmap> RenderWaveformAsync(
			int width,
			int height,
			int samplesPerPixel,
			long? offsetSamples = null,
			bool separateChannels = false,
			Color? backColor = null,
			Color? graphColor = null,
			float caretPosition = 0.5f,
			int caretWidth = 2,
			double timeMarkerIntervalSeconds = 0.0,
			Color? selectionColor = null,
			float selectionAlpha = 0.33f,
			CancellationToken ct = default)
		{
			return await Task.Run(() =>
			{
				if (this.Data == null || this.Data.Length == 0 || this.SampleRate <= 0 || this.Channels <= 0)
				{
					return new Bitmap(Math.Max(width, 1), Math.Max(height, 1));
				}

				width = Math.Max(width, 1);
				height = Math.Max(height, 1);
				samplesPerPixel = Math.Max(samplesPerPixel, 1);
				caretWidth = Math.Clamp(caretWidth, 0, 64);
				selectionAlpha = Math.Clamp(selectionAlpha, 0f, 1f);

				var bg = backColor ?? Color.White;
				var fg = graphColor ?? Color.Black;
				var selColor = selectionColor ?? Color.LightGray;

				long startSamples = offsetSamples ?? this.PlaybackPositionSamples;
				if (startSamples < 0)
				{
					startSamples = 0;
				}

				using var bmp = new Bitmap(width, height);
				using var g = Graphics.FromImage(bmp);
				g.Clear(bg);

				int channelHeight = separateChannels
					? height / this.Channels
					: height;

				for (int ch = 0; ch < this.Channels; ch++)
				{
					int yOffset = separateChannels ? ch * channelHeight : 0;
					this.DrawChannelWave(g, ch, channelHeight, yOffset, width, samplesPerPixel, startSamples, fg, ct);
				}

				if (timeMarkerIntervalSeconds > 0.0)
				{
					this.DrawTimeMarkers(g, width, height, startSamples, samplesPerPixel, timeMarkerIntervalSeconds, fg, ct);
				}

				// Selection overlay (opaque rectangle)
				long s0 = Math.Min(this.SelectionStart, this.SelectionEnd);
				long s1 = Math.Max(this.SelectionStart, this.SelectionEnd);
				if (s1 > s0)
				{
					// compute pixel range for selection within current viewport
					long visibleStart = startSamples;
					long visibleEnd = startSamples + (long) width * samplesPerPixel * this.Channels;
					long selStartClamped = Math.Clamp(s0, visibleStart, visibleEnd);
					long selEndClamped = Math.Clamp(s1, visibleStart, visibleEnd);

					if (selEndClamped > selStartClamped)
					{
						double relStartSamples = selStartClamped - visibleStart;
						double relEndSamples = selEndClamped - visibleStart;
						// samples per pixel per channel aggregated with interleaving
						double samplesPerPixelInterleaved = (double) samplesPerPixel * this.Channels;
						int x0 = (int) Math.Floor(relStartSamples / samplesPerPixelInterleaved);
						int x1 = (int) Math.Ceiling(relEndSamples / samplesPerPixelInterleaved);
						x0 = Math.Clamp(x0, 0, width);
						x1 = Math.Clamp(x1, 0, width);
						int rectWidth = Math.Max(0, x1 - x0);
						if (rectWidth > 0)
						{
							using var brush = new SolidBrush(Color.FromArgb((int) Math.Round(selectionAlpha * 255), selColor));
							g.FillRectangle(brush, x0, 0, rectWidth, height);
						}
					}
				}

				if (caretWidth > 0)
				{
					this.DrawCaret(g, width, height, caretPosition, caretWidth, Color.Red);
				}

				return (Bitmap) bmp.Clone();
			}, ct).ConfigureAwait(false);
		}

		[SupportedOSPlatform("windows")]
		private void DrawChannelWave(
			Graphics g,
			int channel,
			int channelHeight,
			int yOffset,
			int width,
			int samplesPerPixel,
			long startSamples,
			Color color,
			CancellationToken ct)
		{
			using var pen = new Pen(color);

			int midY = yOffset + channelHeight / 2;
			int maxAmpPixels = channelHeight / 2 - 1;

			// jedes Pixel repräsentiert samplesPerPixel Samples pro Kanal
			for (int x = 0; x < width; x++)
			{
				ct.ThrowIfCancellationRequested();

				long sampleStart = startSamples + (long) x * samplesPerPixel * this.Channels + channel;
				long sampleEnd = sampleStart + (long) samplesPerPixel * this.Channels;

				if (sampleStart >= this.Data.LongLength)
				{
					break;
				}

				float min = 0f;
				float max = 0f;
				bool hasSample = false;

				for (long s = sampleStart; s < sampleEnd && s < this.Data.LongLength; s += this.Channels)
				{
					float v = this.Data[s];
					if (!hasSample)
					{
						min = max = v;
						hasSample = true;
					}
					else
					{
						if (v < min)
						{
							min = v;
						}

						if (v > max)
						{
							max = v;
						}
					}
				}

				if (!hasSample)
				{
					continue;
				}

				int y1 = midY - (int) (max * maxAmpPixels);
				int y2 = midY - (int) (min * maxAmpPixels);

				g.DrawLine(pen, x, y1, x, y2);
			}
		}

		[SupportedOSPlatform("windows")]
		public void DrawCaret(Graphics g, int width, int height, float caretPosition, int caretWidth, Color caretColor)
		{
			caretPosition = Math.Clamp(caretPosition, 0f, 1f);
			caretWidth = Math.Clamp(caretWidth, 0, 64);
			if (caretWidth <= 0)
			{
				return;
			}

			int x = (int) (width * caretPosition);
			using var pen = new Pen(caretColor, caretWidth);
			g.DrawLine(pen, x, 0, x, height);
		}

		[SupportedOSPlatform("windows")]
		private void DrawTimeMarkers(
			Graphics g,
			int width,
			int height,
			long startSamples,
			int samplesPerPixel,
			double intervalSeconds,
			Color color,
			CancellationToken ct)
		{
			if (intervalSeconds <= 0 || this.SampleRate <= 0)
			{
				return;
			}

			using var pen = new Pen(color, 1);
			using var font = new Font("Segoe UI", 8);

			double startTimeSec = startSamples / (double) (this.SampleRate * this.Channels);
			double endTimeSec = (startSamples + (long) width * samplesPerPixel * this.Channels) /
								(double) (this.SampleRate * this.Channels);

			double firstMarker = Math.Ceiling(startTimeSec / intervalSeconds) * intervalSeconds;

			for (double t = firstMarker; t < endTimeSec; t += intervalSeconds)
			{
				ct.ThrowIfCancellationRequested();

				double relSec = t - startTimeSec;
				double relSamples = relSec * this.SampleRate * this.Channels;
				double pixels = relSamples / (samplesPerPixel * this.Channels);

				int x = (int) Math.Round(pixels);
				if (x < 0 || x >= width)
				{
					continue;
				}

				g.DrawLine(pen, x, 0, x, height);

				string label = TimeSpan.FromSeconds(t).ToString(@"mm\:ss");
				var size = g.MeasureString(label, font);
				g.DrawString(label, font, Brushes.Gray, x + 2, height - size.Height - 2);
			}
		}
	}
}
