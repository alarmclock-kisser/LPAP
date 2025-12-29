// Spectral Reassignment + Adaptive Phase Propagation time-stretch (inspired by high-quality methods).
// I/O: float2 complex spectrum (FFT bins) -> float2 complex spectrum.
// Signature: keeps float2* input/output/state and exposes tunables via args (0 => defaults).

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
    if (fabsf(x) < 1e-12f && fabsf(y) < 1e-12f) return 0.0f;
    return atan2f(y, x);
}
__device__ __forceinline__ float hypotf_safe(float x, float y)
{
    float ax = fabsf(x), ay = fabsf(y);
    float m = fmaxf(ax, ay);
    if (m < 1e-12f) return 0.0f;
    x /= m; y /= m;
    return m * sqrtf(x * x + y * y);
}

// Local instantaneous frequency estimate via phase derivative stabilization.
// Uses neighbor bins to reduce bias (simple reassignment surrogate).
__device__ float estimate_inst_freq(
    const float2* __restrict__ input, int bin, int N, int rad, float expected)
{
    int l = max(0, bin - rad);
    int r = min(N - 1, bin + rad);

    float wsum = 0.0f;
    float acc = 0.0f;
    for (int b = l; b <= r; ++b) {
        float2 v = input[b];
        float m = hypotf_safe(v.x, v.y);
        float ph = safe_atan2f(v.y, v.x);
        float delta = wrap_pi(ph - expected * ((float)b / (float)bin == 1.0f ? 1.0f : 1.0f)); // cheap bias cancel
        acc += m * delta;
        wsum += m;
    }
    if (wsum <= 1e-12f) return expected;
    float meanDelta = acc / wsum;
    return expected + meanDelta;
}

// Magnitude shaping with dual-EMA (attack/release) to reduce pumping and smear.
// attackAlpha < releaseAlpha for fast attack, slower release.
__device__ float dual_ema(float prev, float cur, float attackAlpha, float releaseAlpha)
{
    float alpha = (cur > prev) ? attackAlpha : releaseAlpha;
    return alpha * prev + (1.0f - alpha) * cur;
}

// Main kernel
extern "C" __global__ void timestretch08(
    const float2* __restrict__ input,       // N complex spectrum
    float2* __restrict__ output,            // N complex spectrum out
    float2* __restrict__ state,             // per-bin: x=prevAnalysisPhase, y=synthPhaseAcc
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor,
    // Tunables (0 => default):
    const float EPS_MAG_arg,                // default 1e-12f
    const float MAX_FACTOR_arg,             // default 8.0f
    const int   REASSIGN_RADIUS_arg,        // default 3
    const float PHASE_JITTER_DAMP_arg,      // default 0.12f
    const float ATTACK_ALPHA_arg,           // default 0.40f
    const float RELEASE_ALPHA_arg,          // default 0.80f
    const int   COHERENCE_RADIUS_arg,       // default 2
    const float ENERGY_FLOOR_REL_arg,       // default 1e-4f
    const float ENERGY_CLAMP_RATIO_arg      // default 1.12f
)
{
    // Defaults
    const float EPS_MAG            = (EPS_MAG_arg            == 0.0f) ? 1e-12f : EPS_MAG_arg;
    const float MAX_FACTOR         = (MAX_FACTOR_arg         == 0.0f) ? 8.0f    : MAX_FACTOR_arg;
    const int   REASSIGN_RADIUS    = (REASSIGN_RADIUS_arg    == 0   ) ? 3       : REASSIGN_RADIUS_arg;
    const float PHASE_JITTER_DAMP  = (PHASE_JITTER_DAMP_arg  == 0.0f) ? 0.12f   : PHASE_JITTER_DAMP_arg;
    const float ATTACK_ALPHA       = (ATTACK_ALPHA_arg       == 0.0f) ? 0.40f   : ATTACK_ALPHA_arg;
    const float RELEASE_ALPHA      = (RELEASE_ALPHA_arg      == 0.0f) ? 0.80f   : RELEASE_ALPHA_arg;
    const int   COHERENCE_RADIUS   = (COHERENCE_RADIUS_arg   == 0   ) ? 2       : COHERENCE_RADIUS_arg;
    const float ENERGY_FLOOR_REL   = (ENERGY_FLOOR_REL_arg   == 0.0f) ? 1e-4f   : ENERGY_FLOOR_REL_arg;
    const float ENERGY_CLAMP_RATIO = (ENERGY_CLAMP_RATIO_arg == 0.0f) ? 1.12f   : ENERGY_CLAMP_RATIO_arg;

    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Clamp factor
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

    // Expected analysis advance
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // Input bin
    float2 cur = input[bin];
    float inMag = hypotf_safe(cur.x, cur.y);
    float inPhase = safe_atan2f(cur.y, cur.x);

    // State
    float2 st = state[bin];
    float prevPhase = st.x;
    float synthAcc  = st.y;

    if (!isfinite(prevPhase) || !isfinite(synthAcc) || (prevPhase == 0.0f && synthAcc == 0.0f)) {
        prevPhase = inPhase;
        synthAcc  = inPhase;
    }

    // Phase delta with expected unwrap
    float delta = wrap_pi(inPhase - prevPhase - expected);

    // Instantaneous frequency (spectral reassignment surrogate)
    float instAdv = estimate_inst_freq(input, bin, chunkSize, REASSIGN_RADIUS, expected);

    // Damped true increment to reduce jitter
    float trueInc = instAdv + delta;
    trueInc = (1.0f - PHASE_JITTER_DAMP) * trueInc + PHASE_JITTER_DAMP * instAdv;

    // Synthesis increment
    float outInc = trueInc * stretch;
    synthAcc = synthAcc + outInc;

    // Neighborhood coherence (magnitude-weighted mean phase)
    int l = max(0, bin - COHERENCE_RADIUS);
    int r = min(chunkSize - 1, bin + COHERENCE_RADIUS);
    float wx = 0.0f, wy = 0.0f, wsum = 0.0f;
    for (int b = l; b <= r; ++b) {
        float2 v = input[b];
        float m = hypotf_safe(v.x, v.y);
        float ph = safe_atan2f(v.y, v.x);
        wx += m * cosf(ph);
        wy += m * sinf(ph);
        wsum += m;
    }
    float cohPhase = synthAcc;
    if (wsum > EPS_MAG) {
        float avgPh = safe_atan2f(wy, wx);
        float d = wrap_pi(avgPh - synthAcc);
        float blend = 0.18f; // gentle blend toward local mean
        cohPhase = wrap_pi(synthAcc + blend * d);
    }

    // Dual-EMA envelope with energy floor and clamp
    float prevEnv = inMag;
    float env = dual_ema(prevEnv, inMag, ATTACK_ALPHA, RELEASE_ALPHA);
    float floorMag = ENERGY_FLOOR_REL * fmaxf(prevEnv, inMag);
    env = fmaxf(env, floorMag);
    float maxAllowed = ENERGY_CLAMP_RATIO * fmaxf(prevEnv, EPS_MAG);
    if (env > maxAllowed) env = maxAllowed;

    // Output
    float s = sinf(cohPhase);
    float c = cosf(cohPhase);
    output[bin].x = env * c;
    output[bin].y = env * s;

    // Persist
    state[bin].x = inPhase;   // keep actual analysis phase for next delta
    state[bin].y = synthAcc;  // synthesized accumulator
}