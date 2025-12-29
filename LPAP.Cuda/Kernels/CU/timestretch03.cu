#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

__device__ __forceinline__ float mag2(float2 v)
{
    return v.x * v.x + v.y * v.y;
}

__device__ __forceinline__ float fast_mag(float2 v)
{
    return hypotf(v.x, v.y);
}

extern "C" __global__ void timestretch03(
    const float2* __restrict__ input,   // ONE chunk spectrum
    float2* __restrict__ output,        // ONE chunk spectrum
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor)
{
    // clamp overlap
    float ov = overlap;
    ov = (ov < 0.0f) ? 0.0f : ((ov >= 1.0f) ? 0.9999f : ov);

    int overlapSize = (int)(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;

    int hopIn = chunkSize - overlapSize;

    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Read current bin (and neighbors if available)
    float2 cur = input[bin];

    // magnitude + phase
    float mag = fast_mag(cur);
    float phase = atan2f(cur.y, cur.x);

    // ---- (A) Local magnitude smoothing to reduce "metallic" ----
    // Very small 3-tap smoothing; keeps transients mostly intact but reduces bin-wise jitter.
    float magSm = mag;
    if (bin > 0 && bin + 1 < chunkSize)
    {
        float magL = fast_mag(input[bin - 1]);
        float magR = fast_mag(input[bin + 1]);
        magSm = 0.25f * magL + 0.5f * mag + 0.25f * magR;
    }

    // expected phase advance for hopIn
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // ---- (B) Transient protection via spectral flux proxy ----
    // We don't have prev-frame, so we approximate "transientness" by comparing to neighbors:
    // if a bin is a sharp peak compared to neighbors, treat it as transient/tonal anchor.
    float peakness = 0.0f;
    if (bin > 0 && bin + 1 < chunkSize)
    {
        float m  = mag2(cur);
        float ml = mag2(input[bin - 1]);
        float mr = mag2(input[bin + 1]);

        // peakness in [0..1-ish]
        float neigh = 0.5f * (ml + mr) + 1e-12f;
        peakness = fminf(2.0f, m / neigh); // >1 means peak
    }

    // gate: if strong peak => reduce stretching of phase (prevents "smear" on kicks)
    // tweak constants: threshold 1.4..2.0 works well
    float transientGate = 1.0f;
    if (peakness > 1.6f)
    {
        // strong peak: keep phase closer to original (less time-stretching in phase)
        transientGate = 0.25f; // 0=no stretch of phase deviation, 1=full stretch
    }
    else if (peakness > 1.2f)
    {
        transientGate = 0.55f;
    }

    // ---- (C) Phase-locking to local peak (bin-wise) ----
    // If we're near a strong peak, lock our phase offset to the strongest of {bin-1,bin,bin+1}.
    int anchor = bin;
    if (bin > 0 && bin + 1 < chunkSize)
    {
        float m0 = mag2(input[bin - 1]);
        float m1 = mag2(input[bin]);
        float m2 = mag2(input[bin + 1]);

        if (m0 > m1 && m0 > m2) anchor = bin - 1;
        else if (m2 > m1 && m2 > m0) anchor = bin + 1;
        else anchor = bin;
    }

    float phaseAnchor = phase;
    if (anchor != bin)
    {
        float2 a = input[anchor];
        phaseAnchor = atan2f(a.y, a.x);
    }

    // ---- Compute output phase ----
    // Stateless baseline:
    // outPhase = phase + expected*(factor-1)
    //
    // Improvement:
    // - apply transient gate (reduces phase stretching around peaks)
    // - lock relative phase to anchor (reduces "phasiness"/comb)
    float phaseStretch = (float)(factor - 1.0);
    float outPhase = phase + expected * phaseStretch * transientGate;

    // lock: preserve relative phase offset to anchor
    // (only meaningful if anchor differs; otherwise offset is 0)
    if (anchor != bin)
    {
        float rel = wrap_pi(phase - phaseAnchor);
        float outAnchor = phaseAnchor + expected * phaseStretch * transientGate;
        outPhase = outAnchor + rel;
    }

    // Write output
    float s = sinf(outPhase);
    float c = cosf(outPhase);
    output[bin].x = magSm * c;
    output[bin].y = magSm * s;
}
