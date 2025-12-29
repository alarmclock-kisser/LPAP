// Stateless legacy time-stretch kernel using float overlap (0..1) and CUFFT-friendly handling.
// Produces phase-vocoder style output in frequency domain. Ensure host-side IFFT scales by 1/chunkSize.
//
// Key fixes for "stille":
// - use float overlap to derive hopIn consistently: hopIn = max(1, chunkSize - round(overlap * chunkSize))
// - avoid zeroing bins: never write 0 unless input is NaN; pass-through on tiny magnitudes
// - guard NaNs, but keep current magnitude/phase if previous is invalid
// - keep expected advance = 2*pi * bin * hopIn / chunkSize (independent of sampleRate)

#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

__device__ __forceinline__ float2 make_c(float x, float y) { float2 v; v.x = x; v.y = y; return v; }

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

__device__ __forceinline__ float hypotf_safe(float x, float y)
{
    float ax = fabsf(x), ay = fabsf(y);
    float m = fmaxf(ax, ay);
    if (m == 0.0f) return 0.0f;
    x /= m; y /= m;
    return m * sqrtf(x * x + y * y);
}

extern "C" __global__ void timestretch_legacy01(
    const float2* __restrict__ samples,   // concatenated complex spectra: chunks * chunkSize
    float2* __restrict__ output,
    const int totalSamples,               // total complex elements
    const int chunkSize,                  // FFT size
    const float overlap,                  // 0..1
    const int sampleRate,                 // kept for signature parity (not used in expected advance)
    const double factor)
{
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx >= totalSamples) return;

    // chunk/bin from linear index
    int chunk = idx / chunkSize;
    int bin   = idx % chunkSize;

    // derive hopIn from float overlap (robust clamp)
    float ov = overlap;
    if (ov < 0.0f) ov = 0.0f;
    if (ov >= 1.0f) ov = 0.9999f;
    int overlapSize = (int)roundf(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;
    int hopIn = chunkSize - overlapSize;
    if (hopIn <= 0) hopIn = 1;

    // read current bin; guard NaNs but avoid writing zeros if possible
    float2 cur = samples[idx];
    float curx = isfinite(cur.x) ? cur.x : 0.0f;
    float cury = isfinite(cur.y) ? cur.y : 0.0f;

    // First chunk: passthrough
    if (chunk == 0)
    {
        output[idx] = make_c(curx, cury);
        return;
    }

    // read previous chunk same bin
    int prevIdx = idx - chunkSize;
    float2 prev = samples[prevIdx];
    float prevx = isfinite(prev.x) ? prev.x : curx; // fallback to current if invalid
    float prevy = isfinite(prev.y) ? prev.y : cury;

    // magnitudes and phases
    float mag       = hypotf_safe(curx, cury);
    float phaseCur  = atan2f(cury, curx);
    float phasePrev = atan2f(prevy, prevx);

    // If magnitude is extremely small, passthrough to avoid zeroing
    if (mag <= 0.0f)
    {
        output[idx] = make_c(curx, cury);
        return;
    }

    // expected phase advance per bin
    float expectedPhaseAdv = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // delta and wrap
    float deltaPhase = phaseCur - phasePrev;
    float delta = wrap_pi(deltaPhase - expectedPhaseAdv);

    // stretch
    float stretch = (float)factor;
    if (!isfinite(stretch)) stretch = 1.0f;

    float phaseOut = phasePrev + expectedPhaseAdv + (delta * stretch);

    // synth with current magnitude
    float s = sinf(phaseOut);
    float c = cosf(phaseOut);
    output[idx] = make_c(mag * c, mag * s);
}