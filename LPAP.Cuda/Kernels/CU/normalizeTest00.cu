extern "C" __global__
void normalizeTest00(float* inout, long length, float amplitude)
{
    // In-place No-Op: nur Bounds-Check, keine Manipulation
    long idx = (long)(blockIdx.x * blockDim.x + threadIdx.x);
    if (idx >= length || idx < 0)
    {
        return;
    }

    // bewusst nichts tun:
    // float v = inout[idx]; // optionales Read für Testzwecke
    // inout[idx] = v;       // kein Schreibzugriff notwendig für No-Op
}