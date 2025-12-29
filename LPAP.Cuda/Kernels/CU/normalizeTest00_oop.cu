extern "C" __global__
void normalizeTest00_oop(const float* __restrict__ in, float* __restrict__ out, long length, float amplitude)
{
    long idx = (long)(blockIdx.x * blockDim.x + threadIdx.x);
    if (idx < 0 || idx >= length)
    {
        return;
    }

    // Out-of-Place No-Op: kopiere unverändert (kein Einsatz von amplitude)
    // Für echten No-Op kann auch nichts geschrieben werden; zum Test hier Copy.
    out[idx] = in[idx];
}