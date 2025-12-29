// SBSMS-inspired spectral band tracking and modulation synthesis for high-quality time-stretch.
// Input/Output: float2 complex spectrum (FFT bins), per-chunk processing.
// Kernel performs:
//  - magnitude/phase read
//  - band (partial) tracking via local peak search and continuation
//  - instantaneous frequency estimation (phase difference + bias correction)
//  - adaptive phase propagation (stretch)
//  - magnitude modulation (attack/release) per tracked band
// State layout per bin (compact):
//   state[bin].x = prevAnalysisPhase
//   state[bin].y = synthPhaseAcc
//
// Notes:
// - This kernel is designed to be called per STFT frame with COLA-compatible windowing on host.
// - Tunables are passed via args; if an arg is 0, a default is used.
// - This is a CUDA adaptation inspired by SBSMS concepts (band tracking, IF estimation, adaptive resynthesis).
//   It is not a verbatim port of any specific SBSMS codebase.

#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

// ---- small helpers ----
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

// Local peak search to identify partial center around a bin
__device__ int find_local_peak(const float2* __restrict__ input, int bin, int N, int radius)
{
    int left = max(0, bin - radius);
    int right = min(N - 1, bin + radius);
    int pbin = bin;
    float pmag = -1.0f;

    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float mag = hypotf_safe(v.x, v.y);
        if (mag > pmag) { pmag = mag; pbin = b; }
    }
    return pbin;
}

// Instantaneous frequency estimate around peak (SBSMS-inspired surrogate)
__device__ float estimate_if(const float2* __restrict__ input, int peakBin, int N, int rad, float expected)
{
    int left = max(0, peakBin - rad);
    int right = min(N - 1, peakBin + rad);

    float wsum = 0.0f;
    float acc = 0.0f;
    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float m = hypotf_safe(v.x, v.y);
        float ph = safe_atan2f(v.y, v.x);
        float d = wrap_pi(ph - expected * ((float)b / fmaxf(1.0f, (float)peakBin)));
        acc += m * d;
        wsum += m;
    }
    if (wsum <= 1e-12f) return expected;
    float meanDelta = acc / wsum;
    return expected + meanDelta;
}

// Attack/Release dual-EMA for magnitude modulation
__device__ float dual_ema(float prev, float cur, float attackA, float releaseA)
{
    float a = (cur > prev) ? attackA : releaseA;
    return a * prev + (1.0f - a) * cur;
}

// Neighborhood phase coherence blend (avoid combing)
__device__ float blend_coherence(const float2* __restrict__ input, int peakBin, int N, int radius, float phase, float blend)
{
    int left = max(0, peakBin - radius);
    int right = min(N - 1, peakBin + radius);

    float vx = 0.0f, vy = 0.0f, wsum = 0.0f;
    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float m = hypotf_safe(v.x, v.y);
        float ph = safe_atan2f(v.y, v.x);
        vx += m * cosf(ph);
        vy += m * sinf(ph);
        wsum += m;
    }
    if (wsum <= 1e-12f) return phase;
    float avgPh = safe_atan2f(vy, vx);
    float d = wrap_pi(avgPh - phase);
    return wrap_pi(phase + blend * d);
}

