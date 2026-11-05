using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Wpf;

public enum WaterfallPalette { Classic, Contrast }

/// <summary>Istoric spectral complet, proiectat la afisare; firul audio doar adauga cadre intr-o coada limitata.</summary>
public sealed class WaterfallControl : FrameworkElement
{
    private const int MaxQueue = 64;
    private const double LeftMargin = 38, RightMargin = 48, AxisHeight = 22;
    private static DependencyProperty Register(string name, Type type, object value) => DependencyProperty.Register(
        name, type, typeof(WaterfallControl), new FrameworkPropertyMetadata(value, Changed));
    public static readonly DependencyProperty MarkFrequencyHzProperty = Register(nameof(MarkFrequencyHz), typeof(double), 2125d);
    public static readonly DependencyProperty ReverseProperty = Register(nameof(Reverse), typeof(bool), false);
    public static readonly DependencyProperty ShiftHzProperty = Register(nameof(ShiftHz), typeof(double), 170d);
    public static readonly DependencyProperty MinFrequencyHzProperty = Register(nameof(MinFrequencyHz), typeof(double), 0d);
    public static readonly DependencyProperty MaxFrequencyHzProperty = Register(nameof(MaxFrequencyHz), typeof(double), 4000d);
    public static readonly DependencyProperty BinHzProperty = Register(nameof(BinHz), typeof(double), 15.625d);
    public static readonly DependencyProperty RangeDbProperty = Register(nameof(RangeDb), typeof(double), 40d);
    public static readonly DependencyProperty FloorDbProperty = Register(nameof(FloorDb), typeof(double), -100d);
    public static readonly DependencyProperty AutoFloorProperty = Register(nameof(AutoFloor), typeof(bool), true);
    public static readonly DependencyProperty LockLevelsProperty = Register(nameof(LockLevels), typeof(bool), false);
    public static readonly DependencyProperty GammaProperty = Register(nameof(Gamma), typeof(double), 1d);
    public static readonly DependencyProperty PaletteProperty = Register(nameof(Palette), typeof(WaterfallPalette), WaterfallPalette.Contrast);
    public static readonly DependencyProperty HistorySecondsProperty = Register(nameof(HistorySeconds), typeof(double), 15d);
    public static readonly DependencyProperty IsPausedProperty = Register(nameof(IsPaused), typeof(bool), false);
    public static readonly DependencyProperty ShowSpectrumProperty = Register(nameof(ShowSpectrum), typeof(bool), true);
    public static readonly DependencyProperty FollowSignalProperty = Register(nameof(FollowSignal), typeof(bool), true);
    public static readonly DependencyProperty SnapToSignalProperty = Register(nameof(SnapToSignal), typeof(bool), false);
    public static readonly DependencyProperty SpectrumSizeProperty = Register(nameof(SpectrumSize), typeof(int), 512);
    public static readonly DependencyProperty FrequencyCommandProperty = Register(nameof(FrequencyCommand), typeof(ICommand), null!);
    public static readonly DependencyProperty QsyCommandProperty = Register(nameof(QsyCommand), typeof(ICommand), null!);

