#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

extern "C" __global__ void timestretch02(
    const float2* __restrict__ input,   // points to ONE chunk spectrum
    float2* __restrict__ output,        // points to ONE chunk spectrum
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor)
{
    float ov = overlap;
    ov = (ov < 0.0f) ? 0.0f : ((ov >= 1.0f) ? 0.9999f : ov);

    int overlapSize = (int)(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;

    int hopIn = chunkSize - overlapSize;

    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Per-chunk indexing only
    int idx = bin;

    // Stateless (no prev frame available in this per-chunk call):
    // Best we can do with same args: keep phase, optionally adjust expected advancement slightly.
    // If you want real pitch-preserving stretch, you MUST provide state (prevPhase/phaseAcc) across frames.
    float2 cur = input[idx];

    float mag   = hypotf(cur.x, cur.y);
    float phase = atan2f(cur.y, cur.x);

    // mild “expected” correction (doesn't create new info, but stabilizes some bins)
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);
    float outPhase = phase + expected * (float)(factor - 1.0);

    float s = sinf(outPhase);
    float c = cosf(outPhase);

    output[idx].x = mag * c;
    output[idx].y = mag * s;
}
