extern "C" __global__ void ifft_scale_01(
    float* data,       // one time-domain chunk after IFFT
    const int n)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;

    data[i] *= (1.0f / (float)n);
}