// ---- Kernel ----
// Tunables (0 => use defaults):
// EPS_MAG_arg              default: 1e-12f
// MAX_FACTOR_arg           default: 8.0f
// PEAK_RADIUS_arg          default: 3
// IF_RADIUS_arg            default: 3
// PHASE_DAMP_arg           default: 0.14f
// COHERENCE_RADIUS_arg     default: 2
// COHERENCE_BLEND_arg      default: 0.18f
// ATTACK_ALPHA_arg         default: 0.35f
// RELEASE_ALPHA_arg        default: 0.80f
// FLOOR_REL_arg            default: 1e-4f
// ENERGY_CLAMP_ratio_arg   default: 1.12f
extern "C" __global__ void timestretch_sbsms01(
    const float2* __restrict__ input,
    float2* __restrict__ output,
    float2* __restrict__ state,
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor,
    // Tunables
    const float EPS_MAG_arg,
    const float MAX_FACTOR_arg,
    const int   PEAK_RADIUS_arg,
    const int   IF_RADIUS_arg,
    const float PHASE_DAMP_arg,
    const int   COHERENCE_RADIUS_arg,
    const float COHERENCE_BLEND_arg,
    const float ATTACK_ALPHA_arg,
    const float RELEASE_ALPHA_arg,
    const float FLOOR_REL_arg,
    const float ENERGY_CLAMP_ratio_arg
)
{
    // Resolve defaults
    const float EPS_MAG            = (EPS_MAG_arg            == 0.0f) ? 1e-12f : EPS_MAG_arg;
    const float MAX_FACTOR         = (MAX_FACTOR_arg         == 0.0f) ? 8.0f    : MAX_FACTOR_arg;
    const int   PEAK_RADIUS        = (PEAK_RADIUS_arg        == 0   ) ? 3       : PEAK_RADIUS_arg;
    const int   IF_RADIUS          = (IF_RADIUS_arg          == 0   ) ? 3       : IF_RADIUS_arg;
    const float PHASE_DAMP         = (PHASE_DAMP_arg         == 0.0f) ? 0.14f   : PHASE_DAMP_arg;
    const int   COHERENCE_RADIUS   = (COHERENCE_RADIUS_arg   == 0   ) ? 2       : COHERENCE_RADIUS_arg;
    const float COHERENCE_BLEND    = (COHERENCE_BLEND_arg    == 0.0f) ? 0.18f   : COHERENCE_BLEND_arg;
    const float ATTACK_ALPHA       = (ATTACK_ALPHA_arg       == 0.0f) ? 0.35f   : ATTACK_ALPHA_arg;
    const float RELEASE_ALPHA      = (RELEASE_ALPHA_arg      == 0.0f) ? 0.80f   : RELEASE_ALPHA_arg;
    const float FLOOR_REL          = (FLOOR_REL_arg          == 0.0f) ? 1e-4f   : FLOOR_REL_arg;
    const float ENERGY_CLAMP_RATIO = (ENERGY_CLAMP_ratio_arg == 0.0f) ? 1.12f   : ENERGY_CLAMP_ratio_arg;

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

    // Expected analysis phase advance
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // Read input
    float2 cur = input[bin];
    float inMag = hypotf_safe(cur.x, cur.y);
    float inPhase = safe_atan2f(cur.y, cur.x);

    // Read state
    float2 st = state[bin];
    float prevPhase = st.x;
    float synthAcc  = st.y;

    if (!isfinite(prevPhase) || !isfinite(synthAcc) || (prevPhase == 0.0f && synthAcc == 0.0f)) {
        prevPhase = inPhase;
        synthAcc  = inPhase;
    }

    // Phase delta vs expected
    float delta = wrap_pi(inPhase - prevPhase - expected);

    // Peak-centered band tracking
    int peakBin = find_local_peak(input, bin, chunkSize, PEAK_RADIUS);

    // IF estimate near peak
    float instAdv = estimate_if(input, peakBin, chunkSize, IF_RADIUS, expected);

    // Adaptive phase propagation (damped jitter, SBSMS-inspired)
    float trueInc = instAdv + delta;
    trueInc = (1.0f - PHASE_DAMP) * trueInc + PHASE_DAMP * instAdv;

    float outInc = trueInc * stretch;
    synthAcc = synthAcc + outInc;

    // Neighborhood phase coherence
    float cohPhase = blend_coherence(input, peakBin, chunkSize, COHERENCE_RADIUS, synthAcc, COHERENCE_BLEND);

    // Magnitude modulation (dual-EMA) with floor/clamp
    float prevEnv = inMag; // local fallback (persistent envelope can be added externally if desired)
    float env = dual_ema(prevEnv, inMag, ATTACK_ALPHA, RELEASE_ALPHA);
    float floorMag = FLOOR_REL * fmaxf(prevEnv, inMag);
    env = fmaxf(env, floorMag);
    float maxAllowed = ENERGY_CLAMP_RATIO * fmaxf(prevEnv, EPS_MAG);
    if (env > maxAllowed) env = maxAllowed;

    // Emit complex
    float s = sinf(cohPhase);
    float c = cosf(cohPhase);
    output[bin].x = env * c;
    output[bin].y = env * s;

    // Persist minimal state
    state[bin].x = inPhase;
    state[bin].y = synthAcc;
}