    public double MarkFrequencyHz { get => (double)GetValue(MarkFrequencyHzProperty); set => SetValue(MarkFrequencyHzProperty, value); }
    public bool Reverse { get => (bool)GetValue(ReverseProperty); set => SetValue(ReverseProperty, value); }
    public double ShiftHz { get => (double)GetValue(ShiftHzProperty); set => SetValue(ShiftHzProperty, value); }
    public double MinFrequencyHz { get => (double)GetValue(MinFrequencyHzProperty); set => SetValue(MinFrequencyHzProperty, value); }
    public double MaxFrequencyHz { get => (double)GetValue(MaxFrequencyHzProperty); set => SetValue(MaxFrequencyHzProperty, value); }
    public double BinHz { get => (double)GetValue(BinHzProperty); set => SetValue(BinHzProperty, value); }
    public double RangeDb { get => (double)GetValue(RangeDbProperty); set => SetValue(RangeDbProperty, value); }
    public double FloorDb { get => (double)GetValue(FloorDbProperty); set => SetValue(FloorDbProperty, value); }
    public bool AutoFloor { get => (bool)GetValue(AutoFloorProperty); set => SetValue(AutoFloorProperty, value); }
    public bool LockLevels { get => (bool)GetValue(LockLevelsProperty); set => SetValue(LockLevelsProperty, value); }
    public double Gamma { get => (double)GetValue(GammaProperty); set => SetValue(GammaProperty, value); }
    public WaterfallPalette Palette { get => (WaterfallPalette)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }
    public double HistorySeconds { get => (double)GetValue(HistorySecondsProperty); set => SetValue(HistorySecondsProperty, value); }
    public bool IsPaused { get => (bool)GetValue(IsPausedProperty); set => SetValue(IsPausedProperty, value); }
    public bool ShowSpectrum { get => (bool)GetValue(ShowSpectrumProperty); set => SetValue(ShowSpectrumProperty, value); }
    public bool FollowSignal { get => (bool)GetValue(FollowSignalProperty); set => SetValue(FollowSignalProperty, value); }
    public bool SnapToSignal { get => (bool)GetValue(SnapToSignalProperty); set => SetValue(SnapToSignalProperty, value); }
    public int SpectrumSize { get => (int)GetValue(SpectrumSizeProperty); set => SetValue(SpectrumSizeProperty, value); }
    public ICommand? FrequencyCommand { get => (ICommand?)GetValue(FrequencyCommandProperty); set => SetValue(FrequencyCommandProperty, value); }
    public ICommand? QsyCommand { get => (ICommand?)GetValue(QsyCommandProperty); set => SetValue(QsyCommandProperty, value); }
    public event Action<double>? FrequencySelected;
    public event Action<int>? SpectrumSizeChanged;
    public event Action<string>? Info;

    private readonly ConcurrentQueue<SpectrumFrame> _queue = new();
    private readonly SpectralHistory _history = new();
    private readonly Dictionary<SpectrumFrame, float[]> _projection = new();
    private readonly Dictionary<string, FormattedText> _texts = new();
    private SpectralSnapshot? _paused;
    private SpectrumFrame? _latest;
    private WriteableBitmap? _bitmap;
    private float[] _power = [];
    private int[] _pixels = [], _palette = BuildPalette(WaterfallPalette.Contrast, 1);
    private Brush[] _paletteBrushes = [];
    private int _pending, _dropped, _width, _height, _wheel;
    private long _legacySequence;
    private double _step, _firstHz, _autoFloor = double.NaN, _lastFloorTime, _pausedFloor;
    private long _floorEpoch = -1;
    private bool _dirty = true, _hooked;
    private Point? _hover, _pan;
    private double _panMin, _panSpan;
    private static readonly Pen MarkPen = Pen(Colors.Tomato, 1.5);
    private static readonly Pen SpacePen = Pen(Colors.Orange, 1.5);
    private static readonly Pen GreyPen = Pen(Color.FromRgb(75, 85, 100), 1);
    private static readonly Pen SpectrumPen = Pen(Colors.LightSkyBlue, 1);
    private static readonly Pen BoundaryPen = Pen(Colors.Orange, 1);
    private static readonly Brush Back = Brush(Color.FromRgb(14, 19, 28));
    private static readonly Brush LabelBack = Brush(Color.FromArgb(210, 0, 0, 0));
    private static readonly Typeface Font = new("Segoe UI");

