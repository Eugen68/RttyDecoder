namespace RttyDecoder.Core.Dsp;

/// <summary>
/// A-TUNE: cauta in spectrul mediat perechea de varfuri aflate la 170 Hz una de alta (11 bin-uri
/// de 15.6 Hz), in 500-3000 Hz - acelasi criteriu ca pe Android. In plus, pozitia perechii se
/// rafineaza sub-bin (interpolare parabolica pe scorul perechii), deci eroarea de acord scade de la
/// ~+-8 Hz (jumatate de bin) la cativa Hz.
/// </summary>
public static class AutoTune
{
    private const int ShiftBins = 11;
    private const int FirstBin = 32;   // 500 Hz
    private const int LastBin = 192;   // 3000 Hz

    /// <summary>Pragul pe scorul perechii (magnitudini liniare, FFT Hamming de 512, semnal +-1).</summary>
    public const double MinPairScore = 0.002;

    /// <summary>
    /// Frecventa tonului de jos al perechii (Hz), sau null daca nu e niciun semnal destul de puternic.
    /// In NORM tonul de jos e Mark; in REV Mark e tonul de sus (+170 Hz).
    /// </summary>
    public static double? FindLowerTone(double[] magnitude, double binHz)
    {
        int best = -1;
        double bestScore = -1;
        for (int i = FirstBin; i < LastBin && i + ShiftBins < magnitude.Length; i++)
        {
            double score = magnitude[i] + magnitude[i + ShiftBins];
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        if (best < 0 || bestScore <= MinPairScore) return null;

        double delta = 0;
        if (best > 0 && best + ShiftBins + 1 < magnitude.Length)
        {
            double a = magnitude[best - 1] + magnitude[best - 1 + ShiftBins];
            double c = magnitude[best + 1] + magnitude[best + 1 + ShiftBins];
            double denom = a - 2 * bestScore + c;
            if (denom < 0) delta = Math.Clamp(0.5 * (a - c) / denom, -0.5, 0.5);
        }
        return (best + delta) * binHz;
    }
}
