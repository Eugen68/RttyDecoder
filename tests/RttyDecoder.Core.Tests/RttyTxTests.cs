using System.Diagnostics;
using System.Text;
using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.Tx;

namespace RttyDecoder.Core.Tests;

public class RttyTxTests
{
    /// <summary>Ceas accelerat: timpul emisiei curge de <c>speed</c> ori mai repede decat cel real (0 = instantaneu).</summary>
    private sealed class TestClock : ITxClock
    {
        private readonly double _speed;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _virtual;

        public TestClock(double speed = 0) => _speed = speed;

        public double Now => _speed == 0 ? Volatile.Read(ref _virtual) : _sw.Elapsed.TotalSeconds * _speed;

        public void WaitUntil(double time, CancellationToken ct)
        {
            if (_speed == 0)
            {
                if (time > _virtual) Volatile.Write(ref _virtual, time);
                return;
            }
            // asteptare precisa (o milisecunda reala ar fi ~1 bit in timpul accelerat)
            while (!ct.IsCancellationRequested && Now < time) Thread.SpinWait(20);
        }

        public void BeginPrecise() { }
        public void EndPrecise() { }
    }

    /// <summary>
    /// Ceas virtual "in pasi": timpul sare instantaneu, dar dupa <see cref="Allow"/> asteptari firul
    /// de emisie se opreste pana testul permite altele - rezultate exacte, fara dependenta de cat de
    /// incarcata e masina.
    /// </summary>
    private sealed class StepClock : ITxClock
    {
        private readonly object _lock = new();
        private double _now;
        private long _steps;

        public double Now
        {
            get { lock (_lock) return _now; }
        }

        /// <summary>Firul de emisie asteapta (a consumat toti pasii).</summary>
        public bool Blocked { get; private set; }

        public void Allow(long steps)
        {
            lock (_lock)
            {
                _steps = steps;
                Monitor.PulseAll(_lock);
            }
        }

        public void WaitUntil(double time, CancellationToken ct)
        {
            lock (_lock)
            {
                while (_steps <= 0 && !ct.IsCancellationRequested)
                {
                    Blocked = true;
                    Monitor.Wait(_lock, 20);
                }
                Blocked = false;
                _steps--;
                if (time > _now) _now = time;
            }
        }

        public void BeginPrecise() { }
        public void EndPrecise() { }
    }

