namespace RttyDecoder.Core.Dsp;

/// <summary>Rezultatul detectiei de polaritate.</summary>
/// <param name="MarkIsLow">true = Mark e tonul de jos (NORM), false = tonul de sus (REV), null = nu se poate spune.</param>
/// <param name="ScoreLow">Proportia de caractere bine incadrate cu ipoteza "Mark = tonul de jos" (0..1).</param>
/// <param name="ScoreHigh">La fel, cu ipoteza "Mark = tonul de sus".</param>
/// <param name="Frames">Cate caractere au fost incercate (cu ipoteza castigatoare).</param>
public readonly record struct PolarityResult(bool? MarkIsLow, double ScoreLow, double ScoreHigh, int Frames);

/// <summary>
/// Care dintre cele doua tonuri ale unui semnal RTTY e Mark. In spectru tonurile arata la fel, dar
/// in timp nu: cu polaritatea corecta fiecare caracter incepe cu un bit de start Space si se termina
/// cu bitul de stop Mark (1.5 biti), deci caracterele "se incadreaza"; cu polaritatea gresita
/// incadrarea esueaza mereu. Se incearca ambele ipoteze pe ultimele secunde de audio si castiga cea
/// cu incadrarea clar mai buna. Pe zgomot sau semnal prea scurt: null.
/// </summary>
public static class RttyPolarity
{
    public const double Baud = 45.45;

    /// <summary>Esantioane de decizie pe bit.</summary>
    private const int PerBit = 8;

    /// <summary>Latimea de banda a filtrelor de ton (Hz) - cat sa urmareasca bitii de 22 ms.</summary>
    private const double FilterHz = 35;

    /// <summary>Minimul de caractere incadrate ca sa decida.</summary>
    public const int MinFrames = 6;

    /// <summary>Diferenta minima intre scoruri ca sa decida.</summary>
    public const double MinMargin = 0.15;

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

        // 2. incadrarea caracterelor cu fiecare ipoteza
        var (sLow, nLow) = Score(lowOn, spb, markIsLow: true);
        var (sHigh, nHigh) = Score(lowOn, spb, markIsLow: false);
        bool low = sLow >= sHigh;
        int frames = low ? nLow : nHigh;
        bool sure = frames >= MinFrames && Math.Abs(sLow - sHigh) >= MinMargin && Math.Max(sLow, sHigh) >= 0.5;
        return new PolarityResult(sure ? low : null, sLow, sHigh, frames);
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
