using System.Collections.Concurrent;
using RttyDecoder.Core.Baudot;

namespace RttyDecoder.Core.Tx;

/// <summary>Iesirea emisiei FSK: PTT-ul si pinul de manipulare (de obicei prin HamRig).</summary>
public interface IFskOutput
{
    void SetPtt(bool on);

    /// <summary>Pinul FSK activ / inactiv.</summary>
    void SetLine(bool active);
}

/// <summary>
/// Emisia RTTY prin FSK: pinul de manipulare comutat la 45.45 baud (start, 5 biti, 1.5 stop), statia
/// in modul RTTY genereaza tonurile. Portat din aplicatia Android (EcranRttyDroid): preambul LTRS
/// dupa PTT, la sfarsitul mesajului LTRS si o pauza pe Mark, resincronizare daca firul ramane in
/// urma, pin activ = Mark (inversat la REV / dupa setari).
///
/// Doua moduri, folosite si impreuna: mesaje in coada (<see cref="Send"/> - Enter, macro-uri) si
/// LIVE (<see cref="StartLive"/>: caracterele pleaca pe masura ce se scriu, cu diddle intre ele).
/// Un singur fir de emisie, cu prioritate mare; toate metodele publice se pot apela de pe orice fir.
/// </summary>
public sealed class RttyFskTransmitter : IDisposable
{
    private readonly IFskOutput _output;
    private readonly ITxClock _clock;
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly ConcurrentQueue<char> _live = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _lock = new();
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private volatile bool _liveOn;
    private volatile bool _transmitting;
    private volatile int _queuedChars;

    public RttyFskTransmitter(IFskOutput output, ITxClock? clock = null)
    {
        _output = output;
        _clock = clock ?? new StopwatchTxClock();
    }

    /// <summary>Setarile; se citesc la inceputul fiecarei emisii.</summary>
    public RttyTxSettings Settings { get; set; } = new();

    /// <summary>Starea NORM/REV a receptiei (urmata daca <see cref="RttyTxSettings.FollowRxReverse"/>).</summary>
    public bool RxReverse { get; set; }

    public bool IsTransmitting => _transmitting;

    public bool IsLive => _liveOn;

    /// <summary>Caractere inca netrimise (mesaje + LIVE).</summary>
    public int QueuedCharacters => _queuedChars;

    /// <summary>Emisia a pornit (PTT on) / s-a oprit (motivul: null = normal).</summary>
    public event Action? Started;

    public event Action<string?>? Stopped;

    /// <summary>Un caracter a plecat (pentru ecou in terminal; CR LF ca '\n').</summary>
    public event Action<char>? CharacterSent;

    /// <summary>Mesaj in coada (Enter, macro). Porneste emisia daca nu e deja pornita.</summary>
    public void Send(string text)
    {
        string t = Normalize(text);
        if (t.Length == 0) return;
        _messages.Enqueue(t);
        Interlocked.Add(ref _queuedChars, t.Length);
        EnsureRunning();
    }

    /// <summary>LIVE: emisia porneste si ramane pornita (diddle) pana la <see cref="StopLive"/>.</summary>
    public void StartLive()
    {
        _liveOn = true;
        EnsureRunning();
    }

    /// <summary>Un caracter scris in LIVE.</summary>
    public void SendLive(char c)
    {
        string t = Normalize(c.ToString());
        if (t.Length == 0) return;
        foreach (char x in t) _live.Enqueue(x);
        Interlocked.Add(ref _queuedChars, t.Length);
        if (!_liveOn) StartLive();
        _wake.Set();
    }

    /// <summary>Iese din LIVE: se trimite ce a ramas, apoi emisia se termina normal.</summary>
    public void StopLive()
    {
        _liveOn = false;
        _wake.Set();
    }

    /// <summary>Oprire imediata: coada golita, pinul si PTT-ul oprite.</summary>
    public void Stop()
    {
        _liveOn = false;
        while (_messages.TryDequeue(out _)) { }
        while (_live.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _queuedChars, 0);
        CancellationTokenSource? cts;
        Thread? t;
        lock (_lock)
        {
            cts = _stop;
            t = _thread;
        }
        cts?.Cancel();
        _wake.Set();
        if (t != null && t != Thread.CurrentThread) t.Join(2000);
    }

