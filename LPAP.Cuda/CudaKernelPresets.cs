using System;
using System.Collections.Generic;

namespace LPAP.Cuda
{
    public static class CudaKernelPresets
    {
        public enum Timestretch07Preset
        {
            MusicBalanced,           // 4096 / 0.75 – ausgewogen
            TransientTight,          // 2048 / 0.75 – bessere Transienten
            SustainRich,             // 4096 / 0.5  – weniger „gedämpft“
            PercussionFocus,         // 2048 / 0.85 – harte Transienten
            HighStretchStable,       // 4096 / 0.75 – >2x stabiler
            LowStretchLight,         // 2048 / 0.5  – <0.7x, weniger Schmieren
            VocalSmooth              // 4096 / 0.75 – glatter, weniger Rattern
        }

        public static CudaKernelPreset Timestretch07(Timestretch07Preset preset, double? stretchFactor = null)
        {
            var p = new CudaKernelPreset
            {
                Name = $"timestretch07::{preset}",
                KernelName = "timestretch07",
                Arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };

            switch (preset)
            {
                case Timestretch07Preset.MusicBalanced:
                    FillArgs(p.Arguments, 4096, 0.70f, 1e-12f, 8.0f, 3, 2, 0.40f, 0.65f, 0.18f, 0.75f, 1e-4f, 1.1f, 0.10f, stretchFactor);
                    break;
                case Timestretch07Preset.TransientTight:
                    FillArgs(p.Arguments, 2048, 0.70f, 1e-12f, 8.0f, 3, 1, 0.30f, 0.80f, 0.20f, 0.85f, 3e-4f, 1.15f, 0.15f, stretchFactor);
                    break;
                case Timestretch07Preset.SustainRich:
                    FillArgs(p.Arguments, 4096, 0.50f, 1e-12f, 8.0f, 2, 1, 0.45f, 0.50f, 0.15f, 0.70f, 1e-4f, 1.20f, 0.08f, stretchFactor);
                    break;
                case Timestretch07Preset.PercussionFocus:
                    FillArgs(p.Arguments, 2048, 0.75f, 1e-12f, 8.0f, 4, 1, 0.30f, 0.85f, 0.22f, 0.85f, 3e-4f, 1.10f, 0.18f, stretchFactor);
                    break;
                case Timestretch07Preset.HighStretchStable:
                    FillArgs(p.Arguments, 4096, 0.65f, 1e-12f, 6.0f, 3, 2, 0.35f, 0.70f, 0.20f, 0.80f, 2e-4f, 1.10f, 0.15f, stretchFactor);
                    break;
                case Timestretch07Preset.LowStretchLight:
                    FillArgs(p.Arguments, 2048, 0.50f, 1e-12f, 8.0f, 2, 1, 0.45f, 0.55f, 0.15f, 0.70f, 1e-4f, 1.20f, 0.10f, stretchFactor);
                    break;
                case Timestretch07Preset.VocalSmooth:
                    FillArgs(p.Arguments, 4096, 0.65f, 1e-12f, 8.0f, 2, 2, 0.40f, 0.60f, 0.16f, 0.80f, 2e-4f, 1.15f, 0.12f, stretchFactor);
                    break;
            }

            return p;
        }

        public enum Timestretch08Preset
        {
            MusicHQ,            // 4096 / 0.75 – High quality general
            TransientGuard,     // 2048 / 0.75 – tighter transients
            SustainAir,         // 4096 / 0.50 – more openness
            PercStrong,         // 2048 / 0.85 – percussion focus
            Stretch2xStable,    // 4096 / 0.75 – >2x stability
            LowStretchClear,    // 2048 / 0.60 – <0.7x clarity
            VocalSilky          // 4096 / 0.70 – smooth vocals
        }

