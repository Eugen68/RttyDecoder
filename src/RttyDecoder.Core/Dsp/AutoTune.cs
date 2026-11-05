namespace RttyDecoder.Core.Dsp;

public readonly record struct AutoTuneCandidate(double LowerToneHz, double Confidence, double WeakToneSnrDb);

/// <summary>Pereche RTTY: doua maxime locale, separate cu ShiftHz, ambele peste zgomot. Independent de FFT.</summary>
public static class AutoTune
{
    // Pastrat pentru compatibilitatea surselor; criteriul nou foloseste SNR relativ, nu suma bruta FFT.
    public const double MinPairScore = 0.002;
    public static double? FindLowerTone(double[] magnitude, double binHz) => FindCandidate(magnitude, binHz)?.LowerToneHz;

    public static AutoTuneCandidate? FindCandidate(double[] magnitude, double binHz,
        double minHz = 500, double maxHz = 3000, double shiftHz = 170)
    {
        if (!double.IsFinite(binHz) || binHz <= 0 || !double.IsFinite(shiftHz) || shiftHz <= 0 || magnitude.Length < 4) return null;
        int first = Math.Max(1, (int)Math.Ceiling(minHz / binHz));
        int last = Math.Min(magnitude.Length - 2, (int)Math.Floor((maxHz + shiftHz) / binHz));
        if (first >= last) return null;
        double[] floor = magnitude[first..(last + 1)].Where(double.IsFinite).Select(x => Math.Max(0, x)).Order().ToArray();
        if (floor.Length == 0) return null;
        double noise = Math.Max(1e-8, floor[floor.Length / 2]);
        var peaks = new List<(double Hz, double Amplitude)>();
        for (int i = first; i <= last; i++)
        {
            double value = magnitude[i];
            if (!double.IsFinite(value) || value < Math.Max(1e-6, noise * 3.5)
                || value <= magnitude[i - 1] || value < magnitude[i + 1]) continue;
            double a = Math.Log(Math.Max(magnitude[i - 1], 1e-12));
            double b = Math.Log(Math.Max(value, 1e-12));
            double c = Math.Log(Math.Max(magnitude[i + 1], 1e-12));
            double denominator = a - 2 * b + c;
            double delta = denominator < 0 ? Math.Clamp(.5 * (a - c) / denominator, -.5, .5) : 0;
            peaks.Add(((i + delta) * binHz, value));
        }
        AutoTuneCandidate? best = null;
        double bestScore = 0;
        foreach (var low in peaks)
        {
            if (low.Hz < minHz || low.Hz > maxHz) continue;
            foreach (var high in peaks)
            {
                double error = Math.Abs(high.Hz - low.Hz - shiftHz);
                double tolerance = Math.Max(8, binHz * 1.1);
                if (error > tolerance) continue;
                if (Math.Min(low.Amplitude, high.Amplitude) < .02 * Math.Max(low.Amplitude, high.Amplitude)) continue;
                double score = Math.Sqrt(low.Amplitude * high.Amplitude) * (1 - .5 * error / tolerance);
                if (score <= bestScore) continue;
                bestScore = score;
                double snr = 20 * Math.Log10(Math.Min(low.Amplitude, high.Amplitude) / noise);
                best = new((low.Hz + high.Hz - shiftHz) / 2,
                    Math.Clamp((snr - 10) / 20, 0, 1) * (1 - .5 * error / tolerance), snr);
            }
        }
        return best;
    }

    /// <summary>
    /// Toate perechile RTTY din spectru (pentru marcajele de pe waterfall), cu acelasi criteriu ca
    /// <see cref="FindCandidate"/>: doua maxime locale peste zgomot, la shiftHz unul de altul. Fiecare
    /// varf intra intr-o singura pereche; cele mai bune intai, cel mult <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<AutoTuneCandidate> FindCandidates(double[] magnitude, double binHz,
        double minHz = 500, double maxHz = 3000, double shiftHz = 170, int max = 8)
    {
        var found = new List<AutoTuneCandidate>();
        if (!double.IsFinite(binHz) || binHz <= 0 || !double.IsFinite(shiftHz) || shiftHz <= 0 || magnitude.Length < 4) return found;
        int first = Math.Max(1, (int)Math.Ceiling(minHz / binHz));
        int last = Math.Min(magnitude.Length - 2, (int)Math.Floor((maxHz + shiftHz) / binHz));
        if (first >= last) return found;
        double[] floor = magnitude[first..(last + 1)].Where(double.IsFinite).Select(x => Math.Max(0, x)).Order().ToArray();
        if (floor.Length == 0) return found;
        double noise = Math.Max(1e-8, floor[floor.Length / 2]);
        var peaks = new List<(double Hz, double Amplitude)>();
        for (int i = first; i <= last; i++)
        {
            double value = magnitude[i];
            if (!double.IsFinite(value) || value < Math.Max(1e-6, noise * 3.5)
                || value <= magnitude[i - 1] || value < magnitude[i + 1]) continue;
            double a = Math.Log(Math.Max(magnitude[i - 1], 1e-12));
            double b = Math.Log(Math.Max(value, 1e-12));
            double c = Math.Log(Math.Max(magnitude[i + 1], 1e-12));
            double denominator = a - 2 * b + c;
            double delta = denominator < 0 ? Math.Clamp(.5 * (a - c) / denominator, -.5, .5) : 0;
            peaks.Add(((i + delta) * binHz, value));
        }
        double tolerance = Math.Max(8, binHz * 1.1);
        var pairs = new List<(int Low, int High, double Score, AutoTuneCandidate Candidate)>();
        for (int l = 0; l < peaks.Count; l++)
        {
            var low = peaks[l];
            if (low.Hz < minHz || low.Hz > maxHz) continue;
            for (int h = 0; h < peaks.Count; h++)
            {
                var high = peaks[h];
                double error = Math.Abs(high.Hz - low.Hz - shiftHz);
                if (error > tolerance) continue;
                if (Math.Min(low.Amplitude, high.Amplitude) < .02 * Math.Max(low.Amplitude, high.Amplitude)) continue;
                double score = Math.Sqrt(low.Amplitude * high.Amplitude) * (1 - .5 * error / tolerance);
                double snr = 20 * Math.Log10(Math.Min(low.Amplitude, high.Amplitude) / noise);
                pairs.Add((l, h, score, new((low.Hz + high.Hz - shiftHz) / 2,
                    Math.Clamp((snr - 10) / 20, 0, 1) * (1 - .5 * error / tolerance), snr)));
            }
        }
        var used = new HashSet<int>();
        foreach (var pair in pairs.OrderByDescending(p => p.Score))
        {
            if (found.Count >= max) break;
            if (used.Contains(pair.Low) || used.Contains(pair.High)) continue;
            used.Add(pair.Low); used.Add(pair.High);
            found.Add(pair.Candidate);
        }
        return found;
    }
}
