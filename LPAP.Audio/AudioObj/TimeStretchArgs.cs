using System;
using System.Collections.Generic;
using System.Text;

namespace LPAP.Audio
{
    public class TimeStretchArgs
    {
        public string Algorithm { get; set; } = "N/A";
        public double StretchFactor { get; set; } = 1.0;

        public int Workers { get; set; } = 0;
        public double ElapsedSeconds { get; set; } = 0.0;

        private Dictionary<string, decimal> AdditionalParams = [];
        public decimal? this[string argument]
        {
            get => this.AdditionalParams.TryGetValue(argument, out var value) ? value : null;
            set
            {
                // Try to add or update find by IgnoreCase
                var param = this.AdditionalParams.Keys.FirstOrDefault(k => k.Equals(argument, StringComparison.OrdinalIgnoreCase));
                if (param != null)
                {
                    if (value.HasValue)
                    {
                        this.AdditionalParams[param] = value.Value;
                    }
                    else
                    {
                        this.AdditionalParams.Remove(param);
                    }
                }
                else
                {
                    if (value.HasValue)
                    {
                        this.AdditionalParams.Add(argument, value.Value);
                    }
                }
            }
        }




        public TimeStretchArgs(string algorithm, double stretchFactor, int workers = 1, double elapsedSeconds = 0.0, Dictionary<string, decimal>? additionalParams = null)
        {
            this.Algorithm = algorithm;
            this.StretchFactor = stretchFactor;
            this.Workers = workers;
            this.ElapsedSeconds = elapsedSeconds;
            if (additionalParams != null)
            {
                this.AdditionalParams = additionalParams;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Algorithm: {this.Algorithm}" + Environment.NewLine + $"StretchFactor: {this.StretchFactor:F9}");
            sb.Append(Environment.NewLine);
            sb.Append($", Workers: {this.Workers}, Time: {this.ElapsedSeconds:F3}");
            sb.Append(Environment.NewLine);
            foreach (var kvp in this.AdditionalParams)
            {
                sb.Append($"{kvp.Key}: {kvp.Value}");
                sb.Append(Environment.NewLine);
            }
            return sb.ToString();
        }





    }
}
