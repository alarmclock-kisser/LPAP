#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

extern "C" __global__ void timestretch04(
    const float2* __restrict__ input,   // ONE chunk spectrum
    float2* __restrict__ output,        // ONE chunk spectrum
    float2* __restrict__ state,         // length = chunkSize. state[bin] = {prevPhase, phaseAcc}
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor)
{
    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    float ov = overlap;
    ov = (ov < 0.0f) ? 0.0f : ((ov >= 1.0f) ? 0.9999f : ov);

    int overlapSize = (int)(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;

    int hopIn = chunkSize - overlapSize;
    if (hopIn <= 0) hopIn = 1;

    // read current bin
    float2 cur = input[bin];
    float mag = hypotf(cur.x, cur.y);
    float phase = atan2f(cur.y, cur.x);

    // expected phase advance (for bin in hopIn)
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // load state
    float2 st = state[bin];
    float prevPhase = st.x;
    float phaseAcc  = st.y;

    // init: if state is "empty" (very likely on first chunk), seed it
    // (if you really want deterministic init: host-side memset zero is fine)
    if (prevPhase == 0.0f && phaseAcc == 0.0f)
    {
        prevPhase = phase;
        phaseAcc  = phase;
    }

    // true phase increment
    float delta = wrap_pi(phase - prevPhase - expected);
    float trueInc = expected + delta;

    // synthesis hop = hopIn * factor (PVOC)
    float outInc = trueInc * (float)factor;
    phaseAcc = phaseAcc + outInc;

    // write output with accumulated synthesis phase
    float s = sinf(phaseAcc);
    float c = cosf(phaseAcc);
    output[bin].x = mag * c;
    output[bin].y = mag * s;

    // update state
    state[bin].x = phase;     // new prev analysis phase
    state[bin].y = phaseAcc;  // new synth accumulator
}