        public static CudaKernelPreset Timestretch08(Timestretch08Preset preset, double? stretchFactor = null)
        {
            var p = new CudaKernelPreset
            {
                Name = $"timestretch08::{preset}",
                KernelName = "timestretch08",
                Arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };

            switch (preset)
            {
                case Timestretch08Preset.MusicHQ:
                    Fill08(p.Arguments, 4096, 0.70f, 1e-12f, 8.0f, 3, 0.12f, 0.40f, 0.80f, 2, 1e-4f, 1.12f, stretchFactor);
                    break;
                case Timestretch08Preset.TransientGuard:
                    Fill08(p.Arguments, 2048, 0.70f, 1e-12f, 8.0f, 4, 0.15f, 0.35f, 0.85f, 1, 3e-4f, 1.10f, stretchFactor);
                    break;
                case Timestretch08Preset.SustainAir:
                    Fill08(p.Arguments, 4096, 0.50f, 1e-12f, 8.0f, 3, 0.10f, 0.45f, 0.75f, 1, 1e-4f, 1.18f, stretchFactor);
                    break;
                case Timestretch08Preset.PercStrong:
                    Fill08(p.Arguments, 2048, 0.75f, 1e-12f, 8.0f, 5, 0.18f, 0.30f, 0.88f, 1, 3e-4f, 1.08f, stretchFactor);
                    break;
                case Timestretch08Preset.Stretch2xStable:
                    Fill08(p.Arguments, 4096, 0.70f, 1e-12f, 6.0f, 3, 0.16f, 0.42f, 0.82f, 2, 2e-4f, 1.10f, stretchFactor);
                    break;
                case Timestretch08Preset.LowStretchClear:
                    Fill08(p.Arguments, 2048, 0.60f, 1e-12f, 8.0f, 2, 0.12f, 0.50f, 0.70f, 1, 1e-4f, 1.20f, stretchFactor);
                    break;
                case Timestretch08Preset.VocalSilky:
                    Fill08(p.Arguments, 4096, 0.70f, 1e-12f, 8.0f, 3, 0.12f, 0.38f, 0.80f, 2, 2e-4f, 1.14f, stretchFactor);
                    break;
            }

            return p;
        }

        private static void Fill08(
            Dictionary<string, object> dict,
            int chunkSize, float overlap,
            float EPS_MAG, float MAX_FACTOR,
            int REASSIGN_RADIUS, float PHASE_JITTER_DAMP,
            float ATTACK_ALPHA, float RELEASE_ALPHA,
            int COHERENCE_RADIUS, float ENERGY_FLOOR_REL, float ENERGY_CLAMP_RATIO,
            double? stretchFactor)
        {
            // STFT params
            dict["chunkSize"] = chunkSize;
            dict["overlap"] = overlap;

            // Kernel args (must match timestretch08 signature names)
            dict["EPS_MAG_arg"] = EPS_MAG;
            dict["MAX_FACTOR_arg"] = MAX_FACTOR;
            dict["REASSIGN_RADIUS_arg"] = REASSIGN_RADIUS;
            dict["PHASE_JITTER_DAMP_arg"] = PHASE_JITTER_DAMP;
            dict["ATTACK_ALPHA_arg"] = ATTACK_ALPHA;
            dict["RELEASE_ALPHA_arg"] = RELEASE_ALPHA;
            dict["COHERENCE_RADIUS_arg"] = COHERENCE_RADIUS;
            dict["ENERGY_FLOOR_REL_arg"] = ENERGY_FLOOR_REL;
            dict["ENERGY_CLAMP_RATIO_arg"] = ENERGY_CLAMP_RATIO;

            if (stretchFactor.HasValue)
            {
                dict["factor"] = stretchFactor.Value;
            }
        }

        public enum TimestretchSbsms01Preset
        {
            MusicHQ,            // 4096 / 0.75 – ausgewogen, hohe Qualität
            TransientTight,     // 2048 / 0.75 – enge Transienten
            SustainAir,         // 4096 / 0.50 – mehr Offenheit
            PercFocus,          // 2048 / 0.85 – Percussion-fokussiert
            Stretch2xStable,    // 4096 / 0.75 – stabil bei >2x
            LowStretchClear,    // 2048 / 0.60 – <0.7x klarer
            VocalSilky          // 4096 / 0.70 – glatte Vocals
        }

