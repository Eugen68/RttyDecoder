namespace RttyDecoder.Core.Dsp;

/// <summary>
/// FFT radix-2 cu fereastra Hamming, doar magnitudinea (port din SimpleFFT.java).
/// Fiecare apelant are propria instanta: bufferele interne sunt refolosite intre apeluri.
/// </summary>
public sealed class SimpleFft
{
    private readonly int _n;
    private readonly int _log2N;
    private readonly double[] _cos;
    private readonly double[] _sin;
    private readonly double[] _window;
    private readonly double[] _x;
    private readonly double[] _y;

    public SimpleFft(int n)
    {
        if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("n trebuie sa fie putere a lui 2", nameof(n));
        _n = n;
        _log2N = (int)Math.Round(Math.Log2(n));
        _cos = new double[n / 2];
        _sin = new double[n / 2];
        _x = new double[n];
        _y = new double[n];
        for (int i = 0; i < n / 2; i++)
        {
            _cos[i] = Math.Cos(-2 * Math.PI * i / n);
            _sin[i] = Math.Sin(-2 * Math.PI * i / n);
        }
        _window = new double[n];
        for (int i = 0; i < n; i++) _window[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (n - 1));
    }

    public int Size => _n;

    /// <summary>Magnitudinea primelor n/2 bin-uri pentru esantioanele (deja normalizate la +-1) date.</summary>
    public void Magnitude(ReadOnlySpan<double> input, Span<double> magnitudeOut)
    {
        int limit = Math.Min(_n, input.Length);
        for (int i = 0; i < limit; i++)
        {
            _x[i] = input[i] * _window[i];
            _y[i] = 0;
        }
        for (int i = limit; i < _n; i++)
        {
            _x[i] = 0;
            _y[i] = 0;
        }

        int j = 0;
        int n2 = _n / 2;
        for (int i = 1; i < _n - 1; i++)
        {
            int n1 = n2;
            while (j >= n1)
            {
                j -= n1;
                n1 /= 2;
            }
            j += n1;
            if (i < j)
            {
                (_x[i], _x[j]) = (_x[j], _x[i]);
                (_y[i], _y[j]) = (_y[j], _y[i]);
            }
        }

        n2 = 1;
        for (int i = 0; i < _log2N; i++)
        {
            int n1 = n2;
            n2 *= 2;
            int a = 0;
            int step = 1 << (_log2N - i - 1);
            for (j = 0; j < n1; j++)
            {
                double c = _cos[a];
                double s = _sin[a];
                a += step;
                for (int k = j; k < _n; k += n2)
                {
                    double t1 = c * _x[k + n1] - s * _y[k + n1];
                    double t2 = s * _x[k + n1] + c * _y[k + n1];
                    _x[k + n1] = _x[k] - t1;
                    _y[k + n1] = _y[k] - t2;
                    _x[k] += t1;
                    _y[k] += t2;
                }
            }
        }

        int bins = Math.Min(_n / 2, magnitudeOut.Length);
        for (int m = 0; m < bins; m++) magnitudeOut[m] = Math.Sqrt(_x[m] * _x[m] + _y[m] * _y[m]);
    }
}
