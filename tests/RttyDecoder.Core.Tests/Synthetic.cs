using System.Text;
using System.Text.RegularExpressions;
using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Core.Tests;

/// <summary>
/// java.util.Random bit cu bit (LCG 48 de biti + nextGaussian polar), ca semnalele sintetice sa fie
/// identice cu cele din RttyDemodulatorTest.java - rezultatele se pot compara direct cu Android.
/// </summary>
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;
    private long _seed;
    private double _nextNextGaussian;
    private bool _haveNextNextGaussian;

    public JavaRandom(long seed) => _seed = (seed ^ Multiplier) & Mask;

    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return (int)((ulong)_seed >> (48 - bits));
    }

    public int NextInt(int bound)
    {
        int r = Next(31);
        int m = bound - 1;
        if ((bound & m) == 0) return (int)((bound * (long)r) >> 31);
        for (int u = r; u - (r = u % bound) + m < 0; u = Next(31)) { }
        return r;
    }

    public double NextDouble() => (((long)Next(26) << 27) + Next(27)) * (1.0 / (1L << 53));

    public double NextGaussian()
    {
        if (_haveNextNextGaussian)
        {
            _haveNextNextGaussian = false;
            return _nextNextGaussian;
        }
        double v1, v2, s;
        do
        {
            v1 = 2 * NextDouble() - 1;
            v2 = 2 * NextDouble() - 1;
            s = v1 * v1 + v2 * v2;
        } while (s >= 1 || s == 0);
        double multiplier = Math.Sqrt(-2 * Math.Log(s) / s);
        _nextNextGaussian = v2 * multiplier;
        _haveNextNextGaussian = true;
        return v1 * multiplier;
    }
}

/// <summary>
/// Generatorul de RTTY sintetic din RttyDemodulatorTest.java: AFSK cu faza continua, 45.45 baud,
/// shift 170 Hz, 1.5 biti de stop, zgomot gaussian limitat la ~300-3000 Hz, fading Rayleigh optional.
/// </summary>
public static class Synthetic
{
    public const int Fs = 48000;
    public const double MarkHz = 2125.0;
    public const double Baud = RttyDemodulator.BaudRtty;

    public const string Text =
        "CQ CQ CQ DE YO9VA YO9VA YO9VA PSE K\n"
        + "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 1234567890\n"
        + "UR RST 599 599 NAME EUGEN QTH PLOIESTI KN 35 73 SK\n";

    public sealed class Options
    {
        public double SnrDb = double.PositiveInfinity;
        public long Seed = 1;
        public double OffsetHz;
        public double Baud = Synthetic.Baud;
        public double FadingHz;
        public bool SpaceBelow;      // REV
        public bool TypingPauses;    // tastare manuala
        public bool BurstPauses;     // rafale type-ahead
        public int SampleRate = Fs;
        public double MarkHz = Synthetic.MarkHz;
    }

    public static short[] Generate(string text, Options o)
    {
        var biti = new StringBuilder(); // '1' = Mark, '0' = Space, fiecare = 1/2 bit
        var tx = new BaudotCodec();
        for (int i = 0; i < 46; i++) biti.Append("11");
        foreach (int c in new[] { 0x1F, 0x1F, 0x1F }) AddChar(biti, c);
        var rndPauze = new JavaRandom(o.Seed * 131 + 3);
        int panaLaPauza = 15 + rndPauze.NextInt(16);
        foreach (char ch in text)
        {
            foreach (int c in tx.Encode(ch)) AddChar(biti, c);
            if (o.BurstPauses && --panaLaPauza <= 0)
            {
                int jumatati = 9 + rndPauze.NextInt(170);
                biti.Append('1', jumatati);
                panaLaPauza = 15 + rndPauze.NextInt(16);
            }
            if (o.TypingPauses)
            {
                double r = rndPauze.NextDouble();
                int jumatati = r < 0.05 ? 40 + rndPauze.NextInt(110) : r < 0.55 ? 1 + rndPauze.NextInt(8) : 0;
                biti.Append('1', jumatati);
            }
        }
        for (int i = 0; i < 46; i++) biti.Append("11");

        int fs = o.SampleRate;
        double samplesPerHalfBit = fs / o.Baud / 2.0;
        int n = (int)Math.Ceiling(biti.Length * samplesPerHalfBit);
        const double amplitudine = 0.3;
        var semnal = new double[n];
        double[]? castigMark = o.FadingHz > 0 ? RayleighGain(n, fs, o.FadingHz, o.Seed * 31 + 7) : null;
        double[]? castigSpace = o.FadingHz > 0 ? RayleighGain(n, fs, o.FadingHz, o.Seed * 31 + 11) : null;
        double faza = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = Math.Min(biti.Length - 1, (int)(i / samplesPerHalfBit));
            bool mark = biti[idx] == '1';
            double f = (mark ? o.MarkHz : o.MarkHz + (o.SpaceBelow ? -1 : 1) * RttyDemodulator.ShiftHz) + o.OffsetHz;
            faza += 2 * Math.PI * f / fs;
            double castig = castigMark == null ? 1.0 : (mark ? castigMark[i] : castigSpace![i]);
            semnal[i] = amplitudine * castig * Math.Sin(faza);
        }

