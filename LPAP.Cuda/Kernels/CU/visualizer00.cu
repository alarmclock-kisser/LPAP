// visualizer00.cu
extern "C" __global__ void visualizer00(
    const float* input,      // interleaved floats (frames * channels)
    unsigned char* output,   // BGRA, width*height*4
    int width,
    int height,
    long long totalFrames,          // frames = samplesPerChannel
    long long samplePositionFrames, // window start (frame index)
    int samplesPerPixel,            // frames per x-column
    int channels                    // 1..N
)
{
    int x = (int)(blockIdx.x * blockDim.x + threadIdx.x);
    int y = (int)(blockIdx.y * blockDim.y + threadIdx.y);
    if (x >= width || y >= height) return;

    // background (black)
    const unsigned char bgB = 0, bgG = 0, bgR = 0, bgA = 255;
    const unsigned char waveB = 255, waveG = 255, waveR = 255, waveA = 255;

    // Compute min/max for this column (x)
    long long start = samplePositionFrames + (long long)x * (long long)samplesPerPixel;
    long long end   = start + (long long)samplesPerPixel;

    if (end <= 0 || start >= totalFrames || samplesPerPixel <= 0 || channels <= 0 || height <= 0)
    {
        // just background
        int idx = (y * width + x) * 4;
        output[idx + 0] = bgB;
        output[idx + 1] = bgG;
        output[idx + 2] = bgR;
        output[idx + 3] = bgA;
        return;
    }

    if (start < 0) start = 0;
    if (end > totalFrames) end = totalFrames;

    float mn =  1e30f;
    float mx = -1e30f;

    for (long long f = start; f < end; ++f)
    {
        long long base = f * (long long)channels;

        // average channels
        float s = 0.0f;
        #pragma unroll
        for (int c = 0; c < channels; ++c)
            s += input[base + c];
        s *= 1.0f / (float)channels;

        mn = fminf(mn, s);
        mx = fmaxf(mx, s);
    }

    // clamp audio range
    mn = fmaxf(-1.0f, fminf(1.0f, mn));
    mx = fmaxf(-1.0f, fminf(1.0f, mx));

    // map to y pixel coordinates
    float mid = 0.5f * (float)(height - 1);
    float yTopF = mid - (mx * mid);
    float yBotF = mid - (mn * mid);

    int y0 = (int)floorf(fminf(yTopF, yBotF));
    int y1 = (int)ceilf (fmaxf(yTopF, yBotF));
    if (y0 < 0) y0 = 0;
    if (y1 >= height) y1 = height - 1;

    bool isWave = (y >= y0 && y <= y1);

    int idx = (y * width + x) * 4;
    output[idx + 0] = isWave ? waveB : bgB;
    output[idx + 1] = isWave ? waveG : bgG;
    output[idx + 2] = isWave ? waveR : bgR;
    output[idx + 3] = isWave ? waveA : bgA;
}
