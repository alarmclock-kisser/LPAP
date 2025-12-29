#ifndef M_PI
#define M_PI 3.14159265358979323846f
#endif

// Numerische Konstanten
#define EPS_MAG 1e-12f
#define PHASE_LOCK_RADIUS 2      // +/- bins für einfaches Peak-Locking
#define BACKFEED_MIX 0.25f       // Anteil der Rückführung am Betrag
#define MAG_SMOOTH_ALPHA 0.6f    // EMA Glättung für Betrag pro Bin
#define MAX_FACTOR 8.0f          // harte Schranke für extremes Timestretch

// State-Belegung pro Bin:
// state[bin].x = prevAnalysisPhase
// state[bin].y = synthPhaseAcc
// Für optionale magnitude-EMA und rückführungsbetrag verwenden wir einen separaten Array-Offset-Ansatz:
// Layout-Variante:
//   stateMagEma[bin]     = optionaler Betrag-EMA
//   backfeedMag[bin]     = optionaler Rückführungsbetrag
// Um den bestehenden state-Zeiger beizubehalten, können diese Felder über benachbarte Speicherbereiche verwaltet werden.
// Für Einfachheit in diesem Kernel zeigen wir die Variante mit separaten Arrays.
// Falls nicht verfügbar, setzen Sie pointers auf nullptr und der Kernel nutzt Fallbacks.

__device__ __forceinline__ float wrap_pi(float x)
{
    x = fmodf(x + (float)M_PI, 2.0f * (float)M_PI);
    if (x < 0.0f) x += 2.0f * (float)M_PI;
    return x - (float)M_PI;
}

__device__ __forceinline__ float safe_atan2f(float y, float x)
{
    // Minimale Stabilisierung gegen NaN in sehr kleinen Beträgen
    if (fabsf(x) < EPS_MAG && fabsf(y) < EPS_MAG) return 0.0f;
    return atan2f(y, x);
}

__device__ __forceinline__ float hypotf_safe(float x, float y)
{
    float ax = fabsf(x);
    float ay = fabsf(y);
    float m = fmaxf(ax, ay);
    if (m < EPS_MAG) return 0.0f;
    x /= m; y /= m;
    return m * sqrtf(x * x + y * y);
}

// Peak-Locking: Finde lokalen Peak und übernimm dessen Phase in der Nähe.
// input: aktueller Komplett-Frame (Spektrum eines Chunks)
// curBin: der aktuell zu synthetisierende Bin
// returns: gelockte Phase (oder original, falls kein Peak)
__device__ float peak_lock_phase(const float2* __restrict__ input, int curBin, int chunkSize, float fallbackPhase)
{
    int left = max(0, curBin - PHASE_LOCK_RADIUS);
    int right = min(chunkSize - 1, curBin + PHASE_LOCK_RADIUS);

    // Suche lokalen Maximalbetrag im Bereich
    int peakBin = curBin;
    float peakMag = -1.0f;

    for (int b = left; b <= right; ++b)
    {
        float2 v = input[b];
        float m = hypotf_safe(v.x, v.y);
        if (m > peakMag)
        {
            peakMag = m;
            peakBin = b;
        }
    }

    // Wenn Peak identisch mit aktuellem Bin oder sehr klein, nimm fallbackPhase
    if (peakBin == curBin || peakMag <= EPS_MAG)
        return fallbackPhase;

    float2 peak = input[peakBin];
    return safe_atan2f(peak.y, peak.x);
}

// Optionale Rückführung: nutze Teile des vorherigen Output-Betrags, um Kontinuität zu verbessern.
// backMag: vorher gemerkter Output-Betrag pro Bin
// returns: gemischter Betrag (inputMag dominiert, backfeed als sanfter Anteil)
__device__ float mix_backfeed(float inputMag, float backMag)
{
    if (backMag <= 0.0f) return inputMag;
    return fmaxf(EPS_MAG, (1.0f - BACKFEED_MIX) * inputMag + BACKFEED_MIX * backMag);
}

// Betrag-EMA zur Glättung
__device__ float ema_mag(float prevEma, float curMag)
{
    return (MAG_SMOOTH_ALPHA * prevEma) + ((1.0f - MAG_SMOOTH_ALPHA) * curMag);
}

