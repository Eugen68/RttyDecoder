namespace RttyDecoder.Core.Dsp;

/// <summary>Rezultatul detectiei de polaritate.</summary>
/// <param name="MarkIsLow">true = Mark e tonul de jos (NORM), false = tonul de sus (REV), null = nu se poate spune.</param>
/// <param name="ScoreLow">Proportia de caractere bine incadrate cu ipoteza "Mark = tonul de jos" (0..1).</param>
/// <param name="ScoreHigh">La fel, cu ipoteza "Mark = tonul de sus".</param>
/// <param name="Frames">Cate caractere au fost incercate (cu ipoteza castigatoare).</param>
/// <param name="FractionLow">Fractia rularilor tonului de jos cu lungime ~x.5 biti (Mark-ul are mai multe).</param>
/// <param name="FractionHigh">La fel, pentru tonul de sus.</param>
public readonly record struct PolarityResult(bool? MarkIsLow, double ScoreLow, double ScoreHigh, int Frames,
                                             double FractionLow = 0, double FractionHigh = 0);

/// <summary>
/// Care dintre cele doua tonuri ale unui semnal RTTY e Mark. In spectru tonurile arata la fel, dar
/// in timp nu. Doua indicii, in ordinea increderii:
/// <list type="number">
/// <item>Duratele: bitul de stop are 1.5 biti, deci doar Mark-ul are rulari de lungime fractionara
/// (1.5, 2.5... biti); rularile de Space sunt mereu numere intregi de biti. Se numara, pentru fiecare
/// ton, ce fractie din rularile lui (1.2-8 biti) are partea fractionara ~0.5.</item>
/// <item>Incadrarea (doar daca duratele nu decid, de ex. stop de 1 sau 2 biti): cu polaritatea
/// corecta fiecare caracter incepe cu start pe Space si se termina cu stop pe Mark; praguri stricte.</item>
/// </list>
/// Incadrarea singura se insela pe RYRY (biti alternanti, aproape simetrici la inversare): pe
/// inregistrarea reala W6IWI 14089 gresea ~6% din decizii; cu duratele 0. Corectia vine din aplicatia
/// Android (Eugen, 7ff7b6b), masurata pe inregistrari reale. Pe zgomot sau semnal prea scurt: null.
/// </summary>
public static class RttyPolarity
{
    public const double Baud = 45.45;

    /// <summary>Esantioane de decizie pe bit.</summary>
    private const int PerBit = 8;

    /// <summary>Latimea de banda a filtrelor de ton (Hz) - cat sa urmareasca bitii de 22 ms.</summary>
    private const double FilterHz = 35;

    /// <summary>Duratele: diferenta minima intre fractiile de rulari x.5 ale celor doua tonuri.</summary>
    public const double MinDurationMargin = 0.20;

    /// <summary>Duratele: minimul de rulari (1.2-8 biti, ambele tonuri) ca sa decida.</summary>
    public const int MinRuns = 16;

    /// <summary>
    /// Duratele decid doar daca macar una dintre ipoteze incadreaza atatea caractere (0..1): altfel in
    /// fereastra nu e semnal decodabil (pauza, alt semnal, QRN) si rularile sunt intamplatoare. Pe
    /// inregistrarile reale (ferestre de 5 s, NORM + REV oglindit) fara ea: 14086 39 gresite din 4475,
    /// QRN 7086 91 din 2147; cu ea: 0 din 4426, 1 din 1843 (RYRY 14089: 1 din 889).
    /// </summary>
    public const double MinDurationFraming = 0.30;

    /// <summary>Incadrarea: minimul de caractere incadrate ca sa decida.</summary>
    public const int MinFrames = 12;

    /// <summary>Incadrarea: diferenta minima intre scoruri ca sa decida.</summary>
    public const double MinMargin = 0.30;

    /// <summary>Incadrarea: scorul minim al ipotezei castigatoare.</summary>
    public const double MinScore = 0.6;

