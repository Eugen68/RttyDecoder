namespace RttyDecoder.Core.Tx;

/// <summary>
/// Iesire care primeste caractere Baudot intregi (start + 5 biti + stop), nu fronturi de pin.
/// Ritmul il da iesirea (de exemplu ceasul placii de sunet), nu ceasul emitatorului.
/// </summary>
public interface IFrameOutput : IFskOutput
{
    /// <summary>Pune un cod Baudot in coada; asteapta cat timp coada este plina.</summary>
    void SendCode(int code, CancellationToken ct);

    /// <summary>Pune o pauza pe Mark in coada.</summary>
    void SendMark(double seconds, CancellationToken ct);

    /// <summary>Asteapta pana cand tot ce era in coada a iesit efectiv.</summary>
    void Drain(CancellationToken ct);
}

/// <summary>
/// Generator AFSK (unul pe emisie; porneste cu o rampa de 5 ms): coduri Baudot -> tonuri Mark/Space cu faza continua, la rata placii de sunet.
/// Bitii se numara in esantioane (cu rest fractionar), deci viteza este exacta pe ceasul placii.
/// Fara date in coada genereaza Mark. <see cref="Render"/> se apeleaza de pe firul audio; restul
/// metodelor de pe orice fir.
/// </summary>
public sealed class AfskModulator
{
    private readonly object _lock = new();
    private readonly Queue<(bool Mark, double Bits, bool EndsCode)> _segments = new();
    private readonly double _bitSamples;
    private readonly int _rampSamples;
    private double _phase;
    private double _remaining;
    private bool _currentMark = true;
    private bool _currentEndsCode;
    private int _codesPending;
    private int _rampPos;
    private bool _failed;

    public AfskModulator(int sampleRate, double markHz, double spaceHz, double level = 0.5)
    {
        if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;
        MarkHz = markHz;
        SpaceHz = spaceHz;
        Level = Math.Clamp(level, 0, 1);
        _bitSamples = sampleRate / RttyTxSettings.Baud;
        _rampSamples = sampleRate / 200; // 5 ms
    }

    public int SampleRate { get; }
    public double MarkHz { get; }
    public double SpaceHz { get; }
    public double Level { get; }

    /// <summary>Coduri inca negenerate complet (inclusiv cel inceput).</summary>
    public int PendingCodes { get { lock (_lock) return _codesPending; } }

    /// <summary>Nimic in coada: se genereaza Mark.</summary>
    public bool IsIdle { get { lock (_lock) return _segments.Count == 0 && _remaining <= 0; } }

    public void Enqueue(int code)
    {
        lock (_lock)
        {
            _segments.Enqueue((false, 1, false));
            for (int i = 0; i < 5; i++) _segments.Enqueue((((code >> i) & 1) == 1, 1, false));
            _segments.Enqueue((true, RttyTxSettings.StopBits, true));
            _codesPending++;
        }
    }

    public void EnqueueMark(double seconds)
    {
        if (seconds <= 0) return;
        lock (_lock) _segments.Enqueue((true, seconds * RttyTxSettings.Baud, false));
    }

    /// <summary>Goleste coada (emisia oprita); generatorul ramane pe Mark.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _segments.Clear();
            _codesPending = 0;
            _remaining = 0;
            _currentMark = true;
            _currentEndsCode = false;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Placa de sunet s-a oprit: deblocheaza asteptarile cu eroare.</summary>
    public void Fail()
    {
        lock (_lock) { _failed = true; Monitor.PulseAll(_lock); }
    }

    /// <summary>Asteapta pana cand in coada sunt cel mult <paramref name="maxPending"/> coduri.</summary>
    public void WaitForRoom(int maxPending, CancellationToken ct) => WaitWhile(() => _codesPending > maxPending, ct);

    /// <summary>Asteapta pana cand coada s-a generat complet.</summary>
    public void WaitIdle(CancellationToken ct) => WaitWhile(() => _segments.Count > 0 || _remaining > 0, ct);

    private void WaitWhile(Func<bool> busy, CancellationToken ct)
    {
        lock (_lock)
        {
            while (busy())
            {
                ct.ThrowIfCancellationRequested();
                if (_failed) throw new IOException("iesirea audio s-a oprit");
                Monitor.Wait(_lock, 20);
            }
        }
    }

    /// <summary>Genereaza esantioane mono in [-Level, Level]. Fara date: Mark.</summary>
    public void Render(Span<float> buffer)
    {
        bool finished = false;
        double markStep = 2 * Math.PI * MarkHz / SampleRate;
        double spaceStep = 2 * Math.PI * SpaceHz / SampleRate;
        lock (_lock)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (_remaining <= 0)
                {
                    if (_segments.TryDequeue(out var segment))
                    {
                        _currentMark = segment.Mark;
                        _currentEndsCode = segment.EndsCode;
                        // restul fractionar al segmentului anterior se pastreaza: fara deriva
                        _remaining += segment.Bits * _bitSamples;
                    }
                    else
                    {
                        _currentMark = true;
                        _currentEndsCode = false;
                        _remaining = 0;
                    }
                }
                _phase += _currentMark ? markStep : spaceStep;
                if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
                double gain = _rampPos < _rampSamples ? 0.5 - 0.5 * Math.Cos(Math.PI * ++_rampPos / _rampSamples) : 1;
                buffer[i] = (float)(Level * gain * Math.Sin(_phase));
                if (_remaining > 0)
                {
                    _remaining -= 1;
                    if (_remaining <= 0)
                    {
                        if (_currentEndsCode) { _codesPending--; _currentEndsCode = false; }
                        finished = true;
                    }
                }
            }
            if (finished) Monitor.PulseAll(_lock);
        }
    }
}
