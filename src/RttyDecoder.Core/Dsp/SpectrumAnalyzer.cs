namespace RttyDecoder.Core.Dsp;

/// <summary>Instantaneu imuabil prin conventie: amplitudini de varf normalizate (1 = 0 dBFS), inclusiv DC/Nyquist.</summary>
public sealed record SpectrumFrame(long Epoch, long Sequence, double AudioSeconds, double BinHz, float[] Magnitudes);

/// <summary>48 kHz -> FIR anti-alias -> 8 kHz -> FFT Hamming 512/1024. Nu este thread-safe.</summary>
public sealed class SpectrumAnalyzer
{
    public const int InputRate = 48000;
    public const int Decimation = 6;
    public const int OutputRate = InputRate / Decimation;
    public const int FftSize = 512;
    public const int Bins = FftSize / 2;
    public const double BinHz = (double)OutputRate / FftSize;
    public const int HopSamples = 256;
    public const double RowSeconds = (double)HopSamples / OutputRate;
    public const int AverageRows = 8;
    private readonly double[] _h;
    private readonly double[] _delay;
    private int _delayPos, _phase, _ringPos, _sinceLastRow, _received;
    private static long _nextEpoch;
    private long _samples, _epoch, _sequence;
    private double[] _ring = [], _frame = [], _mag = [];
    private SimpleFft _fft = null!;
    private double _windowSum;
    private double[][] _history = [];
    private int _historyPos, _historyCount;
    public int Size => _fft.Size;
    public double BinWidthHz => (double)OutputRate / Size;
    public event Action<float[]>? RowAvailable;
    public event Action<SpectrumFrame>? FrameAvailable;

    public SpectrumAnalyzer(int size = FftSize)
    {
        _epoch = Interlocked.Increment(ref _nextEpoch);
        const int taps = 121;
        const double fc = 3400.0 / InputRate;
        _h = new double[taps];
        for (int i = 0; i < taps; i++)
        {
            int m = i - taps / 2;
            double sinc = m == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * m) / (Math.PI * m);
            double window = .42 - .5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + .08 * Math.Cos(4 * Math.PI * i / (taps - 1));
            _h[i] = sinc * window;
        }
        double sum = _h.Sum();
        for (int i = 0; i < taps; i++) _h[i] /= sum;
        _delay = new double[taps];
        SetSize(size);
    }

    /// <summary>Schimbarea rezolutiei nu reseteaza timpul audio; prima fereastra noua trebuie umpluta complet.</summary>
    public void SetSize(int size)
    {
        if (size is not (512 or 1024)) throw new ArgumentOutOfRangeException(nameof(size));
        if (_fft?.Size == size) return;
        _fft = new SimpleFft(size);
        _windowSum = _fft.WindowSum;
        _ring = new double[size]; _frame = new double[size]; _mag = new double[size / 2 + 1];
        _history = Enumerable.Range(0, AverageRows).Select(_ => new double[_mag.Length]).ToArray();
        _ringPos = _sinceLastRow = _received = _historyPos = _historyCount = 0;
    }

    public void Reset()
    {
        Array.Clear(_delay); Array.Clear(_ring);
        _delayPos = _phase = _ringPos = _sinceLastRow = _received = 0;
        _historyPos = _historyCount = 0;
        _samples = _sequence = 0;
        _epoch = Interlocked.Increment(ref _nextEpoch);
    }

    public void Process(short[] pcm, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _delay[_delayPos] = pcm[i] / 32768.0;
            _delayPos = (_delayPos + 1) % _h.Length;
            if (++_phase < Decimation) continue;
            _phase = 0;
            double value = 0;
            int index = _delayPos;
            for (int k = 0; k < _h.Length; k++)
            {
                value += _h[k] * _delay[index];
                if (++index == _h.Length) index = 0;
            }
            _ring[_ringPos] = value;
            _ringPos = (_ringPos + 1) % Size;
            _samples++;
            _received = Math.Min(Size, _received + 1);
            if (++_sinceLastRow >= HopSamples)
            {
                _sinceLastRow = 0;
                if (_received >= Size) EmitRow();
            }
        }
    }

    private void EmitRow()
    {
        for (int i = 0; i < Size; i++) _frame[i] = _ring[(_ringPos + i) % Size];
        _fft.Magnitude(_frame, _mag);
        for (int i = 0; i < _mag.Length; i++)
            _mag[i] *= (i == 0 || i == Size / 2 ? 1 : 2) / _windowSum;
        Array.Copy(_mag, _history[_historyPos], _mag.Length);
        _historyPos = (_historyPos + 1) % AverageRows;
        _historyCount = Math.Min(AverageRows, _historyCount + 1);
        _sequence++;
        if (RowAvailable == null && FrameAvailable == null) return;
        var row = Array.ConvertAll(_mag, x => (float)x);
        FrameAvailable?.Invoke(new(_epoch, _sequence, (double)_samples / OutputRate, BinWidthHz, row));
        RowAvailable?.Invoke(row);
    }

    public double[]? AverageSpectrum()
    {
        if (_historyCount == 0) return null;
        var result = new double[_mag.Length];
        for (int r = 0; r < _historyCount; r++)
            for (int i = 0; i < result.Length; i++) result[i] += _history[r][i] / _historyCount;
        return result;
    }
}
