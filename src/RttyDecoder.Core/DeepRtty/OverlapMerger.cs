namespace RttyDecoder.Core.DeepRtty;

/// <summary>Un caracter decodat de CTC: pozitia absoluta in stream (esantioane) si increderea.</summary>
public readonly record struct CtcToken(long Sample, int Index, float Score);

/// <summary>Greedy CTC pe primul element de batch, cu pozitia (centrul run-ului) si probabilitatea maxima.</summary>
public static class CtcTokens
{
    public readonly record struct FrameToken(float Frame, int Index, float Score);

    public static List<FrameToken> Greedy(float[] output, long[] dims, int blankIndex)
    {
        var tokens = new List<FrameToken>();
        if (dims.Length < 2) return tokens;
        int classes = (int)dims[^1];
        int frames = (int)dims[^2];
        if (classes <= 0 || frames <= 0 || output.Length < frames * classes) return tokens;

        int runIndex = -1;
        int runStart = 0;
        float runPeak = float.NegativeInfinity;

        void Close(int stop)
        {
            if (runIndex >= 0 && runIndex != blankIndex)
                tokens.Add(new FrameToken((runStart + stop - 1) * 0.5f, runIndex, MathF.Exp(runPeak)));
        }

        for (int t = 0; t < frames; t++)
        {
            int offset = t * classes;
            int best = 0;
            float bestValue = float.NegativeInfinity;
            for (int c = 0; c < classes; c++)
            {
                float value = output[offset + c];
                if (value > bestValue)
                {
                    bestValue = value;
                    best = c;
                }
            }
            if (best != runIndex)
            {
                Close(t);
                runIndex = best;
                runStart = t;
                runPeak = bestValue;
            }
            else if (bestValue > runPeak)
            {
                runPeak = bestValue;
            }
        }
        Close(frames);
        return tokens;
    }
}

/// <summary>
/// Imbina tokenii unor ferestre suprapuse intr-un singur stream, fiecare caracter o singura data
/// (metoda receptorului streaming DeepRTTY). Din fiecare fereastra conteaza doar partea "sigura",
/// fara <c>edgeGuard</c> esantioane la marginile interioare. In zona comuna a doua ferestre
/// consecutive tokenii sunt aliniati in timp: din pereche ramane cel cu increderea mai mare, iar un
/// token fara pereche e pastrat doar de fereastra careia ii apartine jumatatea respectiva a zonei.
/// Ce intoarce <see cref="Push"/> e definitiv.
/// </summary>
public sealed class OverlapMerger
{
    private readonly long _edgeGuard;
    private readonly double _tolerance;
    private List<CtcToken> _pending = new();
    private double _pendingReliableEnd = double.NegativeInfinity;
    private int _windows;

    public OverlapMerger(long edgeGuard, double tolerance)
    {
        _edgeGuard = edgeGuard;
        _tolerance = tolerance;
    }

    /// <summary>Tot ce e inainte de acest esantion a fost deja returnat de Push.</summary>
    public double CommittedUntil { get; private set; } = double.NegativeInfinity;

    public List<CtcToken> Push(long windowStart, long windowLength, IReadOnlyList<CtcToken> windowTokens)
    {
        long reliableStart = windowStart + (_windows > 0 ? _edgeGuard : 0L);
        var tokens = windowTokens.Where(t => t.Sample >= reliableStart).ToList();
        _windows++;
        if (_windows == 1)
        {
            _pending = tokens;
            _pendingReliableEnd = windowStart + windowLength - _edgeGuard;
            return new List<CtcToken>();
        }

        double zoneStart = Math.Max(reliableStart, CommittedUntil);
        double zoneEnd = _pendingReliableEnd;
        if (zoneEnd < zoneStart)
        {
            // Fara suprapunere sigura: fiecare fereastra e crezuta pana la mijlocul golului.
            zoneStart = 0.5 * (zoneStart + zoneEnd);
            zoneEnd = zoneStart;
        }
        double middle = 0.5 * (zoneStart + zoneEnd);
        bool InZone(CtcToken t) => t.Sample >= zoneStart && t.Sample <= zoneEnd;

        var committed = _pending.Where(t => t.Sample < zoneStart).ToList();
        var merged = new List<CtcToken>();
        foreach (var (old, nou) in Align(_pending.Where(InZone).ToList(), tokens.Where(InZone).ToList()))
        {
            if (old is { } o && nou is { } n) merged.Add(o.Score >= n.Score ? o : n);
            else if (old is { } o2 && o2.Sample < middle) merged.Add(o2);
            else if (nou is { } n2 && n2.Sample >= middle) merged.Add(n2);
        }
        // Tokenii fara pereche dintre doua perechi pot veni in ordine arbitrara (sortare stabila).
        committed.AddRange(merged.OrderBy(t => t.Sample));

        CommittedUntil = zoneEnd;
        _pending = tokens.Where(t => t.Sample > zoneEnd).ToList();
        _pendingReliableEnd = windowStart + windowLength - _edgeGuard;
        return committed;
    }

    /// <summary>
    /// Aliniere in timp a doua secvente scurte. Perechile sunt permise doar in toleranta;
    /// caracterele egale costa mai putin decat substitutiile, ambele mai putin decat doi tokeni
    /// nepereche, deci un token parazit nu poate "fura" perechea unuia real.
    /// </summary>
    private List<(CtcToken?, CtcToken?)> Align(List<CtcToken> a, List<CtcToken> b)
    {
        int n = a.Count, m = b.Count;
        const double gap = 1.0;
        var cost = new double[n + 1, m + 1];
        var move = new int[n + 1, m + 1]; // 0 pereche, 1 doar a, 2 doar b
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= m; j++)
                cost[i, j] = double.PositiveInfinity;
        for (int i = 0; i <= n; i++) { cost[i, 0] = i * gap; move[i, 0] = 1; }
        for (int j = 0; j <= m; j++) { cost[0, j] = j * gap; move[0, j] = 2; }
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double best = cost[i - 1, j] + gap;
                int step = 1;
                if (cost[i, j - 1] + gap < best)
                {
                    best = cost[i, j - 1] + gap;
                    step = 2;
                }
                double dt = Math.Abs(a[i - 1].Sample - b[j - 1].Sample);
                if (dt <= _tolerance)
                {
                    double pair = cost[i - 1, j - 1] + 0.5 * dt / _tolerance +
                                  (a[i - 1].Index == b[j - 1].Index ? 0.0 : 0.9);
                    if (pair <= best)
                    {
                        best = pair;
                        step = 0;
                    }
                }
                cost[i, j] = best;
                move[i, j] = step;
            }
        }
        var pairs = new List<(CtcToken?, CtcToken?)>();
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            switch (move[x, y])
            {
                case 0: pairs.Add((a[x - 1], b[y - 1])); x--; y--; break;
                case 1: pairs.Add((a[x - 1], null)); x--; break;
                default: pairs.Add((null, b[y - 1])); y--; break;
            }
        }
        pairs.Reverse();
        return pairs;
    }
}
