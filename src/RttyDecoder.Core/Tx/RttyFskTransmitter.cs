using RttyDecoder.Core.Baudot;

namespace RttyDecoder.Core.Tx;

/// <summary>Iesirea emisiei FSK: PTT-ul si pinul de manipulare (de obicei prin HamRig). Pentru AFSK vezi <see cref="IFrameOutput"/>.</summary>
public interface IFskOutput
{
    void SetPtt(bool on);
    /// <summary>Activeaza/dezactiveaza linia fizica FSK.</summary>
    void SetLine(bool active);
}

/// <summary>
/// Emisie FSK 45.45 baud, start + 5 biti LSB intai + 1.5 stop. Un singur fir, cu prioritate mare.
/// Mesajele si LIVE se pot adauga de pe orice fir; toate schimbarile cozii si ciclului de viata
/// sunt serializate. Activ = Mark, independent de NORM/REV la receptie; InvertFsk inverseaza pinul.
/// </summary>
public sealed class RttyFskTransmitter : IDisposable
{
    private volatile IFskOutput _output;
    private readonly ITxClock _clock;
    private readonly Queue<string> _messages = new();
    private readonly Queue<char> _live = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _lock = new();
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private volatile bool _liveOn;
    private volatile bool _transmitting;
    private volatile int _queuedChars;
    private bool _disposed;
    private bool _wakeDisposed;
    private bool _restartRequested;

    public RttyFskTransmitter(IFskOutput output, ITxClock? clock = null)
    {
        _output = output;
        _clock = clock ?? new StopwatchTxClock();
    }

