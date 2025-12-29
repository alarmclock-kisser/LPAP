#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

__device__ __forceinline__ float safe_atan2f(float y, float x)
{
    if (!isfinite(x) || !isfinite(y)) return 0.0f;
    if (fabsf(x) < 1e-12f && fabsf(y) < 1e-12f) return 0.0f; // EPS_MAG default
    return atan2f(y, x);
}

__device__ __forceinline__ float hypotf_safe(float x, float y)
{
    float ax = fabsf(x), ay = fabsf(y);
    float m = fmaxf(ax, ay);
    if (m < 1e-12f) return 0.0f; // EPS_MAG default
    x /= m; y /= m;
    return m * sqrtf(x * x + y * y);
}

// Peak locking mit lokaler Nachbarschaft
__device__ float peak_lock_phase(const float2* __restrict__ input, int bin, int N, int peakLockRadius, float fallbackPhase)
{
    int left = max(0, bin - peakLockRadius);
    int right = min(N - 1, bin + peakLockRadius);
    int peakBin = bin;
    float peakMag = -1.0f;

    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float mag = hypotf_safe(v.x, v.y);
        if (mag > peakMag) { peakMag = mag; peakBin = b; }
    }
    if (peakBin == bin || peakMag <= 1e-12f) return fallbackPhase; // EPS_MAG default

    float2 p = input[peakBin];
    return safe_atan2f(p.y, p.x);
}

// Cross-band coherence: sanft zur lokalen Phasenmittel schließen
__device__ float enforce_coherence(const float2* __restrict__ input, int bin, int N, int coherenceRadius, float phase)
{
    int left = max(0, bin - coherenceRadius);
    int right = min(N - 1, bin + coherenceRadius);

    float wsum = 0.0f;
    float vx = 0.0f, vy = 0.0f;
    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float mag = hypotf_safe(v.x, v.y);
        float ph = safe_atan2f(v.y, v.x);
        vx += mag * cosf(ph);
        vy += mag * sinf(ph);
        wsum += mag;
    }
    if (wsum <= 1e-12f) return phase; // EPS_MAG default

    float avgPh = safe_atan2f(vy, vx);
    float blend = 0.15f; // leichter Mix
    float d = wrap_pi(avgPh - phase);
    return wrap_pi(phase + blend * d);
}

// Envelope EMA mit Energie-Deckel
__device__ float envelope_ema(float prevEma, float curMag, float envelopeAlpha, float energyClampRatio)
{
    float ema = envelopeAlpha * prevEma + (1.0f - envelopeAlpha) * curMag;
    float maxAllowed = energyClampRatio * fmaxf(prevEma, 1e-12f); // EPS_MAG default
    if (ema > maxAllowed) ema = maxAllowed;
    return ema;
}