    private static void WaitFor(Func<bool> cond)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!cond())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException();
            Thread.Sleep(2);
        }
    }

    private sealed class Recorder : IFskOutput
    {
        private readonly ITxClock _clock;
        public Recorder(ITxClock clock) => _clock = clock;
        public List<(double T, bool Line)> Line { get; } = new();
        public List<(double T, bool On)> Ptt { get; } = new();
        public bool LineNow { get; private set; }
        public bool PttNow { get; private set; }
        public void SetPtt(bool on) { lock (this) { Ptt.Add((_clock.Now, on)); PttNow = on; } }
        public void SetLine(bool active) { lock (this) { Line.Add((_clock.Now, active)); LineNow = active; } }
    }

    private static (RttyFskTransmitter Tx, Recorder Out, TestClock Clock, ManualResetEventSlim Done, List<string?> Reasons) Create(double speed = 0)
    {
        var clock = new TestClock(speed);
        var rec = new Recorder(clock);
        var tx = new RttyFskTransmitter(rec, clock);
        var done = new ManualResetEventSlim();
        var reasons = new List<string?>();
        tx.Stopped += r => { lock (reasons) reasons.Add(r); done.Set(); };
        return (tx, rec, clock, done, reasons);
    }

    /// <summary>Starea pinului la momentul t (din istoric).</summary>
    private static bool At(List<(double T, bool Line)> line, double t)
    {
        bool v = false;
        foreach (var (time, l) in line)
        {
            if (time > t) break;
            v = l;
        }
        return v;
    }

    /// <summary>UART invers: fronturile Mark->Space sunt biti de start; esantionare la mijlocul bitilor.</summary>
    private static string DecodeLine(List<(double T, bool Line)> line, bool markIsActive = true)
    {
        double bit = 1 / RttyTxSettings.Baud;
        var codec = new BaudotCodec();
        var sb = new StringBuilder();
        double end = line[^1].T;
        double t = line[0].T;
        while (t < end)
        {
            // urmatorul start: trecere pe Space
            // (ultima trecere pe inactiv e oprirea pinului la sfarsitul emisiei, nu un bit de start)
            int i = line.FindIndex(e => e.T > t && e.T < end && e.Line != markIsActive);
            if (i < 0) break;
            double s = line[i].T;
            int code = 0;
            for (int b = 0; b < 5; b++)
            {
                bool mark = At(line, s + (b + 1.5) * bit) == markIsActive;
                if (mark) code |= 1 << b;
            }
            Assert.True(At(line, s + 6.2 * bit) == markIsActive, "bitul de stop trebuie sa fie Mark");
            char c = codec.Decode(code);
            if (c != '\0') sb.Append(c);
            t = s + 6.5 * bit - 1e-6;
        }
        return sb.ToString();
    }

    [Fact]
    public void MesajTrimisSeDecodeazaInapoi()
    {
        var (tx, rec, _, done, reasons) = Create();
        var echo = new StringBuilder();
        tx.CharacterSent += c => echo.Append(c);
        tx.Send("cq de yo9va yo9va 599 5NN tu\n73");
        Assert.True(done.Wait(5000));
        Assert.Null(reasons.Single());

        string decoded = DecodeLine(rec.Line);
        Assert.Equal("CQ DE YO9VA YO9VA 599 5NN TU 73", Collapse(decoded));
        Assert.Equal("\nCQ DE YO9VA YO9VA 599 5NN TU\n73\n", echo.ToString());   // ecou: CR LF automat la capete

        // PTT pornit inainte de primul bit si oprit dupa coada de 300 ms, pinul lasat inactiv
        Assert.Equal(new[] { true, false }, rec.Ptt.Select(p => p.On));
        Assert.True(rec.Ptt[0].T <= rec.Line[0].T);
        Assert.True(rec.Ptt[1].T - rec.Line[^2].T >= 0.3 - 1e-9);
        Assert.False(rec.LineNow);
        Assert.False(tx.IsTransmitting);
    }

    private static string Collapse(string s) => string.Join(" ", s.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void DurataBitilor()
    {
        var (tx, rec, _, done, _) = Create();
        tx.Settings = new RttyTxSettings { PreambleLtrs = 0, AutoCrLf = false };
        tx.Send("E");                                         // E = 00001: start, 1, 0, 0, 0, 0, stop
        Assert.True(done.Wait(5000));
        double bit = 1 / RttyTxSettings.Baud;
        // primul front Mark->Space dupa preambul (aici fara): bitul de start al lui E
        int start = rec.Line.FindIndex(e => !e.Line);
        double t0 = rec.Line[start].T;
        Assert.True(At(rec.Line, t0 + 0.5 * bit) == false);     // start
        Assert.True(At(rec.Line, t0 + 1.5 * bit));              // bit 0 = 1
        for (int b = 1; b < 5; b++) Assert.False(At(rec.Line, t0 + (b + 1.5) * bit));
        Assert.True(At(rec.Line, t0 + 6.2 * bit));              // stop
        Assert.True(At(rec.Line, t0 + 7.4 * bit));              // stop de 1.5 biti (+ LTRS de final dupa)
    }

    /// <summary>
    /// Bucla completa: semnalul FSK emis de noi, transformat in tonuri (ce ar genera statia in
    /// RTTY: Mark 2125 Hz, Space 2295 Hz), decodat cu decodorul nostru.
    /// </summary>
    [Fact]
    public void BuclaPrinDecodor()
    {
        var (tx, rec, _, done, _) = Create();
        const string text = "CQ CQ DE YO9VA YO9VA PSE K\nTHE QUICK BROWN FOX 1234567890 RYRYRY";
        tx.Send(text);
        Assert.True(done.Wait(5000));

        const int fs = 11025;
        double t0 = rec.Ptt[0].T, t1 = rec.Ptt[1].T + 0.5;
        int n = (int)((t1 - t0) * fs);
        var audio = new float[n];
        double ph = 0;
        int k = 0;
        bool line = true;
        for (int i = 0; i < n; i++)
        {
            double t = t0 + (double)i / fs;
            while (k < rec.Line.Count && rec.Line[k].T <= t) line = rec.Line[k++].Line;
            ph += 2 * Math.PI * (line ? 2125.0 : 2295.0) / fs;
            audio[i] = (float)(0.3 * Math.Sin(ph));
        }

        using var rx = new RttyReceiver();
        rx.Update(s => s.MarkFrequencyHz = 2125);
        var sb = new StringBuilder();
        rx.TextReceived += x => { lock (sb) sb.Append(x); };
        rx.Start();
        for (int pos = 0; pos < audio.Length; pos += 1024) rx.Process(audio.AsSpan(pos, Math.Min(1024, audio.Length - pos)), fs);
        rx.Flush();
        rx.Stop();
        Assert.Contains("CQ CQ DE YO9VA YO9VA PSE K", sb.ToString());
        Assert.Contains("THE QUICK BROWN FOX 1234567890 RYRYRY", sb.ToString());
    }

    [Fact]
    public void RttyUInverseazaPinul()
    {
        // RTTY-U: statia inverseaza tonurile; pinul inversat => in aer polaritatea standard
        var (tx, rec, _, done, _) = Create();
        tx.Settings = new RttyTxSettings { FollowRxReverse = false };   // corectia nu depinde de bifa asta
        tx.RigInvertsTones = true;
        tx.Send("YO9VA");
        Assert.True(done.Wait(5000));
        Assert.False(rec.Line[0].Line);
        Assert.Equal("YO9VA", Collapse(DecodeLine(rec.Line, markIsActive: false)));

        // RTTY-L: nimic inversat
        var (tx2, rec2, _, done2, _) = Create();
        tx2.Send("YO9VA");
        Assert.True(done2.Wait(5000));
        Assert.True(rec2.Line[0].Line);
        Assert.Equal("YO9VA", Collapse(DecodeLine(rec2.Line)));
    }

    [Fact]
    public void ReverseInverseazaPinul()
    {
        var (tx, rec, _, done, _) = Create();
        tx.RxReverse = true;                                  // REV la receptie, urmat la emisie
        tx.Send("YO9VA");
        Assert.True(done.Wait(5000));
        Assert.False(rec.Line[0].Line);                       // primul Mark = pin inactiv
        Assert.Equal("YO9VA", Collapse(DecodeLine(rec.Line, markIsActive: false)));

        var (tx2, rec2, _, done2, _) = Create();
        tx2.Settings = new RttyTxSettings { InvertFsk = true };
        tx2.RxReverse = true;                                 // inversare + REV = normal
        tx2.Send("YO9VA");
        Assert.True(done2.Wait(5000));
        Assert.Equal("YO9VA", Collapse(DecodeLine(rec2.Line)));
    }

    [Fact]
    public void LiveCuDiddle()
    {
        var clock = new StepClock();
        var rec = new Recorder(clock);
        using var tx = new RttyFskTransmitter(rec, clock) { Settings = new RttyTxSettings { MaxTxSeconds = 100000 } };
        var done = new ManualResetEventSlim();
        tx.Stopped += _ => done.Set();
        var echo = new StringBuilder();
        tx.CharacterSent += c => { lock (echo) echo.Append(c); };

        clock.Allow(7 * 12);                                  // preambul, CR LF si ~8 LTRS de diddle (7 asteptari pe cod)
        tx.StartLive();
        WaitFor(() => clock.Blocked);
        Assert.True(tx.IsTransmitting);
        int diddleEvents = rec.Line.Count;
        foreach (char c in "YO9VA") tx.SendLive(c);
        tx.StopLive();
        clock.Allow(long.MaxValue);
        Assert.True(done.Wait(5000));

        lock (echo) Assert.Equal("\nYO9VA\n", echo.ToString());
        Assert.True(diddleEvents > 7 * 10);                   // diddle inainte de text
        Assert.Equal("YO9VA", Collapse(DecodeLine(rec.Line)));
        Assert.False(tx.IsTransmitting);
        Assert.False(rec.PttNow);
    }

    [Fact]
    public void StopImediat()
    {
        var (tx, rec, _, done, reasons) = Create(speed: 10);
        tx.Send(new string('R', 200));
        Thread.Sleep(100);
        Assert.True(tx.IsTransmitting);
        tx.Stop();
        Assert.True(done.Wait(5000));
        Assert.Equal("oprit", reasons.Single());
        Assert.False(rec.PttNow);
        Assert.False(rec.LineNow);
        Assert.Equal(0, tx.QueuedCharacters);
    }

    [Fact]
    public void LimitaDeTimp()
    {
        var (tx, rec, _, done, reasons) = Create();
        tx.Settings = new RttyTxSettings { MaxTxSeconds = 2 };
        tx.Send(new string('R', 100));                        // ~16 s
        Assert.True(done.Wait(5000));
        Assert.Contains("limita", reasons.Single());
        Assert.False(rec.PttNow);
        Assert.True(rec.Ptt[^1].T - rec.Ptt[0].T < 2.5);
    }

    [Fact]
    public void MesajeleSeAdunaIntrOSinguraEmisie()
    {
        var clock = new StepClock();
        var rec = new Recorder(clock);
        using var tx = new RttyFskTransmitter(rec, clock);
        var done = new ManualResetEventSlim();
        tx.Stopped += _ => done.Set();
        clock.Allow(0);
        tx.Send("CQ");
        WaitFor(() => clock.Blocked);                         // emisia a pornit cu primul mesaj
        tx.Send("DE YO9VA");                                  // al doilea vine in timpul emisiei
        Assert.Equal(10, tx.QueuedCharacters);
        clock.Allow(long.MaxValue);
        Assert.True(done.Wait(5000));
        Assert.Equal(2, rec.Ptt.Count);                       // un singur PTT on/off
        Assert.Equal("CQDE YO9VA", Collapse(DecodeLine(rec.Line)));
    }

    [Fact]
    public void CaractereInexistenteInBaudot()
    {
        var (tx, rec, _, done, _) = Create();
        tx.Send("YO9VA @ é ~ ok");
        Assert.True(done.Wait(5000));
        Assert.Equal("YO9VA OK", Collapse(DecodeLine(rec.Line)));
    }
}
