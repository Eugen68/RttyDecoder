namespace RttyDecoder.Core.Dsp;

/// <summary>
/// Spectrul pentru waterfall si A-TUNE: 48 kHz -&gt; 8 kHz cu filtru anti-aliasing (FIR), FFT de 512
/// puncte (15.6 Hz/bin, 0-4000 Hz), cate un rand la fiecare <see cref="HopSamples"/> esantioane de 8 kHz.
///
/// Fata de Android: acolo se lua direct un esantion din 6, fara filtru, deci tot ce era intre 4 si
/// 24 kHz (zgomot, tonuri parazite din placa de sunet) se plia peste 0-4 kHz; si se folosea doar
/// inceputul fiecarui bloc audio. Aici fiecare esantion intra in calcul.
/// Nu e thread-safe.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int InputRate = 48000;
    public const int Decimation = 6;
    public const int OutputRate = InputRate / Decimation;
    public const int FftSize = 512;
    public const int Bins = FftSize / 2;
    public const double BinHz = (double)OutputRate / FftSize;

    /// <summary>Pas intre randuri: 256 esantioane la 8 kHz = 32 ms (~31 randuri/s).</summary>
    public const int HopSamples = 256;

    /// <summary>Randuri pastrate pentru media folosita de A-TUNE (~0.26 s, ca pe Android).</summary>
    public const int AverageRows = 8;

    private readonly double[] _h;
    private readonly double[] _delay;
    private int _delayPos;
    private int _phase;

    private readonly double[] _ring = new double[FftSize];
    private int _ringPos;
    private int _sinceLastRow;
    private readonly double[] _frame = new double[FftSize];
    private readonly double[] _mag = new double[Bins];
    private readonly SimpleFft _fft = new(FftSize);

    private readonly double[][] _history = new double[AverageRows][];
    private int _historyPos;
    private int _historyCount;

    /// <summary>Un rand nou de magnitudini liniare (Bins valori, BinHz pe bin).</summary>
    public event Action<float[]>? RowAvailable;

    public SpectrumAnalyzer()
    {
        // Trece-jos ~3.4 kHz, tranzitie pana la 4 kHz (Nyquist-ul iesirii), Blackman.
        const int taps = 121;
        double fc = 3400.0 / InputRate;
        _h = new double[taps];
        double sum = 0;
        for (int i = 0; i < taps; i++)
        {
            int m = i - taps / 2;
            double sinc = m == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * m) / (Math.PI * m);
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
            _h[i] = sinc * w;
            sum += _h[i];
        }
        for (int i = 0; i < taps; i++) _h[i] /= sum;
        _delay = new double[taps];
        for (int i = 0; i < AverageRows; i++) _history[i] = new double[Bins];
    }

    public void Reset()
    {
        Array.Clear(_delay);
        Array.Clear(_ring);
        _delayPos = _phase = _ringPos = _sinceLastRow = 0;
        _historyPos = _historyCount = 0;
    }

    public void Process(short[] pcm, int count)
    {
        int taps = _h.Length;
        for (int i = 0; i < count; i++)
        {
            _delay[_delayPos] = pcm[i] / 32768.0;
            _delayPos = (_delayPos + 1) % taps;
            if (++_phase < Decimation) continue;
            _phase = 0;

            double y = 0;
            int idx = _delayPos;
            for (int k = 0; k < taps; k++)
            {
                y += _h[k] * _delay[idx];
                if (++idx == taps) idx = 0;
            }
            _ring[_ringPos] = y;
            _ringPos = (_ringPos + 1) % FftSize;
            if (++_sinceLastRow >= HopSamples)
            {
                _sinceLastRow = 0;
                EmitRow();
            }
        }
    }

    private void EmitRow()
    {
        for (int i = 0; i < FftSize; i++) _frame[i] = _ring[(_ringPos + i) % FftSize];
        _fft.Magnitude(_frame, _mag);

        Array.Copy(_mag, _history[_historyPos], Bins);
        _historyPos = (_historyPos + 1) % AverageRows;
        if (_historyCount < AverageRows) _historyCount++;

        var handler = RowAvailable;
        if (handler != null)
        {
            var row = new float[Bins];
            for (int i = 0; i < Bins; i++) row[i] = (float)_mag[i];
            handler(row);
        }
    }

    /// <summary>Media magnitudinilor pe ultimele randuri (cel mult <see cref="AverageRows"/>), sau null.</summary>
    public double[]? AverageSpectrum()
    {
        if (_historyCount == 0) return null;
        var avg = new double[Bins];
        for (int r = 0; r < _historyCount; r++)
            for (int i = 0; i < Bins; i++)
                avg[i] += _history[r][i];
        for (int i = 0; i < Bins; i++) avg[i] /= _historyCount;
        return avg;
    }
}
