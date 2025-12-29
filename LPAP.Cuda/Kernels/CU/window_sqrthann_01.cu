extern "C" __global__ void window_sqrthann_01(
    float* data,           // one time-domain chunk (float*)
    const int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;

    // Hann: 0.5 - 0.5*cos(2*pi*i/(n-1))
    // sqrt-Hann for perfect reconstruction at 50% overlap when used on both analysis & synthesis
    float denom = (n > 1) ? (float)(n - 1) : 1.0f;
    float w = 0.5f - 0.5f * cosf(6.283185307179586f * (float)i / denom);
    w = sqrtf(fmaxf(w, 0.0f));

    data[i] *= w;
}