    /// <summary>Iesirea (FSK pe pin sau AFSK pe placa de sunet); se citeste la inceputul fiecarei emisii.</summary>
    public IFskOutput Output
    {
        get => _output;
        set => _output = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Copiate la inceputul fiecarei emisii.</summary>
    public RttyTxSettings Settings { get; set; } = new();
    public bool IsTransmitting => _transmitting;
    public bool IsLive => _liveOn;
    /// <summary>Caractere in asteptare, fara cel deja inceput.</summary>
    public int QueuedCharacters => _queuedChars;
    /// <summary>PTT activat, pe firul emisiei.</summary>
    public event Action? Started;
    /// <summary>Iesirile au fost oprite/incercate; null = final normal, altfel motivul.</summary>
    public event Action<string?>? Stopped;
    /// <summary>Caracter complet emis logic (CR/LF ca newline); nu se raporteaza caractere intrerupte.</summary>
    public event Action<char>? CharacterSent;

    /// <summary>Normalizeaza in Baudot si adauga un mesaj. Porneste sau continua emisia.</summary>
    public void Send(string text)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string value = Normalize(text);
            if (value.Length == 0) return;
            _queuedChars += value.Length;
            _messages.Enqueue(value);
            EnsureRunning();
        }
    }

    /// <summary>Pastreaza emisia pornita; diddle intre caractere daca este activat.</summary>
    public void StartLive()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _liveOn = true;
            EnsureRunning();
        }
    }

    /// <summary>Adauga un caracter LIVE si porneste modul daca este necesar.</summary>
    public void SendLive(char c)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string value = Normalize(c.ToString());
            if (value.Length == 0) return;
            _queuedChars += value.Length;
            foreach (char item in value) _live.Enqueue(item);
            _liveOn = true;
            EnsureRunning();
        }
    }

    /// <summary>Iese din LIVE; caracterele deja in coada se termina normal.</summary>
    public void StopLive()
    {
        lock (_lock)
        {
            _liveOn = false;
            if (!_wakeDisposed) _wake.Set();
        }
    }

    /// <summary>Anuleaza si goleste coada; asteapta cel mult 2 s firul. Un driver blocat poate intarzia oprirea fizica.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            _liveOn = false;
            _restartRequested = false;
            _stop?.Cancel();
            ClearQueues();
            if (!_wakeDisposed) _wake.Set();
            thread = _thread;
        }
        if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
    }

    private void ClearQueues()
    {
        _messages.Clear();
        _live.Clear();
        _queuedChars = 0;
    }

    private string Normalize(string text)
    {
        var codec = new BaudotCodec { UsTty = Settings.UsTty };
        var result = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToUpperInvariant(text[i]);
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') continue;
                c = '\n';
            }
            if (c == '\t') c = ' ';
            if (codec.CanEncode(c)) result.Append(c);
        }
        return result.ToString();
    }

    // Sub _lock. Un fir nou poate porni abia dupa ce vechiul fir a oprit iesirile si ceasul.
    private void EnsureRunning()
    {
        if (_thread != null)
        {
            if (_stop?.IsCancellationRequested == true) _restartRequested = true;
            _wake.Set();
            return;
        }
        if (_messages.Count == 0 && _live.Count == 0 && !_liveOn) return;
        _stop = new CancellationTokenSource();
        var ct = _stop.Token;
        _thread = new Thread(() => Run(ct)) { IsBackground = true, Name = "RTTY-TX", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    private void Run(CancellationToken ct)
    {
        var s = Settings.Clone();
        var codec = new BaudotCodec { UsTty = s.UsTty };
        double bit = 1.0 / RttyTxSettings.Baud;
        string? reason = null;
        double time = 0, start = 0;
        bool precise = false;
        // AFSK: iesirea primeste caractere intregi si le da ritmul (ceasul placii de sunet).
        var output = _output;
        var frames = output as IFrameOutput;
        void Line(bool mark) { if (frames == null) output.SetLine(mark ^ s.InvertFsk); }
        void Wait(double target)
        {
            _clock.WaitUntil(target, ct);
            ct.ThrowIfCancellationRequested();
        }
        void Code(int code)
        {
            ct.ThrowIfCancellationRequested();
            if (frames != null)
            {
                frames.SendCode(code, ct);
                ct.ThrowIfCancellationRequested();
                if (_clock.Now - start > s.MaxTxSeconds) throw new TimeoutException();
                return;
            }
            if (_clock.Now - time > 0.015) time = _clock.Now;
            Line(false);
            time += bit;
            Wait(time);
            for (int i = 0; i < 5; i++)
            {
                Line(((code >> i) & 1) == 1);
                time += bit;
                Wait(time);
            }
            Line(true);
            time += bit * RttyTxSettings.StopBits;
            Wait(time);
            if (_clock.Now - start > s.MaxTxSeconds) throw new TimeoutException();
        }
        void Character(char c)
        {
            foreach (int code in codec.Encode(c)) Code(code);
            ct.ThrowIfCancellationRequested();
            CharacterSent?.Invoke(c);
        }

        try
        {
            _clock.BeginPrecise();
            precise = true;
            ct.ThrowIfCancellationRequested();
            output.SetPtt(true);
            _transmitting = true;
            Started?.Invoke();
            Line(true);
            time = start = _clock.Now;
            for (int i = 0; i < s.PreambleLtrs; i++) Code(BaudotCodec.Ltrs);
            if (s.AutoCrLf) Character('\n');
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string? message;
                char? live;
                lock (_lock)
                {
                    ct.ThrowIfCancellationRequested();
                    message = _messages.TryDequeue(out var value) ? value : null;
                    live = message == null && _live.TryDequeue(out char c) ? c : null;
                    if (live != null) _queuedChars--;
                }
                if (message != null)
                {
                    foreach (char c in message)
                    {
                        lock (_lock) { ct.ThrowIfCancellationRequested(); _queuedChars--; }
                        Character(c);
                    }
                    continue;
                }
                if (live is { } lc) { Character(lc); continue; }
                if (_liveOn)
                {
                    if (s.Diddle) { Code(BaudotCodec.Ltrs); codec.ResetShift(); }
                    else
                    {
                        Line(true);
                        _wake.WaitOne(20);
                        time = _clock.Now;
                        if (_clock.Now - start > s.MaxTxSeconds) throw new TimeoutException();
                    }
                    continue;
                }
                if (s.AutoCrLf) Character('\n');
                Code(BaudotCodec.Ltrs);
                Line(true);
                if (frames != null)
                {
                    frames.SendMark(s.TailMs / 1000.0, ct);
                    frames.Drain(ct);
                }
                else Wait(_clock.Now + s.TailMs / 1000.0);
                // Mesaj sosit in pauza finala: continua cu acelasi PTT si acelasi fir.
                lock (_lock) if (_messages.Count == 0 && _live.Count == 0 && !_liveOn) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { reason = "oprit"; }
        catch (TimeoutException) { reason = $"limita de {s.MaxTxSeconds} s atinsa"; }
        catch (Exception error) { reason = "eroare: " + error.Message; }
        finally
        {
            try { if (frames == null) output.SetLine(false); } catch { }
            try { output.SetPtt(false); } catch { }
            _transmitting = false;
            try { if (precise) _clock.EndPrecise(); }
            catch (Exception error) { reason ??= "eroare: " + error.Message; }
            bool more;
            lock (_lock)
            {
                bool restart = _restartRequested && !_disposed;
                if ((reason != null && !restart) || _disposed) { ClearQueues(); _liveOn = false; }
                more = !_disposed && (reason == null || restart) && (_messages.Count > 0 || _live.Count > 0 || _liveOn);
                _restartRequested = false;
                _stop?.Dispose();
                _stop = null;
                _thread = null;
                if (_disposed) DisposeWake();
            }
            try { Stopped?.Invoke(reason); }
            finally
            {
                lock (_lock) if (more && !_disposed) EnsureRunning();
            }
        }
    }

    private void DisposeWake()
    {
        if (_wakeDisposed) return;
        _wake.Dispose();
        _wakeDisposed = true;
    }

    /// <summary>Opreste emisia si respinge mesaje noi; handle-ul se elibereaza dupa iesirea unui driver blocat.</summary>
    public void Dispose()
    {
        lock (_lock) { if (_disposed) return; _disposed = true; }
        Stop();
        // Un driver poate ignora oprirea. Nu eliberam handle-ul folosit de firul inca viu.
        lock (_lock) if (_thread == null) DisposeWake();
    }
}