    /// <summary>Litere mari, CR/LF ca '\n', fara caracterele care nu exista in Baudot.</summary>
    private string Normalize(string text)
    {
        var codec = new BaudotCodec { UsTty = Settings.UsTty };
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToUpperInvariant(text[i]);
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') continue;
                c = '\n';
            }
            if (c == '\t') c = ' ';
            if (codec.CanEncode(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private void EnsureRunning()
    {
        lock (_lock)
        {
            if (_thread != null)
            {
                _wake.Set();
                return;
            }
            _stop = new CancellationTokenSource();
            var ct = _stop.Token;
            _thread = new Thread(() => Run(ct)) { IsBackground = true, Name = "RTTY-TX", Priority = ThreadPriority.Highest };
            _thread.Start();
        }
    }

    private void Run(CancellationToken ct)
    {
        var s = Settings.Clone();
        var codec = new BaudotCodec { UsTty = s.UsTty };
        bool reverse = s.InvertFsk ^ (s.FollowRxReverse && RxReverse);
        double bit = 1.0 / RttyTxSettings.Baud;
        string? reason = null;
        double t = 0, start = 0;

        void Line(bool mark) => _output.SetLine(mark ^ reverse);

        void Code(int code)
        {
            // fir ramas in urma (> 15 ms): resincronizare, nu rafala de biti grabiti
            if (_clock.Now - t > 0.015) t = _clock.Now;
            Line(false);                                     // start = Space
            t += bit;
            _clock.WaitUntil(t, ct);
            for (int i = 0; i < 5 && !ct.IsCancellationRequested; i++)
            {
                Line(((code >> i) & 1) == 1);                // LSB intai, 1 = Mark
                t += bit;
                _clock.WaitUntil(t, ct);
            }
            Line(true);                                      // stop = Mark
            t += bit * RttyTxSettings.StopBits;
            _clock.WaitUntil(t, ct);
            if (_clock.Now - start > s.MaxTxSeconds) throw new TimeoutException();
        }

        void Char(char c)
        {
            foreach (int code in codec.Encode(c))
            {
                if (ct.IsCancellationRequested) return;
                Code(code);
            }
            CharacterSent?.Invoke(c);
        }

        _clock.BeginPrecise();
        try
        {
            _output.SetPtt(true);
            _transmitting = true;
            Started?.Invoke();
            Line(true);
            t = start = _clock.Now;
            for (int i = 0; i < s.PreambleLtrs && !ct.IsCancellationRequested; i++) Code(BaudotCodec.Ltrs);
            if (s.AutoCrLf) Char('\n');

            while (!ct.IsCancellationRequested)
            {
                if (_messages.TryDequeue(out string? msg))
                {
                    foreach (char c in msg)
                    {
                        if (ct.IsCancellationRequested) break;
                        Interlocked.Decrement(ref _queuedChars);
                        Char(c);
                    }
                    continue;
                }
                if (_live.TryDequeue(out char lc))
                {
                    Interlocked.Decrement(ref _queuedChars);
                    Char(lc);
                    continue;
                }
                if (_liveOn)
                {
                    if (s.Diddle)
                    {
                        Code(BaudotCodec.Ltrs);
                        // dupa LTRS-ul de diddle receptoarele sunt pe litere: o cifra urmatoare cere FIGS din nou
                        codec.ResetShift();
                    }
                    else
                    {
                        Line(true);
                        _wake.WaitOne(20);
                        t = _clock.Now;
                        if (_clock.Now - start > s.MaxTxSeconds) throw new TimeoutException();
                    }
                    continue;
                }
                break; // nimic de trimis si nu e LIVE: sfarsitul emisiei
            }

            if (!ct.IsCancellationRequested)
            {
                if (s.AutoCrLf) Char('\n');
                Code(BaudotCodec.Ltrs);
                Line(true);
                _clock.WaitUntil(_clock.Now + s.TailMs / 1000.0, ct);
            }
            else
            {
                reason = "oprit";
            }
        }
        catch (TimeoutException)
        {
            reason = $"limita de {s.MaxTxSeconds} s atinsa";
        }
        catch (Exception e)
        {
            reason = "eroare: " + e.Message;
        }
        finally
        {
            try { _output.SetLine(false); } catch { }
            try { _output.SetPtt(false); } catch { }
            _transmitting = false;
            _clock.EndPrecise();
            bool more;
            lock (_lock)
            {
                _thread = null;
                _stop = null;
                // ceva sosit chiar la sfarsit (dupa ultima verificare): o emisie noua
                more = reason == null && (!_messages.IsEmpty || !_live.IsEmpty || _liveOn);
            }
            if (reason != null)
            {
                while (_messages.TryDequeue(out _)) { }
                while (_live.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _queuedChars, 0);
                _liveOn = false;
            }
            Stopped?.Invoke(reason);
            if (more) EnsureRunning();
        }
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }
}