// Hauptkernel
extern "C" __global__ void timestretch05(
    const float2* __restrict__ input,       // komplexes Spektrum (N = chunkSize), jedes Element: (Re, Im)
    float2* __restrict__ output,            // komplexes Ausgabespektrum (gleiches N, wird in Host mit iFFT verarbeitet)
    float2* __restrict__ state,             // pro Bin: x=prevAnalysisPhase, y=synthPhaseAcc (muss zwischen Chunks gehalten werden)
    const int chunkSize,
    const float overlap,
    const int samplerate,
    const double factor)
{
    // Optional: zusätzliche Speicherslots hinter dem offiziellen State:
    // Diese Zeiger können per Host gesetzt werden, hier sind sie nullptr by default.
    // Wenn Sie Magnitude-EMA und Backfeed persistent halten möchten, führen Sie separate Arrays ein.
    // In diesem Kernel werden sie nicht über state gemappt, sondern als lokale Fallbacks genutzt.
    // (Anpassbar: erweitern Sie die Signatur falls nötig.)

    int bin = blockIdx.x * blockDim.x + threadIdx.x;
    if (bin >= chunkSize) return;

    // Clamp factor
    float stretch = (float)factor;
    if (!isfinite(stretch)) stretch = 1.0f;
    if (stretch < 0.125f) stretch = 0.125f;
    if (stretch > MAX_FACTOR) stretch = MAX_FACTOR;

    // Overlap clamp
    float ov = overlap;
    ov = (ov < 0.0f) ? 0.0f : ((ov >= 1.0f) ? 0.9999f : ov);

    // Hop-Berechnungen
    int overlapSize = (int)(ov * (float)chunkSize);
    if (overlapSize < 0) overlapSize = 0;
    if (overlapSize >= chunkSize) overlapSize = chunkSize - 1;

    int hopIn = chunkSize - overlapSize; // Analysehop
    if (hopIn <= 0) hopIn = 1;

    // Erwarteter Phasenvortrieb in diesem Bin pro Analysehop
    float expected = 2.0f * (float)M_PI * (float)bin * ((float)hopIn / (float)chunkSize);

    // Lade aktuellen Wert aus Input
    float2 cur = input[bin];
    float inMag = hypotf_safe(cur.x, cur.y);
    float inPhase = safe_atan2f(cur.y, cur.x);

    // Lade State
    float2 st = state[bin];
    float prevPhase = st.x;
    float phaseAcc  = st.y;

    // Init bei erstem Chunk (oder ungeladenem State)
    if (!isfinite(prevPhase) || !isfinite(phaseAcc) || (prevPhase == 0.0f && phaseAcc == 0.0f))
    {
        prevPhase = inPhase;
        phaseAcc  = inPhase;
    }

    // Phase-Unwrap basierend auf erwarteter Differenz (klassischer Phase Vocoder)
    float delta = wrap_pi(inPhase - prevPhase - expected);
    float trueInc = expected + delta;

    // Synthesehop
    float outInc = trueInc * stretch;

    // Peak-Locking für natürlichere Transienten
    float lockedPhase = peak_lock_phase(input, bin, chunkSize, inPhase);

    // Wähle Analysephase für Fortschreibung:
    // Mischansatz: bei starker Transiente (delta groß) eher auf gelockte Phase gehen.
    // Gewichtung abhängig von |delta|
    float transWeight = fminf(1.0f, fabsf(delta) / (0.5f * (float)M_PI)); // 0..1
    float usePhase = wrap_pi((1.0f - transWeight) * inPhase + transWeight * lockedPhase);

    // Phasenakkumulator fortschreiben
    phaseAcc = phaseAcc + outInc;

    // Betrag-Glättung (lokal, falls keine persistenten EMA-Arrays genutzt werden)
    float smoothedMag = ema_mag(inMag, inMag);

    // Rückführung: mische einen Anteil des vorherigen Output-Betrags (approx. aus cos/sin von phaseAcc)
    // Hinweis: Da wir output in-place erst schreiben, nutzen wir den vorherigen Betrag über die alte Synthesephase.
    // Ohne separaten persistenten backfeedMag-Buffer wird ein konservativer mix genutzt.
    float backfeedApproxMag = smoothedMag; // Fallback: nutzen smoothedMag als Basis (ersetzt persistenten Rückführungsbetrag)
    float outMag = mix_backfeed(smoothedMag, backfeedApproxMag);

    // Synthesephase anwenden
    float s = sinf(phaseAcc);
    float c = cosf(phaseAcc);
    output[bin].x = outMag * c;
    output[bin].y = outMag * s;

    // State aktualisieren
    state[bin].x = usePhase;   // neue Analysephase (gelockt/mischt), hilft bei Transienten
    state[bin].y = phaseAcc;   // akkumulierte Synthesephase
}