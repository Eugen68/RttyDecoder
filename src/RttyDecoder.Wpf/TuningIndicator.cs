using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Wpf;

/// <summary>
/// Indicatorul de acord cu elipse incrucisate (ca pe ecranul unui osciloscop): Mark pe orizontala,
/// Space pe verticala. Acordat corect = o cruce din doua elipse subtiri; dezacordat = elipse
/// inclinate/ingrosate; zgomot = o pata. Ecranul are "persistenta" (fosforul se stinge in ~0.2 s),
/// deci se vad ambele elipse desi tonurile alterneaza bit cu bit. Citeste punctele din
/// <see cref="Scope"/> de ~30 de ori pe secunda.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TuningIndicator : Grid
{
    private const int Size = 200;            // pixelii bitmap-ului (se scaleaza la marimea controlului)
    private const double Fade = 0.78;        // cat ramane din stralucire de la un cadru la altul (~30 cadre/s)
    private const int Segments = 56;         // segmente pe elipsa

    private readonly WriteableBitmap _bitmap = new(Size, Size, 96, 96, PixelFormats.Pbgra32, null);
    private readonly float[] _glow = new float[Size * Size];   // stralucirea 0..1 pe pixel
    private readonly int[] _pixels = new int[Size * Size];
    private readonly TuningPoint[] _points = new TuningPoint[4096];
    private readonly DispatcherTimer _timer;
    private bool _dark = true;

    public TuningIndicator()
    {
        Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x06, 0x12, 0x08)) });
        Children.Add(Graticule());
        Children.Add(new Image { Source = _bitmap, Stretch = Stretch.Fill });
        SizeChanged += (_, _) =>
        {
            double d = Math.Min(ActualWidth, ActualHeight);
            Clip = new EllipseGeometry(new Point(ActualWidth / 2, ActualHeight / 2), d / 2, d / 2);
        };
        ToolTip = "Indicator de acord: Mark pe orizontala, Space pe verticala.\n" +
                  "Acordat corect = cruce din doua elipse subtiri.\n" +
                  "Elipse inclinate sau groase = dezacordat; o singura elipsa = REV gresit sau doar purtatoare.";
        ToolTipService.SetInitialShowDelay(this, 1500);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Frame(), Dispatcher);
        _timer.Stop();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Sursa punctelor (de obicei <see cref="RttyDecoderViewModel.Scope"/>).</summary>
    public TuningScope? Scope { get; set; }

    private static UIElement Graticule()
    {
        var line = new SolidColorBrush(Color.FromArgb(0x50, 0x4C, 0x9A, 0x5A));
        line.Freeze();
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry(new Point(0, 50), new Point(100, 50)));
        g.Children.Add(new LineGeometry(new Point(50, 0), new Point(50, 100)));
        return new Path { Data = g, Stroke = line, StrokeThickness = 0.6, Stretch = Stretch.Fill, Margin = new Thickness(0) };
    }

    private void Frame()
    {
        int n = Scope?.Read(_points) ?? 0;
        if (n == 0 && _dark) return; // nimic de desenat si ecranul e deja stins

        bool any = false;
        for (int i = 0; i < _glow.Length; i++)
        {
            float v = _glow[i];
            if (v <= 0) continue;
            v = v * (float)Fade - 0.004f;
            _glow[i] = v > 0 ? v : 0;
            any |= v > 0;
        }

        // ultimele ~33 ms de semnal: cate o elipsa la 2 ms, fiecare cu stralucire mica (se aduna)
        float hit = n > 0 ? Math.Min(0.55f, 6f / n) : 0;
        double c = (Size - 1) / 2.0, r = Size * 0.44;
        for (int k = 0; k < n; k++)
        {
            var p = _points[k];
            double px = 0, py = 0;
            for (int s = 0; s <= Segments; s++)
            {
                double t = 2 * Math.PI * s / Segments, ct = Math.Cos(t), st = Math.Sin(t);
                double x = c + r * (p.MarkRe * ct - p.MarkIm * st);
                double y = c - r * (p.SpaceRe * ct - p.SpaceIm * st);
                if (s > 0) Line(px, py, x, y, hit);
                px = x;
                py = y;
            }
            any = true;
        }

        for (int i = 0; i < _glow.Length; i++)
        {
            float v = _glow[i];
            if (v <= 0)
            {
                _pixels[i] = 0;
                continue;
            }
            if (v > 1) v = 1;
            // verde de fosfor; centrul liniilor stralucitoare spre alb-verzui; premultiplicat
            int a = (int)(255 * v);
            int g = a;
            int rC = (int)(a * Math.Max(0, v - 0.6) * 1.6);
            int b = (int)(a * (0.35 + 0.4 * Math.Max(0, v - 0.6)));
            _pixels[i] = (a << 24) | (rC << 16) | (g << 8) | b;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, Size, Size), _pixels, Size * 4, 0);
        _dark = !any;
    }

    private void Line(double x0, double y0, double x1, double y1, float amount)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (steps == 0) steps = 1;
        for (int i = 0; i < steps; i++)
        {
            int x = (int)(x0 + dx * i / steps + 0.5), y = (int)(y0 + dy * i / steps + 0.5);
            if ((uint)x >= Size || (uint)y >= Size) continue;
            _glow[y * Size + x] += amount;
        }
    }
}
