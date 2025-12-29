// High-quality GPU phase vocoder kernel with transient-preserving peak locking,
// cross-band coherence, envelope shaping and controlled backfeed.
// I/O: float2 complex spectrum (FFT bins): (Re, Im) -> (Re, Im).
// Signature keeps float2* input/output/state, but adds optional auxiliary buffers for quality.
// If aux pointers are nullptr, kernel falls back to safe defaults.

#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

// Tunables (safe defaults)
#define EPS_MAG              1e-12f
#define MAX_FACTOR           8.0f
#define PEAK_LOCK_RADIUS     3         // bins around current for peak search
#define COHERENCE_RADIUS     2         // enforce phase coherence across neighbors
#define TRANSIENT_DELTA_PI   0.40f     // delta threshold for transient handling (fraction of pi)
#define TRANSIENT_BLEND      0.65f     // blend toward peak phase at strong transient
#define BACKFEED_MIX         0.18f     // backfeed magnitude mix-in
#define ENVELOPE_ALPHA       0.75f     // EMA for magnitude envelope
#define FLOOR_REL            1e-4f     // relative floor (prevents vanishing bins)
#define ENERGY_CLAMP_RATIO   1.1f      // allow at most 10% energy rise from smoothing
#define PHASE_DAMP           0.10f     // damping for phase increment jitter (stabilize)

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

__device__ __forceinline__ float safe_atan2f(float y, float x)
{
    if (!isfinite(x) || !isfinite(y)) return 0.0f;
    if (fabsf(x) < EPS_MAG && fabsf(y) < EPS_MAG) return 0.0f;
    return atan2f(y, x);
}

__device__ __forceinline__ float hypotf_safe(float x, float y)
{
    float ax = fabsf(x), ay = fabsf(y);
    float m = fmaxf(ax, ay);
    if (m < EPS_MAG) return 0.0f;
    x /= m; y /= m;
    return m * sqrtf(x * x + y * y);
}

// Peak locking with local neighborhood search.
// Returns phase of peak if peak is distinct, otherwise fallbackPhase.
__device__ float peak_lock_phase(const float2* __restrict__ input, int bin, int N, float fallbackPhase)
{
    int left = max(0, bin - PEAK_LOCK_RADIUS);
    int right = min(N - 1, bin + PEAK_LOCK_RADIUS);
    int peakBin = bin;
    float peakMag = -1.0f;

    for (int b = left; b <= right; ++b) {
        float2 v = input[b];
        float mag = hypotf_safe(v.x, v.y);
        if (mag > peakMag) { peakMag = mag; peakBin = b; }
    }
    if (peakBin == bin || peakMag <= EPS_MAG) return fallbackPhase;

    float2 p = input[peakBin];
    return safe_atan2f(p.y, p.x);
}

// Cross-band coherence: blend target phase toward local average to reduce combing.
__device__ float enforce_coherence(const float2* __restrict__ input, int bin, int N, float phase)
{
    int left = max(0, bin - COHERENCE_RADIUS);
    int right = min(N - 1, bin + COHERENCE_RADIUS);

    // Weighted average by magnitude
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
    if (wsum <= EPS_MAG) return phase;

    float avgPh = safe_atan2f(vy, vx);
    // gentle blend to local mean
    float blend = 0.15f;
    float d = wrap_pi(avgPh - phase);
    return wrap_pi(phase + blend * d);
}

// Envelope EMA with energy clamp
__device__ float envelope_ema(float prevEma, float curMag)
{
    float ema = ENVELOPE_ALPHA * prevEma + (1.0f - ENVELOPE_ALPHA) * curMag;
    // clamp up to avoid artificial energy rise
    float maxAllowed = ENERGY_CLAMP_RATIO * fmaxf(prevEma, EPS_MAG);
    if (ema > maxAllowed) ema = maxAllowed;
    return ema;
}

extern "C" __global__ void timestretch06(
    const float2* __restrict__ input,       // complex spectrum (N = chunkSize)
    float2* __restrict__ output,            // complex spectrum out (N = chunkSize)
    float2* __restrict__ state,             // per-bin: x=prevAnalysisPhase, y=synthPhaseAcc
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor,
    // Optional auxiliaries (can be nullptr):
    // magnitude envelope EMA buffer per bin
    float* __restrict__ envMagEma,
    // backfeed magnitude per bin (previous synthesized magnitude)
    float* __restrict__ backfeedMag
)
{
    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Clamp factor to sane range
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

    // Expected analysis phase advance per hop for this bin
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // Current input bin
    float2 cur = input[bin];
    float inMag = hypotf_safe(cur.x, cur.y);
    float inPhase = safe_atan2f(cur.y, cur.x);

    // Load state
    float2 st = state[bin];
    float prevPhase = st.x;
    float phaseAcc  = st.y;

    // Initialize state if empty
    if (!isfinite(prevPhase) || !isfinite(phaseAcc) || (prevPhase == 0.0f && phaseAcc == 0.0f)) {
        prevPhase = inPhase;
        phaseAcc  = inPhase;
    }

    // Analysis phase delta with expected unwrap
    float delta = wrap_pi(inPhase - prevPhase - expected);

    // transient detection
    float transientStrength = fminf(1.0f, fabsf(delta) / (TRANSIENT_DELTA_PI * (float)M_PI));

    // Peak lock target phase near bin
    float lockedPhase = peak_lock_phase(input, bin, chunkSize, inPhase);

    // Blend toward locked phase at transient
    float useAnalysisPhase = wrap_pi((1.0f - transientStrength * TRANSIENT_BLEND) * inPhase
                                   + (transientStrength * TRANSIENT_BLEND) * lockedPhase);

    // True increment with gentle damping (reduce jitter)
    float trueInc = expected + delta;
    trueInc = (1.0f - PHASE_DAMP) * trueInc + PHASE_DAMP * expected;

    // Synthesis increment
    float outInc = trueInc * stretch;

    // Update synthesis accumulator
    phaseAcc = phaseAcc + outInc;

    // Coherence enforcement across neighbors
    float cohPhase = enforce_coherence(input, bin, chunkSize, phaseAcc);

    // Envelope smoothing (EMA), with floor to keep quiet bins stable
    float prevEma = (envMagEma != nullptr) ? envMagEma[bin] : inMag;
    if (!isfinite(prevEma)) prevEma = inMag;
    float smoothedMag = envelope_ema(prevEma, inMag);

    float floorMag = FLOOR_REL * fmaxf(inMag, prevEma);
    smoothedMag = fmaxf(smoothedMag, floorMag);

    // Backfeed magnitude mix-in
    float bfMag = (backfeedMag != nullptr) ? backfeedMag[bin] : smoothedMag;
    if (!isfinite(bfMag)) bfMag = smoothedMag;
    float outMag = fmaxf(EPS_MAG, (1.0f - BACKFEED_MIX) * smoothedMag + BACKFEED_MIX * bfMag);

    // Synthesize complex output at coherent phase
    float s = sinf(cohPhase);
    float c = cosf(cohPhase);
    output[bin].x = outMag * c;
    output[bin].y = outMag * s;

    // Persist state
    state[bin].x = useAnalysisPhase;   // improved analysis phase for next delta
    state[bin].y = cohPhase;           // keep coherent accumulator

    // Persist auxiliaries
    if (envMagEma != nullptr) envMagEma[bin] = smoothedMag;
    if (backfeedMag != nullptr) backfeedMag[bin] = outMag;
}