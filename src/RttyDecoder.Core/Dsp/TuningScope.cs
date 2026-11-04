namespace RttyDecoder.Core.Dsp;

/// <summary>Un punct al indicatorului de acord: semnalele analitice ale celor doua filtre, normalizate.</summary>
/// <remarks>
/// Elipsa de desenat: X(t) = |Mark| cos(t), Y(t) = |Space| cos(t + faza), adica
/// <c>X = Re(M e^{jt})</c>, <c>Y = Re(S e^{jt})</c>, cu M si S de aici.
/// </remarks>
public readonly record struct TuningPoint(float MarkRe, float MarkIm, float SpaceRe, float SpaceIm);

/// <summary>
/// Indicatorul clasic de acord RTTY ("elipsele incrucisate", ca pe un osciloscop X-Y): iesirea
/// filtrului Mark pe orizontala, a filtrului Space pe verticala. Un ton Mark da o elipsa culcata,
/// un ton Space una in picioare; acordat corect, cele doua formeaza o cruce. Dezacordat, elipsele
/// se inclina si se ingroasa; cu shift-ul gresit sau pe zgomot, apare o pata.
///
/// Fiecare filtru e un rezonator (filtru complex de ordinul 1 centrat pe ton, ca un circuit LC):
/// la 170 Hz de centru atenueaza ~13 dB si roteste faza cu ~77 grade, de aceea elipsele sunt
/// subtiri si aproape perpendiculare, ca la indicatoarele analogice. Pentru desen se pastreaza
/// semnalul analitic (complex) al fiecarui filtru, deci elipsa se poate trasa neted la orice rata
/// de esantionare. Normalizare automata dupa varful recent (AGC lent).
///
/// <see cref="Process"/> pe firul audio, <see cref="Read"/> de pe firul UI.
/// </summary>
public sealed class TuningScope
{
    /// <summary>Latimea de banda (-3 dB) a fiecarui filtru: ~ baud-ul (45.45), ca elipsele sa urmareasca bitii.</summary>
    public const double FilterBandwidthHz = 40;

    /// <summary>Un punct la atatea milisecunde de semnal.</summary>
    public const double PointIntervalMs = 2;

    private const int Capacity = 4096;

    private readonly object _lock = new();
    private readonly TuningPoint[] _ring = new TuningPoint[Capacity];
    private int _write;
    private int _count;

    // starea filtrelor (doar firul audio)
    private double _mRe, _mIm, _sRe, _sIm;     // invelitoarea complexa (banda de baza)
    private double _phM, _phS;                 // fazele oscilatoarelor locale
    private double _peak = 1e-3;
    private double _untilPoint;
    private int _rate;

    private double _markHz = 2125;
    private double _spaceHz = 2295;

    /// <summary>Frecventele tonurilor (Hz); se pot schimba oricand (acord, AFC, REV).</summary>
    public void SetTones(double markHz, double spaceHz)
    {
        Volatile.Write(ref _markHz, markHz);
        Volatile.Write(ref _spaceHz, spaceHz);
    }

    /// <summary>Esantioane mono (+-1) la orice rata.</summary>
    public void Process(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length == 0) return;
        if (sampleRate != _rate)
        {
            _rate = sampleRate;
            _mRe = _mIm = _sRe = _sIm = 0;
        }
        double markHz = Volatile.Read(ref _markHz), spaceHz = Volatile.Read(ref _spaceHz);
        double dM = 2 * Math.PI * markHz / sampleRate;
        double dS = 2 * Math.PI * spaceHz / sampleRate;
        double a = 1 - Math.Exp(-2 * Math.PI * FilterBandwidthHz / sampleRate); // trece-jos de ordinul 1
        double peakDecay = Math.Exp(-1.0 / (0.5 * sampleRate));                // varful scade in ~0.5 s
        double step = sampleRate * PointIntervalMs / 1000.0;

        Span<TuningPoint> pending = stackalloc TuningPoint[64];
        int n = 0;
        foreach (float x in samples)
        {
            // coborare in banda de baza (x * e^{-j ph}) si trece-jos: invelitoarea complexa a tonului
            double cm = Math.Cos(_phM), sm = Math.Sin(_phM);
            double cs = Math.Cos(_phS), ss = Math.Sin(_phS);
            _mRe += a * (x * cm - _mRe);
            _mIm += a * (-x * sm - _mIm);
            _sRe += a * (x * cs - _sRe);
            _sIm += a * (-x * ss - _sIm);

            double mag = Math.Max(_mRe * _mRe + _mIm * _mIm, _sRe * _sRe + _sIm * _sIm);
            mag = Math.Sqrt(mag);
            _peak = Math.Max(mag, _peak * peakDecay);

            if (--_untilPoint <= 0)
            {
                _untilPoint += step;
                // semnalul analitic al fiecarui filtru: invelitoarea urcata inapoi pe purtatoare
                double g = 1.0 / Math.Max(_peak, 1e-6);
                double mr = _mRe * cm - _mIm * sm, mi = _mRe * sm + _mIm * cm;   // M e^{+j phM}
                double sr = _sRe * cs - _sIm * ss, si = _sRe * ss + _sIm * cs;   // S e^{+j phS}
                pending[n++] = new TuningPoint((float)(mr * g), (float)(mi * g), (float)(sr * g), (float)(si * g));
                if (n == pending.Length)
                {
                    Store(pending);
                    n = 0;
                }
            }

            _phM += dM;
            _phS += dS;
            if (_phM > 2 * Math.PI) _phM -= 2 * Math.PI;
            if (_phS > 2 * Math.PI) _phS -= 2 * Math.PI;
        }
        if (n > 0) Store(pending[..n]);
    }

    private void Store(ReadOnlySpan<TuningPoint> points)
    {
        lock (_lock)
        {
            foreach (var p in points)
            {
                _ring[_write] = p;
                _write = (_write + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }
    }

    /// <summary>Punctele noi de la ultimul apel (cele mai vechi primele); intoarce cate au fost scrise.</summary>
    public int Read(Span<TuningPoint> destination)
    {
        lock (_lock)
        {
            int n = Math.Min(_count, destination.Length);
            int start = (_write - _count + Capacity) % Capacity;
            start = (start + _count - n) % Capacity; // daca nu incap, doar cele mai noi
            for (int i = 0; i < n; i++) destination[i] = _ring[(start + i) % Capacity];
            _count = 0;
            return n;
        }
    }

    /// <summary>Uita tot (la oprire / schimbarea sursei).</summary>
    public void Reset()
    {
        lock (_lock) _count = 0;
        _peak = 1e-3;
    }
}