        if (!double.IsInfinity(o.SnrDb))
        {
            var rnd = new JavaRandom(o.Seed);
            var zgomot = new double[n];
            for (int i = 0; i < n; i++) zgomot[i] = rnd.NextGaussian();
            var hp1 = Biquad.Highpass(300, fs);
            var hp2 = Biquad.Highpass(300, fs);
            var lp1 = Biquad.Lowpass(3000, fs);
            var lp2 = Biquad.Lowpass(3000, fs);
            double putereZgomot = 0;
            for (int i = 0; i < n; i++)
            {
                zgomot[i] = lp2.Process(lp1.Process(hp2.Process(hp1.Process(zgomot[i]))));
                putereZgomot += zgomot[i] * zgomot[i];
            }
            putereZgomot /= n;
            double putereSemnal = amplitudine * amplitudine / 2.0;
            double scala = Math.Sqrt(putereSemnal / Math.Pow(10, o.SnrDb / 10.0) / putereZgomot);
            for (int i = 0; i < n; i++) semnal[i] += zgomot[i] * scala;
        }

        var pcm = new short[n];
        for (int i = 0; i < n; i++)
            pcm[i] = (short)Math.Max(-32768, Math.Min(32767, (long)Math.Floor(semnal[i] * 32767 + 0.5)));
        return pcm;
    }

    private static double[] RayleighGain(int n, int fs, double fadingHz, long seed)
    {
        const double rataControl = 200.0;
        int m = (int)Math.Ceiling(n / (double)fs * rataControl) + 2;
        var rnd = new JavaRandom(seed);
        var lpRe = Biquad.Lowpass(fadingHz, rataControl);
        var lpIm = Biquad.Lowpass(fadingHz, rataControl);
        var mag = new double[m];
        for (int k = 0; k < 2000; k++) { lpRe.Process(rnd.NextGaussian()); lpIm.Process(rnd.NextGaussian()); }
        double putere = 0;
        for (int k = 0; k < m; k++)
        {
            double re = lpRe.Process(rnd.NextGaussian()), im = lpIm.Process(rnd.NextGaussian());
            mag[k] = Math.Sqrt(re * re + im * im);
            putere += mag[k] * mag[k];
        }
        double norm = Math.Sqrt(putere / m);
        var g = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)fs * rataControl;
            int k = (int)t;
            double fr = t - k;
            g[i] = (mag[k] * (1 - fr) + mag[k + 1] * fr) / norm;
        }
        return g;
    }

    /// <summary>Start (1 bit Space), 5 biti LSB-first, stop 1.5 biti Mark - in jumatati de bit.</summary>
    private static void AddChar(StringBuilder biti, int cod)
    {
        biti.Append("00");
        for (int b = 0; b < 5; b++) biti.Append(((cod >> b) & 1) == 1 ? "11" : "00");
        biti.Append("111");
    }

    /// <summary>Decodare cu demodulatorul clasic, in blocuri de 3840 ca pe Android.</summary>
    public static string DecodeClassic(short[] audio, bool matchedFilter, Action<RttyDemodulator>? config = null)
    {
        var demod = new RttyDemodulator { folosesteMatchedFilter = matchedFilter };
        config?.Invoke(demod);
        var rx = new BaudotCodec();
        var sb = new StringBuilder();
        demod.CodeReceived = cod =>
        {
            char c = rx.Decode(cod);
            if (c != '\0') sb.Append(c);
        };
        demod.Start(Fs);
        const int bloc = 3840;
        var buf = new short[bloc];
        for (int pos = 0; pos < audio.Length; pos += bloc)
        {
            int n = Math.Min(bloc, audio.Length - pos);
            Array.Copy(audio, pos, buf, 0, n);
            demod.ProcessBlock(buf, n);
        }
        return sb.ToString();
    }

    public static string Decode(string text, double snrDb, bool mf, long seed, Action<RttyDemodulator>? config = null)
        => DecodeClassic(Generate(text, new Options { SnrDb = snrDb, Seed = seed }), mf, config);

    public static double Cer(string expected, string decoded)
    {
        string a = Normalize(expected), b = Normalize(decoded);
        return (double)Levenshtein(a, b) / a.Length;
    }

    public static string Normalize(string s) => Regex.Replace(s.Replace('\r', '\n'), @"\s+", " ").Trim();

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static float[] ToFloat(short[] pcm) => pcm.Select(s => s / 32768f).ToArray();

    /// <summary>Biquad RBJ de ordin 2 (Q = 1/sqrt(2)).</summary>
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
            _a1 = a1 / a0; _a2 = a2 / a0;
        }

        public static Biquad Lowpass(double fc, double fs)
        {
            double w = 2 * Math.PI * fc / fs, alpha = Math.Sin(w) / Math.Sqrt(2), c = Math.Cos(w);
            return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        public static Biquad Highpass(double fc, double fs)
        {
            double w = 2 * Math.PI * fc / fs, alpha = Math.Sin(w) / Math.Sqrt(2), c = Math.Cos(w);
            return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        public double Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
