extern "C" __global__ void normalize00(
    float* chunk,
    const int chunkSize,
    const float overlap,
    const float amplitude)
{
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    if (idx >= chunkSize) return;

    // Bestimme Maximalwert des gesamten Chunks ohne Shared/Atomics
    float maxVal = 0.0f;
    for (int i = 0; i < chunkSize; ++i)
    {
        float v = chunk[i];
        v = (v >= 0.0f) ? v : -v; // fabsf ohne Funktionsaufruf für minimale Kosten
        if (v > maxVal) maxVal = v;
    }

    if (maxVal <= 1e-12f) return; // nichts skalieren bei quasi-Null

    float gain = amplitude / maxVal;
    chunk[idx] *= gain;
}