    public WaterfallControl()
    {
        _paletteBrushes = PaletteBrushes(_palette);
        ClipToBounds = true; Focusable = true; Cursor = Cursors.Cross;
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
        SizeChanged += (_, _) => { _bitmap = null; _dirty = true; };
    }
    public double CurrentAutoFloorDb => _autoFloor;
    public double EffectiveFloorDb => AutoFloor
        ? IsPaused && _paused != null ? _pausedFloor : double.IsNaN(_autoFloor) ? FloorDb : _autoFloor - 3
        : FloorDb;
    public (double MinDb, double MaxDb)? CurrentLevelRangeDb { get; private set; }
    public int DroppedFrames => Volatile.Read(ref _dropped);
    public int HistoryCount => _history.Count;
    private double SpanHz => Math.Clamp(Finite(MaxFrequencyHz - MinFrequencyHz, 4000), 100, 4000);
    private double Seconds => Math.Clamp(Finite(HistorySeconds, 15), 8, 30);
    private Rect Plot => new(LeftMargin, ShowSpectrum ? 48 : 0,
        Math.Max(1, ActualWidth - LeftMargin - RightMargin), Math.Max(1, ActualHeight - AxisHeight - (ShowSpectrum ? 48 : 0)));
    private static double Finite(double x, double fallback) => double.IsFinite(x) ? x : fallback;

    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var w = (WaterfallControl)owner;
        if (e.Property == IsPausedProperty)
        {
            w._paused = (bool)e.NewValue ? w._history.Snapshot() : null;
            w._pausedFloor = double.IsNaN(w._autoFloor) ? w.FloorDb : w._autoFloor - 3;
        }
        if (e.Property == GammaProperty || e.Property == PaletteProperty)
        {
            w._palette = BuildPalette(w.Palette, w.Gamma);
            w._paletteBrushes = PaletteBrushes(w._palette);
        }
        if (e.Property == SpectrumSizeProperty)
        {
            int size = (int)e.NewValue;
            if (size is 512 or 1024) w.SpectrumSizeChanged?.Invoke(size);
        }
        if (e.Property == AutoFloorProperty && !(bool)e.NewValue && double.IsFinite(w._autoFloor))
            w.SetCurrentValue(FloorDbProperty, w.IsPaused ? w._pausedFloor : w._autoFloor - 3);
        if (e.Property == MarkFrequencyHzProperty || e.Property == ReverseProperty)
        {
            if (w.FollowSignal && w.SpanHz < 3999) w.CenterOnSignal();
            w.InvalidateVisual();
            return;
        }
        if (e.Property == MinFrequencyHzProperty || e.Property == MaxFrequencyHzProperty)
        {
            w._projection.Clear(); w._bitmap = null;
        }
        w._dirty = true;
        w.InvalidateVisual();
    }

    public void PushFrame(SpectrumFrame frame)
    {
        _queue.Enqueue(frame);
        Interlocked.Increment(ref _pending);
        while (Volatile.Read(ref _pending) > MaxQueue && _queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _pending); Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Adaptor pentru gazde vechi; fara timestamp-uri sursa nu se pot identifica golurile audio.</summary>
    public void PushRow(float[] magnitudes)
    {
        long sequence = Interlocked.Increment(ref _legacySequence);
        PushFrame(new(0, sequence, sequence * SpectrumAnalyzer.RowSeconds, BinHz, magnitudes));
    }

    public void Clear()
    {
        Dispatcher.VerifyAccess();
        while (_queue.TryDequeue(out _)) Interlocked.Decrement(ref _pending);
        _history.Clear(); _paused = null; _latest = null; _projection.Clear();
        if (IsPaused) _paused = _history.Snapshot();
        if (!LockLevels) _autoFloor = double.NaN;
        _floorEpoch = -1;
        CurrentLevelRangeDb = null; Interlocked.Exchange(ref _dropped, 0);
        _dirty = true; InvalidateVisual();
    }

    public void MarkBoundary(string label)
    {
        Dispatcher.VerifyAccess();
        ProcessPendingFrames();
        _history.Mark(label); _dirty = true; InvalidateVisual();
    }

    public void SetViewport(double min, double span)
    {
        span = Math.Clamp(Finite(span, 4000), 100, 4000);
        min = Math.Clamp(Finite(min, 0), 0, 4000 - span);
        SetCurrentValue(MinFrequencyHzProperty, min);
        SetCurrentValue(MaxFrequencyHzProperty, min + span);
    }
    public void RestoreHeldLevel(double floor)
    {
        _autoFloor = Math.Clamp(Finite(floor, -100), -160, 0) + 3;
        SetCurrentValue(FloorDbProperty, _autoFloor - 3);
        _dirty = true; InvalidateVisual();
    }
    public void FullView() => SetViewport(0, 4000);
    public void CenterOnSignal()
    {
        double center = MarkFrequencyHz + (Reverse ? -ShiftHz : ShiftHz) / 2;
        double span = SpanHz >= 3999 ? 800 : SpanHz;
        SetViewport(center - span / 2, span);
    }
    public void Zoom(double factor, double anchorX)
    {
        var p = Plot;
        double part = Math.Clamp((anchorX - p.Left) / p.Width, 0, 1);
        double anchor = MinFrequencyHz + part * SpanHz;
        double span = Math.Clamp(SpanHz * factor, 100, 4000);
        SetCurrentValue(FollowSignalProperty, false);
        SetViewport(anchor - part * span, span);
    }

    private void Hook(bool on)
    {
        if (on && !_hooked) CompositionTarget.Rendering += Rendering;
        if (!on && _hooked) CompositionTarget.Rendering -= Rendering;
        _hooked = on;
    }
    private void Rendering(object? sender, EventArgs args)
    {
        ProcessPendingFrames();
        if (_dirty) InvalidateVisual();
    }

    // Apelat pe UI, cu buget fix: un fisier rapid nu poate tine firul intr-o bucla nelimitata.
    internal void ProcessPendingFrames()
    {
        bool added = false;
        for (int i = 0; i < MaxQueue && _queue.TryDequeue(out var frame); i++)
        {
            Interlocked.Decrement(ref _pending);
            if (!_history.Add(frame)) continue;
            _latest = frame; added = true;
        }
        if (added && _latest != null)
        {
            UpdateFloor(_latest);
            if (!IsPaused) _dirty = true;
        }
    }

    private void UpdateFloor(SpectrumFrame frame)
    {
        int first = Math.Max(1, (int)(250 / frame.BinHz));
        int last = Math.Min(frame.Magnitudes.Length - 1, (int)(3000 / frame.BinHz));
        if (first >= last) return;
        var db = frame.Magnitudes[first..last].Select(a => 20 * Math.Log10(Math.Max(1e-9, double.IsFinite(a) ? a : 1e-9))).Order().ToArray();
        double target = db[(int)(.3 * (db.Length - 1))];
        double elapsed = _floorEpoch == frame.Epoch ? Math.Clamp(frame.AudioSeconds - _lastFloorTime, .001, .25) : SpectrumAnalyzer.RowSeconds;
        if (double.IsNaN(_autoFloor)) _autoFloor = target;
        else if (!LockLevels) _autoFloor += (1 - Math.Exp(-elapsed / 2)) * (target - _autoFloor);
        _floorEpoch = frame.Epoch; _lastFloorTime = frame.AudioSeconds;

        if (AutoFloor && !IsPaused) SetCurrentValue(FloorDbProperty, Math.Clamp(_autoFloor - 3, -160, 0));
    }

    private SpectralSnapshot Snapshot => _paused ?? _history.Snapshot();

    private void Paint(SpectralSnapshot snapshot, Rect plot)
    {
        double step = snapshot.Rows.Length == 0 ? 15.625 : snapshot.Rows.Min(r => r.Frame.BinHz);
        double first = Math.Floor(MinFrequencyHz / step) * step;
        int width = Math.Clamp((int)Math.Ceiling((MaxFrequencyHz - first) / step) + 1, 2, 514);
        int height = Math.Clamp((int)Math.Ceiling(plot.Height * VisualTreeHelper.GetDpi(this).DpiScaleY), 1, 1024);
        if (_bitmap == null || _width != width || _height != height || _step != step || _firstHz != first)
        {
            _width = width; _height = height; _step = step; _firstHz = first;
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            _pixels = new int[width * height]; _power = new float[width * height];
            _projection.Clear();
        }
        Array.Clear(_power); Array.Clear(_pixels);
        if (_projection.Count > 2000) _projection.Clear();
        foreach (var row in snapshot.Rows)
        {
            int y = (int)Math.Floor((snapshot.EndTime - row.Time) / Seconds * height);
            if (y < 0 || y >= height) continue;
            if (!_projection.TryGetValue(row.Frame, out var values))
            {
                values = new float[width];
                for (int x = 0; x < width; x++) values[x] = (float)Math.Pow(10, SpectralHistory.SampleDb(row.Frame, first + x * step) / 10);
                _projection[row.Frame] = values;
            }
            int offset = y * width;
            // Max-hold numai la comprimarea temporala pe pixeli; semnalele scurte raman vizibile.
            for (int x = 0; x < width; x++) _power[offset + x] = Math.Max(_power[offset + x], values[x]);
        }
        // Umple doar intervalele reale dintre cadre vecine, nu golurile de coada/sursa.
        int previous = -1;
        foreach (var row in snapshot.Rows)
        {
            int y = (int)Math.Floor((snapshot.EndTime - row.Time) / Seconds * height);
            if (y < 0 || y >= height) continue;
            if (previous >= 0 && row.GapStart == null)
                for (int fill = y + 1; fill < previous; fill++)
                    Array.Copy(_power, y * width, _power, fill * width, width);
            previous = y;
        }
        double floor = Math.Clamp(Finite(EffectiveFloorDb, -100), -180, 0);
        double range = Math.Clamp(Finite(RangeDb, 40), 10, 120);
        for (int i = 0; i < _power.Length; i++)
        {
            if (_power[i] <= 0) continue;
            double db = 10 * Math.Log10(_power[i]);
            int index = (int)Math.Clamp((db - floor) / range * 255, 0, 255);
            _pixels[i] = _palette[index];
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
        _dirty = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 100 || ActualHeight < 70) return;
        Rect plot = Plot;
        var snapshot = Snapshot;
        if (snapshot.Rows.LastOrDefault()?.Frame is { } visible)
        {
            int first = Math.Clamp((int)(MinFrequencyHz / visible.BinHz), 0, visible.Magnitudes.Length - 1);
            int last = Math.Clamp((int)(MaxFrequencyHz / visible.BinHz), first, visible.Magnitudes.Length - 1);
            var values = visible.Magnitudes[first..(last + 1)].Select(x => 20 * Math.Log10(Math.Max(1e-9, double.IsFinite(x) ? x : 1e-9)));
            CurrentLevelRangeDb = (values.Min(), values.Max());
        }
        dc.DrawRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_dirty || _bitmap == null) Paint(snapshot, plot);
        dc.PushClip(new RectangleGeometry(plot));
        if (_bitmap != null)
            dc.DrawImage(_bitmap, new Rect(HzToX(_firstHz - _step / 2), plot.Top,
                (_width * _step) / SpanHz * plot.Width, plot.Height));
        var labels = new List<(double Y, string Text, Brush Color)>();
        foreach (var row in snapshot.Rows.Where(r => r.GapStart != null))
        {
            double y = plot.Top + (snapshot.EndTime - row.Time) / Seconds * plot.Height;
            double end = plot.Top + (snapshot.EndTime - row.GapStart!.Value) / Seconds * plot.Height;
            if (end < plot.Top || y > plot.Bottom) continue;
            double gapTop = Math.Max(plot.Top, y + plot.Height / Math.Max(1, _height));
            dc.DrawRectangle(Brushes.Black, null, new Rect(plot.Left, gapTop, plot.Width, Math.Max(0, Math.Min(end, plot.Bottom) - gapTop)));
            dc.DrawLine(BoundaryPen, new Point(plot.Left, y), new Point(plot.Right, y));
            labels.Add((y, $"gol {row.Time - row.GapStart.Value:0.00} s", Brushes.Orange));
        }
        foreach (var boundary in snapshot.Boundaries)
        {
            double y = plot.Top + (snapshot.EndTime - boundary.Time) / Seconds * plot.Height;
            dc.DrawLine(GreyPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (y >= plot.Top && y < plot.Bottom) labels.Add((y, boundary.Label, Brushes.LightGray));
        }
        // Etichetele de eveniment apropiate se combina, in loc sa se suprapuna.
        var orderedLabels = labels.OrderBy(label => label.Y).ToArray();
        for (int i = 0; i < orderedLabels.Length; i++)
        {
            var label = orderedLabels[i];
            string text = label.Text;
            while (i + 1 < orderedLabels.Length && orderedLabels[i + 1].Y - label.Y < 13)
                text += " | " + orderedLabels[++i].Text;
            DrawText(dc, text, plot.Left + 4, label.Y + 1, label.Color);
        }
        dc.Pop();
        DrawSpectrum(dc, snapshot, plot);
        DrawAxes(dc, plot);
        double space = MarkFrequencyHz + (Reverse ? -ShiftHz : ShiftHz);
        foreach (var item in new[] { (MarkFrequencyHz, "M", MarkPen), (space, "S", SpacePen) })
        {
            double x = HzToX(item.Item1);
            if (x < plot.Left || x > plot.Right) continue;
            dc.DrawLine(item.Item3, new Point(x, 0), new Point(x, plot.Bottom));
            DrawText(dc, item.Item2, x + 3, 0, item.Item3.Brush);
        }
        if (IsPaused) DrawText(dc, "PAUZA IMAGINE", plot.Right - 105, plot.Top + 2, Brushes.Yellow);
        if (_hover is { } point && plot.Contains(point))
        {
            double hz = XToHz(point.X);
            dc.DrawLine(GreyPen, new Point(point.X, plot.Top), new Point(point.X, plot.Bottom));
            double age = (point.Y - plot.Top) / plot.Height * Seconds;
            var frame = SpectralHistory.FrameAtAge(snapshot, age);
            string text = $"{hz:0.0} Hz  -{age:0.00}s" + (frame != null ? $"  {SpectralHistory.SampleDb(frame, hz):0.0} dBFS" : "  fara date");
            double x = Math.Clamp(point.X + 6, plot.Left, Math.Max(plot.Left, plot.Right - 260));
            dc.DrawRectangle(LabelBack, null, new Rect(x, plot.Bottom - 20, 260, 19));
            DrawText(dc, text, x + 2, plot.Bottom - 19, Brushes.White);
        }
    }

    private void DrawSpectrum(DrawingContext dc, SpectralSnapshot snapshot, Rect plot)
    {
        if (!ShowSpectrum || snapshot.Rows.LastOrDefault()?.Frame is not { } frame) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            const int points = 256;
            for (int i = 0; i <= points; i++)
            {
                double hz = MinFrequencyHz + SpanHz * i / points;
                double level = Math.Clamp((SpectralHistory.SampleDb(frame, hz) - Finite(EffectiveFloorDb, -100)) / Math.Clamp(Finite(RangeDb, 40), 10, 120), 0, 1);
                var point = new Point(HzToX(hz), 43 - level * 38);
                if (i == 0) path.BeginFigure(point, false, false); else path.LineTo(point, true, false);
            }
        }
        geometry.Freeze(); dc.DrawGeometry(null, SpectrumPen, geometry);
        dc.DrawLine(GreyPen, new Point(plot.Left, 46), new Point(plot.Right, 46));
    }

    private void DrawAxes(DrawingContext dc, Rect plot)
    {
        int tick = SpanHz <= 500 ? 50 : SpanHz <= 1200 ? 100 : 500;
        for (double hz = Math.Ceiling(MinFrequencyHz / tick) * tick; hz <= MaxFrequencyHz; hz += tick)
        {
            double x = HzToX(hz);
            dc.DrawLine(GreyPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(dc, hz.ToString("0", CultureInfo.InvariantCulture), Math.Clamp(x - 15, plot.Left, plot.Right - 30), plot.Bottom + 4, Brushes.LightGray);
        }
        int step = Seconds <= 10 ? 2 : Seconds <= 15 ? 5 : 10;
        for (int age = 0; age <= Seconds; age += step)
            DrawText(dc, $"-{age}s", 2, Math.Clamp(plot.Top + age / Seconds * plot.Height - 5, plot.Top, plot.Bottom - 12), Brushes.Gray);
        double floor = EffectiveFloorDb;
        for (int i = 0; i < 64; i++)
        {
            dc.DrawRectangle(_paletteBrushes[255 - i * 255 / 63], null,
                new Rect(plot.Right + 4, plot.Top + i / 64.0 * plot.Height, 7, plot.Height / 64 + 1));
        }
        DrawText(dc, $"{floor + RangeDb:0}", plot.Right + 14, plot.Top, Brushes.Gray);
        DrawText(dc, $"{floor:0}", plot.Right + 14, plot.Bottom - 14, Brushes.Gray);
        DrawText(dc, "dBFS", plot.Right + 13, plot.Top + plot.Height / 2, Brushes.Gray);
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string key = text + "/" + dpi + "/" + brush;
        if (!_texts.TryGetValue(key, out var formatted))
        {
            if (_texts.Count > 256) _texts.Clear();
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, brush, dpi);
            _texts[key] = formatted;
        }
        dc.DrawText(formatted, new Point(x, y));
    }
    private double HzToX(double hz) => Plot.Left + (hz - MinFrequencyHz) / SpanHz * Plot.Width;
    private double XToHz(double x) => MinFrequencyHz + (x - Plot.Left) / Plot.Width * SpanHz;

    private void Select(double hz)
    {
        hz = Math.Clamp(Math.Round(hz, 1), 200, 3800);
        FrequencySelected?.Invoke(hz);
        if (FrequencyCommand?.CanExecute(hz) == true) FrequencyCommand.Execute(hz);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var point = e.GetPosition(this);
        if (!Plot.Contains(point)) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { BeginPan(point); e.Handled = true; return; }
        double hz = XToHz(point.X);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (QsyCommand?.CanExecute(hz) == true) QsyCommand.Execute(hz);
            else Info?.Invoke("QSY indisponibil: conectati statia. Acordul decodorului nu a fost schimbat.");
        }
        else
        {
            if (SnapToSignal && Snapshot.Rows.LastOrDefault()?.Frame is { } frame)
            {
                var candidate = AutoTune.FindCandidate(Array.ConvertAll(frame.Magnitudes, x => (double)x), frame.BinHz,
                    Math.Max(200, hz - 250), Math.Min(3630, hz + 250));
                if (candidate is { } pair)
                {
                    hz = pair.LowerToneHz + (Reverse ? ShiftHz : 0);
                    Info?.Invoke($"Pereche RTTY: ton slab +{pair.WeakToneSnrDb:0} dB peste zgomot; scor spectral {pair.Confidence:0.00}/1. NORM/REV ramane ales manual.");
                }
                else { Info?.Invoke("Pereche RTTY neconfirmata langa cursor; acord manual."); }
            }
            Select(hz);
        }
        e.Handled = true;
    }
    private void BeginPan(Point point)
    {
        _pan = point; _panMin = MinFrequencyHz; _panSpan = SpanHz;
        SetCurrentValue(FollowSignalProperty, false); CaptureMouse();
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && Plot.Contains(e.GetPosition(this))) { BeginPan(e.GetPosition(this)); e.Handled = true; }
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_pan != null) { _pan = null; ReleaseMouseCapture(); e.Handled = true; }
        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hover = e.GetPosition(this);
        if (_pan is { } start) SetViewport(_panMin - (_hover.Value.X - start.X) / Plot.Width * _panSpan, _panSpan);
        InvalidateVisual();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { _pan = null; base.OnLostMouseCapture(e); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = null; InvalidateVisual(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) Zoom(e.Delta > 0 ? .8 : 1.25, e.GetPosition(this).X);
        else
        {
            _wheel += e.Delta;
            int ticks = _wheel / 120;
            _wheel -= ticks * 120;
            if (ticks != 0) Select(MarkFrequencyHz + ticks * (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10 : 1));
        }
        e.Handled = true;
    }
    public BitmapSource Capture()
    {
        Dispatcher.VerifyAccess(); ProcessPendingFrames();
        if (_dirty) InvalidateVisual();
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var result = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY)), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        result.Render(this); result.Freeze(); return result;
    }

    internal static int[] BuildPalette(WaterfallPalette palette, double gamma)
    {
        var colors = new int[256];
        (double T, Color C)[] stops = [(0, Colors.Black), (.25, Colors.DarkBlue), (.5, Colors.Cyan), (.75, Colors.Yellow), (1, Colors.White)];
        for (int i = 0; i < colors.Length; i++)
        {
            double t = Math.Pow(i / 255.0, Math.Clamp(Finite(gamma, 1), .2, 3));
            Color c;
            if (palette == WaterfallPalette.Classic)
            {
                double hue = 220 * (1 - Math.Pow(t, 1.6)) / 60;
                double light = .15 + .75 * t, chroma = 1 - Math.Abs(2 * light - 1), x = chroma * (1 - Math.Abs(hue % 2 - 1)), m = light - chroma / 2;
                var rgb = hue < 1 ? (chroma, x, 0d) : hue < 2 ? (x, chroma, 0d) : hue < 3 ? (0d, chroma, x) : (0d, x, chroma);
                c = Color.FromRgb((byte)Math.Round((rgb.Item1 + m) * 255), (byte)Math.Round((rgb.Item2 + m) * 255), (byte)Math.Round((rgb.Item3 + m) * 255));
            }
            else
            {
                int k = Math.Min(3, (int)(t * 4)); double f = (t - stops[k].T) / .25;
                c = Color.FromRgb((byte)Math.Round(stops[k].C.R + f * (stops[k + 1].C.R - stops[k].C.R)),
                    (byte)Math.Round(stops[k].C.G + f * (stops[k + 1].C.G - stops[k].C.G)), (byte)Math.Round(stops[k].C.B + f * (stops[k + 1].C.B - stops[k].C.B)));
            }
            colors[i] = c.R << 16 | c.G << 8 | c.B;
        }
        return colors;
    }
    private static Brush[] PaletteBrushes(int[] colors) => colors.Select(c => Brush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c))).ToArray();
    private static Pen Pen(Color color, double width) { var p = new Pen(Brush(color), width); p.Freeze(); return p; }
    private static Brush Brush(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
}
