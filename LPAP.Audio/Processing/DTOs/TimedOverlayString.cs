using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LPAP.Audio.Processing.DTOs
{
    public class TimedOverlayString
    {
        public string Text { get; set; } = string.Empty;

        public double ShowTimeStartSeconds { get; set; } = 0.0;
        public double? ShowTimeEndSeconds { get; set; } = null; // Infinitely if null

        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 12f; // In points
        public string FontStyle { get; set; } = "Regular"; // Regular, Bold, Italic, etc.
        public bool Shadow { get; set; } = true;

        public string Color { get; set; } = "#FFFFFF"; // Hex color code, default is white

        public int? XOffset { get; set; } = null; // Centered
        public int? YOffset { get; set; } = null; // Centered
        public float Rotation { get; set; } = 0f; // In degrees, 0° is normal, clockwise rotation




        [JsonConstructor]
        public TimedOverlayString()
        {

        }

        public TimedOverlayString(string subtitlesLine, string fontFamily = "Arial", float fontSize = 12f, string fontStyle = "Regular", bool shadow = true, string color = "#FFFFFF", int? xOffset = null, int? yOffset = null, float rotation = 0f)
        {
            this.FontFamily = fontFamily;
            this.FontSize = fontSize;
            this.FontStyle = fontStyle;
            this.Shadow = shadow;
            this.Color = color.Replace("#", "") + "#";
            this.XOffset = xOffset;
            this.YOffset = yOffset;
            this.Rotation = 360.0f % rotation;

            this.ParseSubtitlesLine(subtitlesLine);
        }


        private void ParseSubtitlesLine(string subtitlesLine)
        {
            if (string.IsNullOrWhiteSpace(subtitlesLine))
                throw new FormatException("Subtitle line is empty.");

            // REGEX ERKLÄRUNG:
            // (\d{1,2}:\d{2}:\d{2}[.,]\d{3}) -> Gruppe 1: Startzeit (H:M:S.mmm oder M:S.mmm)
            // \s*[-–—|→|-->]+\s*            -> Trenner: Optional Leerzeichen, dann ein Trennerzeichen
            // (\d{1,2}:\d{2}:\d{2}[.,]\d{3}) -> Gruppe 2: Endzeit
            // \s*                           -> Optional Leerzeichen
            // (.*)                          -> Gruppe 3: Der restliche Text
            var pattern = @"(\d{1,2}:\d{2}:\d{2}[.,]\d{3})\s*[-–—|→>]+\s*(\d{1,2}:\d{2}:\d{2}[.,]\d{3})\s*(.*)";
            var match = Regex.Match(subtitlesLine, pattern);

            if (!match.Success)
            {
                // Fallback: Wenn das volle Muster nicht passt (z.B. nur eine Zeit vorhanden), 
                // versuchen wir, zumindest einen Zeitstempel zu finden.
                var simpleTimePattern = @"(\d{1,2}:\d{2}:\d{2}[.,]\d{3})";
                var simpleMatch = Regex.Match(subtitlesLine, simpleTimePattern);

                if (simpleMatch.Success)
                {
                    // Wir setzen Start und Ende auf das gleiche Zeitfenster, 
                    // falls nur eine Zeit angegeben ist, oder nutzen Standardwerte.
                    this.ShowTimeStartSeconds = this.ParseTimeToSeconds(simpleMatch.Groups[1].Value);
                    this.ShowTimeEndSeconds = this.ShowTimeStartSeconds + 10.0; // Default 10 Sek.
                    this.Text = subtitlesLine.Replace(simpleMatch.Value, "").Trim();
                }
                else
                {
                    throw new FormatException($"Could not parse any valid timestamp in line: {subtitlesLine}");
                }
                return;
            }

            // Extraktion aus dem erfolgreichen Match
            string startTimeStr = match.Groups[1].Value;
            string endTimeStr = match.Groups[2].Value;
            string rawText = match.Groups[3].Value;

            this.ShowTimeStartSeconds = this.ParseTimeToSeconds(startTimeStr);

            // Falls Endzeit vorhanden ist, parsen wir sie (optional, falls dein DTO Endzeit speichert)
            // Wenn nicht, setzen wir ein Default-Fenster.
            this.ShowTimeEndSeconds = this.ParseTimeToSeconds(endTimeStr);

            // Sanitization: Entferne führende Tabulatoren oder doppelte Leerzeichen
            this.Text = Regex.Replace(rawText, @"^\s+|\s+$", "").Trim();
        }

        /// <summary>
        /// Hilfsmethode, um verschiedene Zeitformate (HH:mm:ss.fff, mm:ss.fff, etc.) 
        /// und verschiedene Dezimaltrenner (Punkt/Komma) sicher in Sekunden umzuwandeln.
        /// </summary>
        private double ParseTimeToSeconds(string timeStr)
        {
            // Ersetze Komma durch Punkt für konsistentes Parsing
            string normalized = timeStr.Replace(',', '.');

            // Versuche verschiedene Formate (HH:mm:ss.fff, mm:ss.fff)
            string[] formats = { "hh:mm:ss.fff", "mm:ss.fff", "h:mm:ss.fff", "m:ss.fff" };

            if (TimeSpan.TryParseExact(normalized, formats, System.Globalization.CultureInfo.InvariantCulture,
                (System.Globalization.TimeSpanStyles) System.Globalization.DateTimeStyles.None, out TimeSpan ts))
            {
                return ts.TotalSeconds;
            }

            // Fallback auf normales Parsing falls Exact fehlschlägt (für unregelmäßige Formate)
            if (TimeSpan.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out ts))
            {
                return ts.TotalSeconds;
            }

            throw new FormatException($"Could not parse time string: {timeStr}");
        }



        // tostring override
        public override string ToString()
        {
            return $"{TimeSpan.FromSeconds(this.ShowTimeStartSeconds).ToString(@"hh\:mm\:ss")} - {TimeSpan.FromSeconds(this.ShowTimeEndSeconds ?? this.ShowTimeStartSeconds + 10).ToString(@"hh\:mm\:ss")}: '{this.Text}'";
        }



    }
}