// Kernel mit Tunables als Argumente. Wenn ein Tunable == 0 ist, wird der Default angesetzt.
extern "C" __global__ void timestretch07(
    const float2* __restrict__ input,       // komplexes Spektrum (N = chunkSize)
    float2* __restrict__ output,            // komplexes Spektrum out (N = chunkSize)
    float2* __restrict__ state,             // pro Bin: x=prevAnalysisPhase, y=synthPhaseAcc
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor,
    // Tunables (0 => Default wird verwendet):
    const float EPS_MAG_arg,               // Default: 1e-12f
    const float MAX_FACTOR_arg,            // Default: 8.0f
    const int   PEAK_LOCK_RADIUS_arg,      // Default: 3
    const int   COHERENCE_RADIUS_arg,      // Default: 2
    const float TRANSIENT_DELTA_PI_arg,    // Default: 0.40f (als Anteil von pi)
    const float TRANSIENT_BLEND_arg,       // Default: 0.65f
    const float BACKFEED_MIX_arg,          // Default: 0.18f
    const float ENVELOPE_ALPHA_arg,        // Default: 0.75f
    const float FLOOR_REL_arg,             // Default: 1e-4f
    const float ENERGY_CLAMP_RATIO_arg,    // Default: 1.1f
    const float PHASE_DAMP_arg             // Default: 0.10f
)
{
    // Defaults anwenden, falls Args == 0
    const float EPS_MAG            = (EPS_MAG_arg            == 0.0f) ? 1e-12f : EPS_MAG_arg;
    const float MAX_FACTOR         = (MAX_FACTOR_arg         == 0.0f) ? 8.0f    : MAX_FACTOR_arg;
    const int   PEAK_LOCK_RADIUS   = (PEAK_LOCK_RADIUS_arg   == 0   ) ? 2       : PEAK_LOCK_RADIUS_arg;
    const int   COHERENCE_RADIUS   = (COHERENCE_RADIUS_arg   == 0   ) ? 1       : COHERENCE_RADIUS_arg;
    const float TRANSIENT_DELTA_PI = (TRANSIENT_DELTA_PI_arg == 0.0f) ? 0.40f   : TRANSIENT_DELTA_PI_arg;
    const float TRANSIENT_BLEND    = (TRANSIENT_BLEND_arg    == 0.0f) ? 0.8f   : TRANSIENT_BLEND_arg;
    const float BACKFEED_MIX       = (BACKFEED_MIX_arg       == 0.0f) ? 0.13f   : BACKFEED_MIX_arg;
    const float ENVELOPE_ALPHA     = (ENVELOPE_ALPHA_arg     == 0.0f) ? 0.85f   : ENVELOPE_ALPHA_arg;
    const float FLOOR_REL          = (FLOOR_REL_arg          == 0.0f) ? 1e-4f   : FLOOR_REL_arg;
    const float ENERGY_CLAMP_RATIO = (ENERGY_CLAMP_RATIO_arg == 0.0f) ? 1.1f    : ENERGY_CLAMP_RATIO_arg;
    const float PHASE_DAMP         = (PHASE_DAMP_arg         == 0.0f) ? 0.2f   : PHASE_DAMP_arg;

    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Factor clamp
    float stretch = (float)factor;
    if (!isfinite(stretch)) stretch = 1.0f;
    if (stretch < 0.125f) stretch = 0.125f;
    if (stretch > MAX_FACTOR) stretch = MAX_FACTOR;

    // Overlap -> hop
    float ov = overlap;
    ov = (ov < 0.0f) ? 0.0f : ((ov >= 1.0f) ? 0.9999f : ov);

    int overlapSize = (int)(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;

    int hopIn = chunkSize - overlapSize;
    if (hopIn <= 0) hopIn = 1;

    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // Input
    float2 cur = input[bin];
    float inMag = hypotf_safe(cur.x, cur.y);
    float inPhase = safe_atan2f(cur.y, cur.x);

    // State
    float2 st = state[bin];
    float prevPhase = st.x;
    float phaseAcc  = st.y;

    if (!isfinite(prevPhase) || !isfinite(phaseAcc) || (prevPhase == 0.0f && phaseAcc == 0.0f)) {
        prevPhase = inPhase;
        phaseAcc  = inPhase;
    }

    // Delta
    float delta = wrap_pi(inPhase - prevPhase - expected);

    // Transient
    float transientStrength = fminf(1.0f, fabsf(delta) / (TRANSIENT_DELTA_PI * (float)M_PI));

    // Peak lock
    float lockedPhase = peak_lock_phase(input, bin, chunkSize, PEAK_LOCK_RADIUS, inPhase);

    // Blend zur gelockten Phase
    float useAnalysisPhase = wrap_pi((1.0f - transientStrength * TRANSIENT_BLEND) * inPhase
                                   + (transientStrength * TRANSIENT_BLEND) * lockedPhase);

    // True increment mit Dämpfung
    float trueInc = expected + delta;
    trueInc = (1.0f - PHASE_DAMP) * trueInc + PHASE_DAMP * expected;

    // Syntheseinkrement
    float outInc = trueInc * stretch;

    // Akkumulator
    phaseAcc = phaseAcc + outInc;

    // Coherence
    float cohPhase = enforce_coherence(input, bin, chunkSize, COHERENCE_RADIUS, phaseAcc);

    // Envelope EMA mit Floor
    float prevEma = inMag; // ohne externen Buffer nutzen wir lokalen Fallback
    if (!isfinite(prevEma)) prevEma = inMag;
    float smoothedMag = envelope_ema(prevEma, inMag, ENVELOPE_ALPHA, ENERGY_CLAMP_RATIO);
    float floorMag = FLOOR_REL * fmaxf(inMag, prevEma);
    smoothedMag = fmaxf(smoothedMag, floorMag);

    // Backfeed (ohne externen Buffer): konservativer Mix (wir nutzen smoothedMag als Approx.)
    float outMag = fmaxf(EPS_MAG, (1.0f - BACKFEED_MIX) * smoothedMag + BACKFEED_MIX * smoothedMag);

    // Output
    float s = sinf(cohPhase);
    float c = cosf(cohPhase);
    output[bin].x = outMag * c;
    output[bin].y = outMag * s;

    // Persist state
    state[bin].x = useAnalysisPhase;
    state[bin].y = cohPhase;
}