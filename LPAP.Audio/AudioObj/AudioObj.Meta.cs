using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LPAP.Audio
{
	public partial class AudioObj
	{
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
