namespace RttyDecoder.Core.Dsp;

public sealed record SpectralRow(SpectrumFrame Frame, double Time, double? GapStart);
public sealed record SpectralBoundary(double Time, string Label);
public sealed record SpectralSnapshot(SpectralRow[] Rows, SpectralBoundary[] Boundaries, double EndTime);

/// <summary>Istoric complet in frecventa, independent de zoom/paleta. Folosit pe un singur fir UI.</summary>
public sealed class SpectralHistory
{
    public const double CapacitySeconds = 30;
    private readonly Queue<SpectralRow> _rows = new();
    private readonly List<SpectralBoundary> _boundaries = new();
    private SpectralRow? _last;
    private double _offset;
    private SpectralSnapshot? _snapshot;
    public int Count => _rows.Count;
    public double EndTime => _last?.Time ?? 0;

    public bool Add(SpectrumFrame frame)
    {
        if (!double.IsFinite(frame.AudioSeconds) || !double.IsFinite(frame.BinHz) || frame.BinHz <= 0 || frame.Magnitudes.Length < 2) return false;
        bool newEpoch = _last == null || _last.Frame.Epoch != frame.Epoch;
        if (!newEpoch && (frame.Sequence <= _last!.Frame.Sequence || frame.AudioSeconds <= _last.Frame.AudioSeconds)) return false;
        if (newEpoch)
        {
            _offset = (_last?.Time + SpectrumAnalyzer.RowSeconds ?? frame.AudioSeconds) - frame.AudioSeconds;
            if (_last != null) Mark("START / sursa noua");
        }
        double time = _offset + frame.AudioSeconds;
        double? gap = !newEpoch && time - _last!.Time > SpectrumAnalyzer.RowSeconds * 1.5 ? _last.Time : null;
        if (!newEpoch && frame.BinHz != _last!.Frame.BinHz) Mark("FFT schimbat");
        _snapshot = null;
        _last = new(frame, time, gap);
        _rows.Enqueue(_last);
        double oldest = time - CapacitySeconds;
        while (_rows.TryPeek(out var row) && (row.Time < oldest || _rows.Count > 1024)) _rows.Dequeue();
        _boundaries.RemoveAll(b => b.Time < oldest);
        return true;
    }

    public void Mark(string label)
    {
        _snapshot = null;
        if (_boundaries.Count > 0 && Math.Abs(_boundaries[^1].Time - EndTime) < .1)
        {
            var previous = _boundaries[^1];
            if (!previous.Label.Contains(label, StringComparison.Ordinal))
                _boundaries[^1] = previous with { Label = (previous.Label + " / " + label)[..Math.Min(70, previous.Label.Length + label.Length + 3)] };
        }
        else _boundaries.Add(new(EndTime, label));
    }

    public SpectralSnapshot Snapshot() => _snapshot ??= new(_rows.ToArray(), _boundaries.ToArray(), EndTime);
    public void Clear() { _rows.Clear(); _boundaries.Clear(); _last = null; _offset = 0; _snapshot = null; }

    /// <summary>Cadrul de la timpul indicat de cursor; nu inventeaza niveluri in goluri.</summary>
    public static SpectrumFrame? FrameAtAge(SpectralSnapshot snapshot, double age)
    {
        if (!double.IsFinite(age) || age < 0 || snapshot.Rows.Length == 0) return null;
        double time = snapshot.EndTime - age;
        if (time < snapshot.Rows[0].Time - SpectrumAnalyzer.RowSeconds) return null;
        foreach (var row in snapshot.Rows)
        {
            if (row.Time < time) continue;
            if (row.GapStart is { } start && time > start && time < row.Time - SpectrumAnalyzer.RowSeconds) return null;
            return row.Frame;
        }
        return null;
    }

    /// <summary>Interpolare a puterii la frecventa exacta; centrul bin-ului nu este deplasat cu jumatate de pixel.</summary>
    public static double SampleDb(SpectrumFrame frame, double frequency)
    {
        double index = frequency / frame.BinHz;
        if (!double.IsFinite(index) || index < 0 || index > frame.Magnitudes.Length - 1) return -180;
        int k = Math.Min((int)index, frame.Magnitudes.Length - 2);
        double a = frame.Magnitudes[k], b = frame.Magnitudes[k + 1], fraction = index - k;
        if (!double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b < 0) return -180;
        return 10 * Math.Log10(Math.Max(1e-18, a * a + fraction * (b * b - a * a)));
    }
}
