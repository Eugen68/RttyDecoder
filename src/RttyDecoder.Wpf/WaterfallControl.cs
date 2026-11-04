using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RttyDecoder.Wpf;

/// <summary>
/// Waterfall RTTY: randuri de spectru (cel mai nou sus), scala de frecventa jos, markerii Mark si
/// Space. Click = acord pe Mark; rotita = +-1 Hz (Ctrl: +-10 Hz).
///
/// <see cref="PushRow"/> se poate apela de pe orice fir (firul audio); desenarea se face pe firul
/// UI, o data pe cadru. Daca vin randuri mai repede decat se pot afisa (decodare rapida dintr-un
/// fisier), cele mai vechi se arunca.
/// </summary>
public sealed class WaterfallControl : FrameworkElement
{
    private const int Rows = 256;
    private const double ScaleHeight = 18;
    private const int MaxQueue = 64;

    public static readonly DependencyProperty MarkFrequencyHzProperty = DependencyProperty.Register(
        nameof(MarkFrequencyHz), typeof(double), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(2125.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReverseProperty = DependencyProperty.Register(
        nameof(Reverse), typeof(bool), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShiftHzProperty = DependencyProperty.Register(
        nameof(ShiftHz), typeof(double), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinFrequencyHzProperty = DependencyProperty.Register(
        nameof(MinFrequencyHz), typeof(double), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WaterfallControl)d).ResetBitmap()));

    public static readonly DependencyProperty MaxFrequencyHzProperty = DependencyProperty.Register(
        nameof(MaxFrequencyHz), typeof(double), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(4000.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WaterfallControl)d).ResetBitmap()));

    /// <summary>Latimea unui bin al randurilor primite (Hz).</summary>
    public static readonly DependencyProperty BinHzProperty = DependencyProperty.Register(
        nameof(BinHz), typeof(double), typeof(WaterfallControl),
        new FrameworkPropertyMetadata(8000.0 / 512, (d, _) => ((WaterfallControl)d).ResetBitmap()));

    /// <summary>Intervalul (dB): cat de larga e scala de culoare peste podea.</summary>
    public static readonly DependencyProperty RangeDbProperty = DependencyProperty.Register(
        nameof(RangeDb), typeof(double), typeof(WaterfallControl), new PropertyMetadata(40.0, Recolor));

    /// <summary>Podeaua manuala (dB): de aici incepe culoarea (cand <see cref="AutoFloor"/> e oprit).</summary>
    public static readonly DependencyProperty FloorDbProperty = DependencyProperty.Register(
        nameof(FloorDb), typeof(double), typeof(WaterfallControl), new PropertyMetadata(-100.0, Recolor));

    /// <summary>Podeaua urmareste singura zgomotul (percentila 30 a fiecarui rand, netezita).</summary>
    public static readonly DependencyProperty AutoFloorProperty = DependencyProperty.Register(
        nameof(AutoFloor), typeof(bool), typeof(WaterfallControl), new PropertyMetadata(true, Recolor));

    /// <summary>Gama: inmulteste exponentul fix (1.6) al curbei de nuanta a paletei (ca in CwDecoder).</summary>
    public static readonly DependencyProperty GammaProperty = DependencyProperty.Register(
        nameof(Gamma), typeof(double), typeof(WaterfallControl), new PropertyMetadata(1.0, Regamma));

    private static void Recolor(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaterfallControl)d)._dirty = true;

    private static void Regamma(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (WaterfallControl)d;
        w._palette = BuildPalette((double)e.NewValue);
        w._dirty = true;
    }

    public static readonly DependencyProperty FrequencyCommandProperty = DependencyProperty.Register(
        nameof(FrequencyCommand), typeof(ICommand), typeof(WaterfallControl));

    /// <summary>Shift+click: QSY - statia se muta astfel incat semnalul de sub mouse sa ajunga pe Mark.</summary>
    public static readonly DependencyProperty QsyCommandProperty = DependencyProperty.Register(
        nameof(QsyCommand), typeof(ICommand), typeof(WaterfallControl));

    private int[] _palette = BuildPalette(1.0);
    private static readonly Pen MarkPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x3D)), 1.5));
    private static readonly Pen SpacePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x3D)), 1.5));
    private static readonly Pen HoverPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen TickPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), 1));
    private static readonly Brush ScaleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)));
    private static readonly Typeface Font = new("Segoe UI");

    private readonly ConcurrentQueue<float[]> _queue = new();
    private WriteableBitmap? _bitmap;
    private int[] _pixels = Array.Empty<int>();
    private int _width;
    private int _firstBin;
    private double _floorDb = double.NaN;      // podeaua automata
    // istoricul in dB (nu pixeli colorati): orice reglaj se aplica pe loc pe tot ce e afisat
    private float[] _db = Array.Empty<float>(); // [rand * _width + x], randul _top e cel mai nou
    private int _top;
    private int _filled;
    private bool _dirty;
    private double? _hoverX;
    private bool _renderingHooked;

    public WaterfallControl()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        Loaded += (_, _) => HookRendering(true);
        Unloaded += (_, _) => HookRendering(false);
    }

    public double MarkFrequencyHz { get => (double)GetValue(MarkFrequencyHzProperty); set => SetValue(MarkFrequencyHzProperty, value); }
    public bool Reverse { get => (bool)GetValue(ReverseProperty); set => SetValue(ReverseProperty, value); }
    public double ShiftHz { get => (double)GetValue(ShiftHzProperty); set => SetValue(ShiftHzProperty, value); }
    public double MinFrequencyHz { get => (double)GetValue(MinFrequencyHzProperty); set => SetValue(MinFrequencyHzProperty, value); }
    public double MaxFrequencyHz { get => (double)GetValue(MaxFrequencyHzProperty); set => SetValue(MaxFrequencyHzProperty, value); }
    public double BinHz { get => (double)GetValue(BinHzProperty); set => SetValue(BinHzProperty, value); }
    public double RangeDb { get => (double)GetValue(RangeDbProperty); set => SetValue(RangeDbProperty, value); }
    public double FloorDb { get => (double)GetValue(FloorDbProperty); set => SetValue(FloorDbProperty, value); }
    public bool AutoFloor { get => (bool)GetValue(AutoFloorProperty); set => SetValue(AutoFloorProperty, value); }
    public double Gamma { get => (double)GetValue(GammaProperty); set => SetValue(GammaProperty, value); }

    /// <summary>Podeaua automata curenta (dB), sau NaN pana la primul rand.</summary>
    public double CurrentAutoFloorDb => _floorDb;

    /// <summary>Nivelul semnalului din ultimul rand: (minim, maxim) in dB; null pana la primul rand.</summary>
    public (double MinDb, double MaxDb)? CurrentLevelRangeDb { get; private set; }

    /// <summary>Podeaua folosita acum (automata sau manuala).</summary>
    private double EffectiveFloor => AutoFloor ? (double.IsNaN(_floorDb) ? -100 : _floorDb - 3) : FloorDb;

    /// <summary>Comanda primeste frecventa (double, Hz) la click sau rotita.</summary>
    public ICommand? FrequencyCommand { get => (ICommand?)GetValue(FrequencyCommandProperty); set => SetValue(FrequencyCommandProperty, value); }

    /// <summary>Comanda de QSY (parametru: frecventa audio in Hz de sub mouse); fara statie nu se poate executa.</summary>
    public ICommand? QsyCommand { get => (ICommand?)GetValue(QsyCommandProperty); set => SetValue(QsyCommandProperty, value); }

    /// <summary>Click sau rotita: noua frecventa Mark ceruta (Hz).</summary>
    public event Action<double>? FrequencySelected;

    /// <summary>Adauga un rand de magnitudini liniare (de la 0 Hz, BinHz pe bin). Thread-safe.</summary>
    public void PushRow(float[] magnitudes)
    {
        _queue.Enqueue(magnitudes);
        while (_queue.Count > MaxQueue && _queue.TryDequeue(out _)) { }
    }

    public void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        _filled = 0;
        _floorDb = double.NaN;
        CurrentLevelRangeDb = null;
        Array.Clear(_pixels);
        _bitmap?.WritePixels(new Int32Rect(0, 0, _width, Rows), _pixels, _width * 4, 0);
        InvalidateVisual();
    }

    private void HookRendering(bool on)
    {
        if (on && !_renderingHooked) CompositionTarget.Rendering += OnRendering;
        else if (!on && _renderingHooked) CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = on;
    }

    private void ResetBitmap()
    {
        _bitmap = null;
        InvalidateVisual();
    }

    private void EnsureBitmap()
    {
        if (_bitmap != null) return;
        _firstBin = Math.Max(0, (int)Math.Floor(MinFrequencyHz / BinHz));
        int lastBin = Math.Max(_firstBin + 1, (int)Math.Ceiling(MaxFrequencyHz / BinHz));
        _width = lastBin - _firstBin;
        _pixels = new int[_width * Rows];
        _db = new float[_width * Rows];
        _filled = 0;
        _top = 0;
        _bitmap = new WriteableBitmap(_width, Rows, 96, 96, PixelFormats.Bgr32, null);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_queue.IsEmpty && !_dirty) return;
        EnsureBitmap();
        while (_queue.TryDequeue(out var row)) AddRow(row);
        _dirty = false;
        Paint();
        _bitmap!.WritePixels(new Int32Rect(0, 0, _width, Rows), _pixels, _width * 4, 0);
        InvalidateVisual();
    }

    /// <summary>Recoloreaza tot istoricul cu podeaua, intervalul si gama curente.</summary>
    private void Paint()
    {
        double floor = EffectiveFloor, scale = 255.0 / Math.Max(10, RangeDb);
        var pal = _palette;
        for (int r = 0; r < Rows; r++)
        {
            int dst = r * _width;
            if (r >= _filled)
            {
                Array.Clear(_pixels, dst, _width);
                continue;
            }
            int src = ((_top + r) % Rows) * _width;
            for (int x = 0; x < _width; x++)
            {
                double v = (_db[src + x] - floor) * scale;
                _pixels[dst + x] = pal[v <= 0 ? 0 : v >= 255 ? 255 : (int)v];
            }
        }
    }

    private void AddRow(float[] mag)
    {
        // randul nou sus: in inel, inaintea celui mai nou
        _top = (_top - 1 + Rows) % Rows;
        if (_filled < Rows) _filled++;
        int o = _top * _width;
        double min = double.MaxValue, max = double.MinValue;
        var sortat = new double[_width];
        for (int x = 0; x < _width; x++)
        {
            int bin = _firstBin + x;
            double db = bin < mag.Length ? 20 * Math.Log10(mag[bin] + 1e-9) : -180;
            _db[o + x] = (float)db;
            sortat[x] = db;
            if (db < min) min = db;
            if (db > max) max = db;
        }
        CurrentLevelRangeDb = (min, max);
        // podeaua automata: percentila 30 a randului, netezita intre randuri
        Array.Sort(sortat);
        double p30 = sortat[(int)(0.3 * (sortat.Length - 1))];
        _floorDb = double.IsNaN(_floorDb) ? p30 : _floorDb + 0.05 * (p30 - _floorDb);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double imgH = Math.Max(0, h - ScaleHeight);
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, imgH));
        EnsureBitmap();
        if (_bitmap != null)
        {
            // bitmap-ul incepe la _firstBin * BinHz; il pozitionam ca frecventele sa cada exact pe axa
            double x0 = HzToX(_firstBin * BinHz);
            double x1 = HzToX((_firstBin + _width) * BinHz);
            dc.DrawImage(_bitmap, new Rect(x0, 0, Math.Max(0, x1 - x0), imgH));
        }

        // scala
        dc.DrawRectangle(ScaleBrush, null, new Rect(0, imgH, w, ScaleHeight));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int f = (int)(Math.Ceiling(MinFrequencyHz / 100) * 100); f <= MaxFrequencyHz; f += 100)
        {
            double x = HzToX(f);
            bool major = f % 500 == 0;
            dc.DrawLine(TickPen, new Point(x, imgH), new Point(x, imgH + (major ? 6 : 3)));
            if (major)
            {
                var ft = new FormattedText(f.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Font, 10, TextBrush, dpi);
                dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, w - ft.Width), imgH + 5));
            }
        }

        // markerii
        double mark = MarkFrequencyHz;
        double space = mark + (Reverse ? -ShiftHz : ShiftHz);
        double xm = HzToX(mark), xs = HzToX(space);
        dc.DrawLine(MarkPen, new Point(xm, 0), new Point(xm, imgH));
        dc.DrawLine(SpacePen, new Point(xs, 0), new Point(xs, imgH));
        DrawLabel(dc, "M", xm, dpi, MarkPen.Brush);
        DrawLabel(dc, "S", xs, dpi, SpacePen.Brush);

        if (_hoverX is { } hx && hx >= 0 && hx <= w)
        {
            dc.DrawLine(HoverPen, new Point(hx, 0), new Point(hx, imgH));
            var ft = new FormattedText(((int)Math.Round(XToHz(hx))).ToString(CultureInfo.InvariantCulture) + " Hz",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 11, Brushes.White, dpi);
            double tx = hx + 4 + ft.Width > w ? hx - 4 - ft.Width : hx + 4;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), null, new Rect(tx - 2, imgH - 18, ft.Width + 4, 16));
            dc.DrawText(ft, new Point(tx, imgH - 17));
        }
    }

    private static void DrawLabel(DrawingContext dc, string text, double x, double dpi, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 10, brush, dpi);
        dc.DrawText(ft, new Point(x + 2, 1));
    }

    private double HzToX(double hz) => (hz - MinFrequencyHz) / Math.Max(1, MaxFrequencyHz - MinFrequencyHz) * ActualWidth;

    private double XToHz(double x) => MinFrequencyHz + x / Math.Max(1, ActualWidth) * (MaxFrequencyHz - MinFrequencyHz);

    private void Select(double hz)
    {
        hz = Math.Clamp(Math.Round(hz, 1), MinFrequencyHz, MaxFrequencyHz);
        FrequencySelected?.Invoke(hz);
        if (FrequencyCommand?.CanExecute(hz) == true) FrequencyCommand.Execute(hz);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (p.Y <= ActualHeight - ScaleHeight)
        {
            double hz = Math.Round(XToHz(p.X));
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && QsyCommand?.CanExecute(hz) == true) QsyCommand.Execute(hz);
            else Select(hz);
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double pas = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10 : 1;
        Select(MarkFrequencyHz + Math.Sign(e.Delta) * pas);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverX = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Paleta "rainbow" din CwDecoder (portata de acolo din aplicatia de referinta DeepCW): saturatie
    /// 100%, nuanta de la albastru (220 de grade) spre rosu dupa t^(1.6*gama), luminozitate liniara
    /// 15%..90%. Interpolare in HSL, nu in RGB (fara culori "murdare" intre ele).
    /// </summary>
    private static int[] BuildPalette(double gamma)
    {
        var p = new int[256];
        double exp = 1.6 * Math.Clamp(gamma, 0.1, 5.0);
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            double hue = 220.0 * (1.0 - Math.Pow(t, exp)) / 360.0;
            var (r, g, b) = HslToRgb(hue, 1.0, 0.15 + 0.75 * t);
            p[i] = (r << 16) | (g << 8) | b;
        }
        return p;
    }

    private static (int R, int G, int B) HslToRgb(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double pp = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        int C(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return (C(Hue(pp, q, h + 1.0 / 3)), C(Hue(pp, q, h)), C(Hue(pp, q, h - 1.0 / 3)));
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
