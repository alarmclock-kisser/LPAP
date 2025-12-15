using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
		public Dictionary<string, double> Metrics { get; private set; } = new Dictionary<string, double>
		{
			{ "Import", 0.0 },{ "Export", 0.0 },{ "Chunk", 0.0 },{ "Aggregate", 0.0 },
			{ "Normalize", 0.0 },{ "Level", 0.0 },{ "Push", 0.0 },{ "Pull", 0.0 },
			{ "FFT", 0.0 },{ "IFFT", 0.0 },{ "Stretch", 0.0 },
			{ "BeatScan", 0.0 },{ "TimingScan", 0.0 }
		};

		public double this[string metric]
		{
			get
			{
				// Find by tolower case
				if (this.Metrics.TryGetValue(metric, out double value))
				{
					return value;
				}
				else
				{
					var key = this.Metrics.Keys.FirstOrDefault(k => k.Equals(metric, StringComparison.OrdinalIgnoreCase));
					if (key != null)
					{
						return this.Metrics[key];
					}
					else
					{
						// If not found, return 0.0
						return 0.0;
					}
				}
			}
			set
			{
				// Find by tolower case
				if (this.Metrics.ContainsKey(metric))
				{
					this.Metrics[metric] = value;
				}
				else
				{
					var key = this.Metrics.Keys.FirstOrDefault(k => k.Equals(metric, StringComparison.OrdinalIgnoreCase));
					if (key != null)
					{
						this.Metrics[key] = value;
					}
					else
					{
						// Capitalize first letter and add to dictionary
						string capitalizedMetric = char.ToUpper(metric[0]) + metric.Substring(1).ToLowerInvariant();
						this.Metrics.Add(capitalizedMetric, value);
					}
				}
			}
		}


		public float ReadBeatsPerMinuteTag(string tag = "TBPM", bool set = true)
		{
			// Read bpm metadata if available
			float bpm = 0.0f;
			float roughBeatsPerMinute = 0.0f;

			try
			{
				if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
				{
					using var file = TagLib.File.Create(this.FilePath);
					if (file.Tag.BeatsPerMinute > 0)
					{
						roughBeatsPerMinute = (float) file.Tag.BeatsPerMinute;
					}
					if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
					{
						var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);

						var tagTextFrame = TagLib.Id3v2.TextInformationFrame.Get(id3v2Tag, tag, false);

						if (tagTextFrame != null && tagTextFrame.Text.Any())
						{
							string bpmString = tagTextFrame.Text.FirstOrDefault() ?? "0,0";
							if (!string.IsNullOrEmpty(bpmString))
							{
								bpmString = bpmString.Replace(',', '.');

								if (float.TryParse(bpmString, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedBeatsPerMinute))
								{
									bpm = parsedBeatsPerMinute;
								}
							}
						}
						else
						{
							bpm = 0.0f;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fehler beim Lesen des Tags {tag.ToUpper()}: {ex.Message} ({ex.InnerException?.Message ?? " - "})");
			}

			// Take rough bpm if <= 0.0f
			if (bpm <= 0.0f && roughBeatsPerMinute > 0.0f)
			{
				Console.WriteLine($"No value found for '{tag.ToUpper()}', taking rough BPM value from legacy tag.");
				bpm = roughBeatsPerMinute;
			}

			if (set)
			{
				this.BeatsPerMinute = bpm;
				if (this.BeatsPerMinute <= 10)
				{
					this.ReadBeatsPerMinuteTagLegacy();
				}
			}

			return bpm;
		}

		public double ReadBeatsPerMinuteTagLegacy()
		{
			// Read bpm metadata if available
			float bpm = 0.0f;

			try
			{
				if (!string.IsNullOrEmpty(this.FilePath) && File.Exists(this.FilePath))
				{
					using var file = TagLib.File.Create(this.FilePath);
					// Check for BPM in standard ID3v2 tag
					if (file.Tag.BeatsPerMinute > 0)
					{
						bpm = (float) file.Tag.BeatsPerMinute;
					}
					// Alternative für spezielle Tags (z.B. TBPM Frame)
					else if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
					{
						var id3v2Tag = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);
						var bpmFrame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2Tag, "BPM", false);

						if (bpmFrame != null && float.TryParse(bpmFrame.Text.FirstOrDefault(), out float parsedBeatsPerMinute))
						{
							bpm = parsedBeatsPerMinute;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Fehler beim Lesen der BPM: {ex.Message}");
			}
			this.BeatsPerMinute = bpm > 0 ? bpm / 100.0f : 0.0f;
			return this.BeatsPerMinute;
		}



	}
}
