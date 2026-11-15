namespace RttyDecoder.Core.Tx;

/// <summary>Tonurile unei emisii AFSK (frecvente audio).</summary>
public readonly record struct AfskTones(double MarkHz, double SpaceHz)
{
    /// <summary>
    /// Polaritatea standard pe aer: Mark este frecventa RF mai mare. In USB / DATA-U tonul audio mai
    /// mare iese mai sus pe RF, deci Mark = tonul de sus; in LSB / DATA-L invers.
    /// <paramref name="invert"/> inverseaza rezultatul (statii / corespondenti cu polaritate inversa).
    /// </summary>
    public static AfskTones ForSideband(double lowerHz, double shiftHz, bool upperSideband, bool invert = false)
    {
        double upper = lowerHz + shiftHz;
        bool markUpper = upperSideband ^ invert;
        return markUpper ? new(upper, lowerHz) : new(lowerHz, upper);
    }
}

/// <summary>Placa de sunet pentru AFSK: cere esantioane de la modulator pe firul ei.</summary>
public interface IAfskSink
{
    /// <summary>Rata la care cere esantioane (dupa deschidere).</summary>
    int SampleRate { get; }
    /// <summary>Intarzierea aproximativa dintre generare si iesirea fizica (secunde).</summary>
    double LatencySeconds { get; }
    /// <summary>Deschide placa si porneste redarea din modulator.</summary>
    void Start(AfskModulator modulator);
    void Stop();
    /// <summary>Redarea s-a oprit singura (placa scoasa, eroare de driver).</summary>
    event Action<Exception?>? Failed;
}

/// <summary>
/// Emisie AFSK: PTT-ul prin statie (CAT), tonurile pe placa de sunet. Tonurile si nivelul se citesc la
/// fiecare PTT, deci urmeaza perechea decodorului si modul statiei din acel moment.
/// </summary>
public sealed class AfskOutput : IFrameOutput
{
    private readonly IAfskSink _sink;
    private readonly Action<bool> _ptt;
    private readonly Func<AfskTones> _tones;
    private readonly Func<double> _level;
    private AfskModulator? _modulator;

    /// <summary>Mark inainte de primul caracter: timp pentru comutarea statiei pe emisie.</summary>
    public double LeadSeconds { get; set; } = 0.1;

    /// <summary>Caractere generate inainte de cel care se aude (latenta mica la Stop).</summary>
    public int MaxAhead { get; set; } = 1;

    public AfskOutput(IAfskSink sink, Action<bool> ptt, Func<AfskTones> tones, Func<double> level)
    {
        _sink = sink;
        _ptt = ptt;
        _tones = tones;
        _level = level;
        _sink.Failed += _ => _modulator?.Fail();
    }

    /// <summary>Modulatorul emisiei curente (null intre emisii).</summary>
    public AfskModulator? Modulator => _modulator;

    public void SetPtt(bool on)
    {
        if (on)
        {
            Stop();
            var tones = _tones();
            var mod = new AfskModulator(_sink.SampleRate, tones.MarkHz, tones.SpaceHz, _level());
            mod.EnqueueMark(LeadSeconds);
            _ptt(true);
            _modulator = mod;
            _sink.Start(mod);
        }
        else
        {
            try { Stop(); }
            finally { _ptt(false); }
        }
    }

    private void Stop()
    {
        var mod = _modulator;
        _modulator = null;
        mod?.Clear();
        mod?.Fail();
        if (mod != null) _sink.Stop();
    }

    /// <summary>AFSK nu foloseste pinul FSK.</summary>
    public void SetLine(bool active) { }

    private AfskModulator Current => _modulator ?? throw new InvalidOperationException("AFSK: PTT oprit");

    public void SendCode(int code, CancellationToken ct)
    {
        var mod = Current;
        mod.Enqueue(code);
        mod.WaitForRoom(MaxAhead, ct);
    }

    public void SendMark(double seconds, CancellationToken ct) => Current.EnqueueMark(seconds);

    public void Drain(CancellationToken ct)
    {
        Current.WaitIdle(ct);
        // ultimele esantioane sunt inca in bufferul placii
        double wait = _sink.LatencySeconds + 0.02;
        if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait))) ct.ThrowIfCancellationRequested();
    }
}
