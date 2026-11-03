namespace RttyDecoder.Core.Dsp;

/// <summary>
/// Reesantionare in flux (rata oarecare -&gt; rata oarecare) cu sinc ferestruit Kaiser, tabel de
/// faze interpolat liniar. Folosit ca decodorul sa primeasca mereu 48 kHz: placile de sunet la
/// 44.1 kHz si fisierele WAV la 8/11.025/22.05/44.1 kHz. Calitatea (~-80 dB) e cu mult peste
/// ce cere RTTY-ul. Nu e thread-safe.
/// </summary>
public sealed class Resampler
{
    private const int Phases = 512;
    private const int ZeroCrossings = 16;
    private const double KaiserBeta = 8.0;

    private readonly double _step;       // esantioane de intrare per esantion de iesire
    private readonly int _halfTaps;
    private readonly double _tapScale;   // trecerea de la distanta (esantioane de intrare) la indexul tabelului
    private readonly double[] _table;    // jumatatea pozitiva a nucleului, Phases puncte per unitate de tabel
    private readonly double _gain;

    private double[] _hist;              // ultimele esantioane de intrare (cu _halfTaps de istoric)
    private int _histLen;
    private double _pos;                 // pozitia urmatorului esantion de iesire, relativa la _hist

    public Resampler(int inputRate, int outputRate)
    {
        if (inputRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        InputRate = inputRate;
        OutputRate = outputRate;
        _step = (double)inputRate / outputRate;
        // Banda trecuta: 0.9 din Nyquist-ul celei mai mici dintre rate.
        double cutoff = 0.9 * Math.Min(1.0, (double)outputRate / inputRate);
        _gain = cutoff;
        _tapScale = cutoff;
        _halfTaps = (int)Math.Ceiling(ZeroCrossings / cutoff) + 1;

        _table = new double[ZeroCrossings * Phases + 2];
        double i0Beta = BesselI0(KaiserBeta);
        for (int i = 0; i < _table.Length; i++)
        {
            double t = (double)i / Phases; // in treceri prin zero
            if (t >= ZeroCrossings) { _table[i] = 0; continue; }
            double sinc = t == 0 ? 1.0 : Math.Sin(Math.PI * t) / (Math.PI * t);
            double r = t / ZeroCrossings;
            double w = BesselI0(KaiserBeta * Math.Sqrt(1 - r * r)) / i0Beta;
            _table[i] = sinc * w;
        }

        _hist = new double[4096 + 2 * _halfTaps];
        Reset();
    }

    public int InputRate { get; }
    public int OutputRate { get; }

    public void Reset()
    {
        Array.Clear(_hist);
        _histLen = _halfTaps; // istoric de zerouri, ca primul esantion sa aiba "trecut"
        _pos = _halfTaps;
    }

    /// <summary>Numarul maxim de esantioane de iesire pentru <paramref name="inputCount"/> esantioane de intrare.</summary>
    public int MaxOutput(int inputCount) => (int)Math.Ceiling(inputCount / _step) + 2;

    /// <summary>Reesantioneaza; intoarce cate esantioane a scris in <paramref name="output"/>.</summary>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        EnsureCapacity(_histLen + input.Length);
        for (int i = 0; i < input.Length; i++) _hist[_histLen + i] = input[i];
        _histLen += input.Length;

        int written = 0;
        // Un esantion de iesire la pozitia _pos are nevoie de _hist[pos - halfTaps .. pos + halfTaps].
        while (_pos + _halfTaps < _histLen && written < output.Length)
        {
            int center = (int)Math.Floor(_pos);
            double frac = _pos - center;
            double acc = 0;
            for (int k = -_halfTaps + 1; k <= _halfTaps; k++)
            {
                double d = Math.Abs(k - frac) * _tapScale * Phases;
                int idx = (int)d;
                if (idx >= _table.Length - 1) continue;
                double f = d - idx;
                double h = _table[idx] + (_table[idx + 1] - _table[idx]) * f;
                acc += _hist[center + k] * h;
            }
            output[written++] = (float)(acc * _gain);
            _pos += _step;
        }

        // Pastram doar istoricul necesar.
        int drop = (int)Math.Floor(_pos) - _halfTaps;
        if (drop > 0)
        {
            Array.Copy(_hist, drop, _hist, 0, _histLen - drop);
            _histLen -= drop;
            _pos -= drop;
        }
        return written;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _hist.Length) return;
        Array.Resize(ref _hist, Math.Max(needed, _hist.Length * 2));
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, q = x * x / 4;
        for (int k = 1; k < 50; k++)
        {
            term *= q / (k * k);
            sum += term;
            if (term < sum * 1e-17) break;
        }
        return sum;
    }
}