    /// <param name="audio">Ultimele secunde de semnal (mono, +-1), ideal 3-6 s.</param>
    /// <param name="lowHz">Tonul de jos al perechii (Hz).</param>
    /// <param name="highHz">Tonul de sus (Hz), de obicei <paramref name="lowHz"/> + 170.</param>
    public static PolarityResult Detect(ReadOnlySpan<float> audio, int sampleRate, double lowHz, double highHz)
    {
        if (sampleRate <= 0 || audio.Length < sampleRate) return new(null, 0, 0, 0);

        // 1. care ton e mai puternic, de PerBit ori pe bit: doua filtre complexe de ordinul 1 (rezonatoare)
        int step = Math.Max(1, (int)Math.Round(sampleRate / (Baud * PerBit)));
        double spb = sampleRate / Baud / step;   // esantioane de decizie pe bit (~8)
        double a = 1 - Math.Exp(-2 * Math.PI * FilterHz / sampleRate);
        double wl = 2 * Math.PI * lowHz / sampleRate, wh = 2 * Math.PI * highHz / sampleRate;
        double lr = 0, li = 0, hr = 0, hi = 0;
        int skip = (int)(0.05 * sampleRate);        // filtrele se stabilizeaza
        var lowOn = new List<bool>(audio.Length / step + 1);
        for (int i = 0; i < audio.Length; i++)
        {
            double x = audio[i];
            lr += a * (x * Math.Cos(wl * i) - lr);
            li += a * (-x * Math.Sin(wl * i) - li);
            hr += a * (x * Math.Cos(wh * i) - hr);
            hi += a * (-x * Math.Sin(wh * i) - hi);
            if (i >= skip && i % step == 0) lowOn.Add(lr * lr + li * li > hr * hr + hi * hi);
        }

        // 2. duratele: rularile fractionare (bitul de stop de 1.5) sunt ale Mark-ului
        var (fLow, rLow) = Durations(lowOn, spb, lowTone: true);
        var (fHigh, rHigh) = Durations(lowOn, spb, lowTone: false);

        // 3. incadrarea caracterelor cu fiecare ipoteza
        var (sLow, nLow) = Score(lowOn, spb, markIsLow: true);
        var (sHigh, nHigh) = Score(lowOn, spb, markIsLow: false);

        if (rLow + rHigh >= MinRuns && Math.Abs(fLow - fHigh) >= MinDurationMargin
            && Math.Max(sLow, sHigh) >= MinDurationFraming)
        {
            bool byDuration = fLow > fHigh;
            return new PolarityResult(byDuration, sLow, sHigh, byDuration ? nLow : nHigh, fLow, fHigh);
        }
        bool low = sLow >= sHigh;
        int frames = low ? nLow : nHigh;
        bool sure = frames >= MinFrames && Math.Abs(sLow - sHigh) >= MinMargin && Math.Max(sLow, sHigh) >= MinScore;
        return new PolarityResult(sure ? low : null, sLow, sHigh, frames, fLow, fHigh);
    }

    /// <summary>
    /// Rularile unui ton (fara prima si ultima, taiate de fereastra) cu lungimea intre 1.2 si 8 biti:
    /// fractia celor cu partea fractionara intre 0.3 si 0.7, si cate sunt.
    /// </summary>
    private static (double Fraction, int Runs) Durations(List<bool> lowOn, double spb, bool lowTone)
    {
        int fractional = 0, total = 0, i = 0, m = lowOn.Count;
        while (i < m)
        {
            int j = i;
            while (j < m && lowOn[j] == lowOn[i]) j++;
            if (lowOn[i] == lowTone && i > 0 && j < m)
            {
                double bits = (j - i) / spb;
                if (bits > 1.2 && bits < 8)
                {
                    total++;
                    double f = bits - Math.Floor(bits);
                    if (f > 0.3 && f < 0.7) fractional++;
                }
            }
            i = j;
        }
        return (total == 0 ? 0 : (double)fractional / total, total);
    }

    /// <summary>
    /// UART asincron: la fiecare trecere Mark -&gt; Space (start), bitul de start trebuie sa fie Space
    /// la mijloc, iar stopul Mark la 6.5 si 7.2 biti. Un caracter bun sare peste el; unul rau avanseaza
    /// un esantion. Scorul = caractere bune / caractere incercate.
    /// </summary>
    private static (double Score, int Frames) Score(List<bool> lowOn, double spb, bool markIsLow)
    {
        bool Mark(int i) => lowOn[i] == markIsLow;
        int good = 0, bad = 0;
        int need = (int)Math.Ceiling(7.3 * spb);
        int i = 1;
        while (i + need < lowOn.Count)
        {
            if (Mark(i - 1) && !Mark(i))
            {
                bool ok = !Mark(i + (int)(0.5 * spb))
                          && Mark(i + (int)(6.5 * spb))
                          && Mark(i + (int)(7.2 * spb));
                if (ok)
                {
                    good++;
                    i += (int)(7.0 * spb);   // in bitul de stop; urmatorul start vine dupa
                    continue;
                }
                bad++;
            }
            i++;
        }
        int n = good + bad;
        return (n == 0 ? 0 : (double)good / n, good);
    }
}