        public static CudaKernelPreset TimestretchSbsms01(TimestretchSbsms01Preset preset, double? stretchFactor = null)
        {
            var p = new CudaKernelPreset
            {
                Name = $"timestretch_sbsms01::{preset}",
                KernelName = "timestretch_sbsms01",
                Arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };

            switch (preset)
            {
                case TimestretchSbsms01Preset.MusicHQ:
                    FillSbsms01(p.Arguments, 4096, 0.70f,
                        EPS_MAG: 1e-12f, MAX_FACTOR: 8.0f,
                        PEAK_RADIUS: 3, IF_RADIUS: 3,
                        PHASE_DAMP: 0.14f,
                        COHERENCE_RADIUS: 2, COHERENCE_BLEND: 0.18f,
                        ATTACK_ALPHA: 0.35f, RELEASE_ALPHA: 0.80f,
                        FLOOR_REL: 1e-4f, ENERGY_CLAMP_RATIO: 1.12f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.TransientTight:
                    FillSbsms01(p.Arguments, 2048, 0.70f,
                        1e-12f, 8.0f,
                        PEAK_RADIUS: 4, IF_RADIUS: 4,
                        PHASE_DAMP: 0.16f,
                        COHERENCE_RADIUS: 1, COHERENCE_BLEND: 0.15f,
                        ATTACK_ALPHA: 0.30f, RELEASE_ALPHA: 0.85f,
                        FLOOR_REL: 3e-4f, ENERGY_CLAMP_RATIO: 1.10f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.SustainAir:
                    FillSbsms01(p.Arguments, 4096, 0.50f,
                        1e-12f, 8.0f,
                        PEAK_RADIUS: 3, IF_RADIUS: 3,
                        PHASE_DAMP: 0.12f,
                        COHERENCE_RADIUS: 1, COHERENCE_BLEND: 0.15f,
                        ATTACK_ALPHA: 0.40f, RELEASE_ALPHA: 0.75f,
                        FLOOR_REL: 1e-4f, ENERGY_CLAMP_RATIO: 1.18f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.PercFocus:
                    FillSbsms01(p.Arguments, 2048, 0.75f,
                        1e-12f, 8.0f,
                        PEAK_RADIUS: 5, IF_RADIUS: 5,
                        PHASE_DAMP: 0.18f,
                        COHERENCE_RADIUS: 1, COHERENCE_BLEND: 0.12f,
                        ATTACK_ALPHA: 0.28f, RELEASE_ALPHA: 0.88f,
                        FLOOR_REL: 3e-4f, ENERGY_CLAMP_RATIO: 1.08f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.Stretch2xStable:
                    FillSbsms01(p.Arguments, 4096, 0.70f,
                        1e-12f, 6.0f,
                        PEAK_RADIUS: 3, IF_RADIUS: 3,
                        PHASE_DAMP: 0.16f,
                        COHERENCE_RADIUS: 2, COHERENCE_BLEND: 0.20f,
                        ATTACK_ALPHA: 0.38f, RELEASE_ALPHA: 0.82f,
                        FLOOR_REL: 2e-4f, ENERGY_CLAMP_RATIO: 1.10f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.LowStretchClear:
                    FillSbsms01(p.Arguments, 2048, 0.60f,
                        1e-12f, 8.0f,
                        PEAK_RADIUS: 2, IF_RADIUS: 2,
                        PHASE_DAMP: 0.12f,
                        COHERENCE_RADIUS: 1, COHERENCE_BLEND: 0.18f,
                        ATTACK_ALPHA: 0.45f, RELEASE_ALPHA: 0.70f,
                        FLOOR_REL: 1e-4f, ENERGY_CLAMP_RATIO: 1.20f,
                        stretchFactor);
                    break;

                case TimestretchSbsms01Preset.VocalSilky:
                    FillSbsms01(p.Arguments, 4096, 0.70f,
                        1e-12f, 8.0f,
                        PEAK_RADIUS: 3, IF_RADIUS: 3,
                        PHASE_DAMP: 0.13f,
                        COHERENCE_RADIUS: 2, COHERENCE_BLEND: 0.17f,
                        ATTACK_ALPHA: 0.36f, RELEASE_ALPHA: 0.80f,
                        FLOOR_REL: 2e-4f, ENERGY_CLAMP_RATIO: 1.14f,
                        stretchFactor);
                    break;
            }

            return p;
        }

        public static IEnumerable<CudaKernelPreset> GetAllPresetsSbsms01()
        {
            foreach (TimestretchSbsms01Preset e in Enum.GetValues(typeof(TimestretchSbsms01Preset)))
            {
                yield return TimestretchSbsms01(e);
            }
        }

        private static void FillSbsms01(
            Dictionary<string, object> dict,
            int chunkSize, float overlap,
            float EPS_MAG, float MAX_FACTOR,
            int PEAK_RADIUS, int IF_RADIUS,
            float PHASE_DAMP,
            int COHERENCE_RADIUS, float COHERENCE_BLEND,
            float ATTACK_ALPHA, float RELEASE_ALPHA,
            float FLOOR_REL, float ENERGY_CLAMP_RATIO,
            double? stretchFactor)
        {
            // STFT params
            dict["chunkSize"] = chunkSize;
            dict["overlap"] = overlap;

            // Kernel args (exact names from timestretch_sbsms01.cu signature)
            dict["EPS_MAG_arg"] = EPS_MAG;
            dict["MAX_FACTOR_arg"] = MAX_FACTOR;
            dict["PEAK_RADIUS_arg"] = PEAK_RADIUS;
            dict["IF_RADIUS_arg"] = IF_RADIUS;
            dict["PHASE_DAMP_arg"] = PHASE_DAMP;
            dict["COHERENCE_RADIUS_arg"] = COHERENCE_RADIUS;
            dict["COHERENCE_BLEND_arg"] = COHERENCE_BLEND;
            dict["ATTACK_ALPHA_arg"] = ATTACK_ALPHA;
            dict["RELEASE_ALPHA_arg"] = RELEASE_ALPHA;
            dict["FLOOR_REL_arg"] = FLOOR_REL;
            dict["ENERGY_CLAMP_ratio_arg"] = ENERGY_CLAMP_RATIO;

            if (stretchFactor.HasValue)
            {
                dict["factor"] = stretchFactor.Value;
            }
        }


        public static Dictionary<string, object> ApplyTimestretch07Preset(
            Dictionary<string, object>? current,
            Timestretch07Preset preset,
            double? stretchFactor = null)
        {
            var presetDef = Timestretch07(preset, stretchFactor);
            var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (current != null)
            {
                foreach (var kv in current)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in presetDef.Arguments)
            {
                merged[kv.Key] = kv.Value;
            }

            return merged;
        }

        // Alle bekannten Presets als Objekte (aktuell nur v07)
        public static IEnumerable<CudaKernelPreset> GetAllPresets()
        {
            foreach (Timestretch07Preset e in Enum.GetValues(typeof(Timestretch07Preset)))
            {
                yield return Timestretch07(e);
            }
            foreach (Timestretch08Preset e in Enum.GetValues(typeof(Timestretch08Preset)))
            {
                yield return Timestretch08(e);
            }
            foreach (TimestretchSbsms01Preset e in Enum.GetValues(typeof(TimestretchSbsms01Preset)))
            {
                yield return TimestretchSbsms01(e);
            }
        }

        private static void FillArgs(
            Dictionary<string, object> dict,
            int chunkSize, float overlap,
            float EPS_MAG, float MAX_FACTOR,
            int PEAK_LOCK_RADIUS, int COHERENCE_RADIUS,
            float TRANSIENT_DELTA_PI, float TRANSIENT_BLEND,
            float BACKFEED_MIX, float ENVELOPE_ALPHA,
            float FLOOR_REL, float ENERGY_CLAMP_RATIO, float PHASE_DAMP,
            double? stretchFactor
        )
        {
            dict["chunkSize"] = chunkSize;
            dict["overlap"] = overlap;

            dict["EPS_MAG_arg"] = EPS_MAG;
            dict["MAX_FACTOR_arg"] = MAX_FACTOR;
            dict["PEAK_LOCK_RADIUS_arg"] = PEAK_LOCK_RADIUS;
            dict["COHERENCE_RADIUS_arg"] = COHERENCE_RADIUS;
            dict["TRANSIENT_DELTA_PI_arg"] = TRANSIENT_DELTA_PI;
            dict["TRANSIENT_BLEND_arg"] = TRANSIENT_BLEND;
            dict["BACKFEED_MIX_arg"] = BACKFEED_MIX;
            dict["ENVELOPE_ALPHA_arg"] = ENVELOPE_ALPHA;
            dict["FLOOR_REL_arg"] = FLOOR_REL;
            dict["ENERGY_CLAMP_RATIO_arg"] = ENERGY_CLAMP_RATIO;
            dict["PHASE_DAMP_arg"] = PHASE_DAMP;

            if (stretchFactor.HasValue)
            {
                dict["factor"] = stretchFactor.Value;
            }
        }

        public class CudaKernelPreset
        {
            public string Name { get; set; } = "N/A";
            public string KernelName { get; set; } = "N/A";
            public Dictionary<string, object> Arguments { get; set; } = [];

            public bool IsSuitableForKernel(string? kernelName) =>
                !string.IsNullOrWhiteSpace(kernelName) &&
                string.Equals(this.KernelName, kernelName, StringComparison.OrdinalIgnoreCase);
        }
    }
}