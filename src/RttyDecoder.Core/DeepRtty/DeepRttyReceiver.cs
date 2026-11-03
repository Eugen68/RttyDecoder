using System.Collections.Concurrent;
using System.Diagnostics;

namespace RttyDecoder.Core.DeepRtty;

/// <summary>
/// Decodorul DeepRTTY pentru receptie continua. Firul audio apeleaza <see cref="Feed"/> cu blocuri
/// de 48 kHz (pregatirea la 3200 Hz e ieftina si se face pe loc); decodarea ruleaza pe un fir
/// propriu, ca o inferenta de cateva zeci/sute de ms sa nu blocheze receptia.
///
/// Iesiri (pe firul de decodare): codurile ITA2 confirmate - o singura data fiecare - si, o data
/// pe secunda, codurile provizorii ale cozii inca neconfirmate (textul gri).
/// </summary>
public sealed class DeepRttyReceiver : IDisposable
{
    /// <summary>Textul provizoriu se re-decodeaza o data pe secunda.</summary>
    private const long ProvizoriuMs = 1000;

    /// <summary>O mutare a centrului mai mare decat atat e o reacordare, nu deriva AFC.</summary>
    private const double SaltAcordHz = 20.0;

    private readonly RttyFrontEnd _frontEnd = new(48000);
    private readonly DeepRttyStream _stream;
    private readonly BlockingCollection<object> _coada = new();
    private Thread? _worker;
    private volatile bool _ruleaza;
    private volatile bool _resetCerut;

    // doar pe firul audio
    private double _ultimulCentru = double.NaN;
    private bool _ultimulReverse;
    private float[] _iesire = Array.Empty<float>();

    /// <summary>Coduri ITA2 confirmate (pe firul de decodare).</summary>
    public event Action<int[]>? Confirmed;

    /// <summary>Coduri provizorii ale cozii neconfirmate (pe firul de decodare).</summary>
    public event Action<int[]>? Provisional;

    /// <summary>Eroare de inferenta (pe firul de decodare).</summary>
    public event Action<Exception>? Error;

    public DeepRttyReceiver(RttyInference inference)
    {
        _stream = new DeepRttyStream(inference);
    }

    public bool IsRunning => _ruleaza;

    public void Start()
    {
        if (_ruleaza) return;
        _ruleaza = true;
        _resetCerut = true;
        _worker = new Thread(Bucla) { Name = "DeepRTTY", IsBackground = true };
        _worker.Start();
    }

    public void Stop()
    {
        if (!_ruleaza) return;
        _ruleaza = false;
        _worker?.Join(2000);
        _worker = null;
        // un Flush in asteptare nu mai are cine sa-l proceseze: il eliberam
        while (_coada.TryTake(out var element))
        {
            if (element is ManualResetEventSlim g) g.Set();
        }
    }

    /// <summary>Arunca audio-ul si textul neconfirmat; textul deja confirmat ramane.</summary>
    public void Reset() => _resetCerut = true;

    /// <summary>
    /// Firul audio: un bloc PCM de 48 kHz. <paramref name="centruHz"/> = centrul semnalului RTTY
    /// (Mark +/- 85 Hz), <paramref name="reverse"/> = Mark e sus. O mutare brusca a acordului
    /// porneste decodarea de la zero; derivele mici ale AFC sunt urmarite din mers.
    /// </summary>
    public void Feed(short[] pcm, int n, double centruHz, bool reverse)
    {
        if (!_ruleaza) return;
        if (double.IsNaN(_ultimulCentru) || Math.Abs(centruHz - _ultimulCentru) > SaltAcordHz || reverse != _ultimulReverse)
        {
            _frontEnd.Reset();
            _resetCerut = true;
        }
        _ultimulCentru = centruHz;
        _ultimulReverse = reverse;
        if (_iesire.Length < n / 15 + 2) _iesire = new float[n / 15 + 2];
        int m = _frontEnd.Process(pcm, n, centruHz, reverse, _iesire, 0);
        if (m > 0) _coada.Add(_iesire.AsSpan(0, m).ToArray());
    }

    /// <summary>
    /// Sfarsitul unei surse finite (fisier WAV): asteapta decodarea a tot ce s-a primit si confirma
    /// si coada provizorie. Blocheaza pana termina (cel mult <paramref name="timeout"/>).
    /// </summary>
    public bool Flush(TimeSpan timeout)
    {
        if (!_ruleaza) return true;
        var gata = new ManualResetEventSlim(); // fara Dispose: firul de decodare il poate seta si dupa un timeout
        _coada.Add(gata);
        // verificam periodic: daca receptorul se opreste intre timp, nu mai asteptam degeaba
        var limita = DateTime.UtcNow + timeout;
        while (!gata.Wait(200))
        {
            if (!_ruleaza || DateTime.UtcNow > limita) return false;
        }
        return true;
    }

    private void Bucla()
    {
        var ceas = Stopwatch.StartNew();
        long ultimulProvizoriu = 0;
        while (_ruleaza)
        {
            try
            {
                _coada.TryTake(out var primul, 100);
                if (_resetCerut)
                {
                    _resetCerut = false;
                    _stream.Reset();
                    while (_coada.TryTake(out var aruncat))
                    {
                        if (aruncat is ManualResetEventSlim g) g.Set();
                    }
                    if (primul is ManualResetEventSlim g0) g0.Set();
                    Provisional?.Invoke(Array.Empty<int>());
                    continue;
                }
                if (primul != null)
                {
                    // tot ce s-a strans intre timp, intr-un singur push
                    var lot = new List<float[]>();
                    ManualResetEventSlim? flush = null;
                    object? element = primul;
                    while (element != null)
                    {
                        if (element is float[] f) lot.Add(f);
                        else if (element is ManualResetEventSlim g) { flush = g; break; }
                        if (!_coada.TryTake(out element)) element = null;
                    }
                    if (lot.Count > 0)
                    {
                        int total = lot.Sum(b => b.Length);
                        var audio = new float[total];
                        int o = 0;
                        foreach (var b in lot) { Array.Copy(b, 0, audio, o, b.Length); o += b.Length; }
                        var confirmate = _stream.Push(audio, total);
                        if (confirmate.Count > 0) Confirmed?.Invoke(confirmate.Select(t => t.Index).ToArray());
                    }
                    if (flush != null)
                    {
                        var coada = _stream.Preview();
                        if (coada.Count > 0) Confirmed?.Invoke(coada.Select(t => t.Index).ToArray());
                        _stream.Reset();
                        Provisional?.Invoke(Array.Empty<int>());
                        flush.Set();
                        continue;
                    }
                }
                long acum = ceas.ElapsedMilliseconds;
                if (acum - ultimulProvizoriu >= ProvizoriuMs)
                {
                    ultimulProvizoriu = acum;
                    var p = _stream.Preview();
                    Provisional?.Invoke(p.Select(t => t.Index).ToArray());
                }
            }
            catch (Exception e)
            {
                Error?.Invoke(e);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _coada.Dispose();
    }
}
