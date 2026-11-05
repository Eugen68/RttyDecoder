using RttyDecoder.Core.Tx;

namespace RttyDecoder.Core.Tests;

public class FskFailureTests
{
    private sealed class Clock : ITxClock, IDisposable
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public int BlockAt = -1;
        public bool ThrowBegin, ThrowEnd;
        public int Waits;
        public double Now { get; private set; }
        public void BeginPrecise() { if (ThrowBegin) throw new IOException("begin defect"); }
        public void EndPrecise() { if (ThrowEnd) throw new IOException("end defect"); }
        public void WaitUntil(double time, CancellationToken ct)
        {
            if (++Waits == BlockAt)
            {
                Entered.Set();
                WaitHandle.WaitAny([Release.WaitHandle, ct.WaitHandle], TimeSpan.FromSeconds(10));
            }
            if (!ct.IsCancellationRequested) Now = Math.Max(Now, time);
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    private sealed class Output : IFskOutput
    {
        public bool Ptt, Line;
        public int OnCount, OffAttempts, Calls;
        public bool Disconnect;
        public void SetPtt(bool on) { if (on) OnCount++; else OffAttempts++; Ptt = on; }
        public void SetLine(bool active)
        {
            if (Disconnect && ++Calls == 4) throw new IOException("port deconectat");
            Line = active;
        }
    }
    private static RttyFskTransmitter Tx(Output output, Clock clock) => new(output, clock)
    { Settings = new RttyTxSettings { AutoCrLf = false, PreambleLtrs = 0, TailMs = 300 } };

    [Fact]
    public void StopInMijloculCaracteruluiNuIlRaporteazaCaTrimis()
    {
        using var clock = new Clock { BlockAt = 3 };
        var output = new Output();
        using var tx = Tx(output, clock);
        var echo = new List<char>();
        string? reason = null;
        tx.CharacterSent += echo.Add;
        tx.Stopped += value => reason = value;
        tx.Send("ABC");
        Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(5)));
        tx.Stop();
        Assert.Empty(echo);
        Assert.Equal("oprit", reason);
        Assert.False(output.Ptt);
        Assert.False(output.Line);
        Assert.Equal(0, tx.QueuedCharacters);
    }

    [Fact]
    public void DeconectareaPortuluiOprestePttSiGolesteCoada()
    {
        using var clock = new Clock();
        var output = new Output { Disconnect = true };
        using var tx = Tx(output, clock);
        using var done = new ManualResetEventSlim();
        string? reason = null;
        tx.Stopped += value => { reason = value; done.Set(); };
        tx.Send("CQ DE YO9VA");
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Contains("deconectat", reason);
        Assert.True(output.OffAttempts > 0);
        Assert.False(output.Ptt);
        Assert.False(tx.IsTransmitting);
        Assert.Equal(0, tx.QueuedCharacters);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EroareaCeasuluiNuLasaTransmisiaBlocata(bool begin)
    {
        using var clock = new Clock { ThrowBegin = begin, ThrowEnd = !begin };
        var output = new Output();
        using var tx = Tx(output, clock);
        using var done = new ManualResetEventSlim();
        string? reason = null;
        tx.Stopped += value => { reason = value; done.Set(); };
        tx.Send("A");
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Contains("defect", reason);
        Assert.False(output.Ptt);
        Assert.False(tx.IsTransmitting);
        Assert.Equal(0, tx.QueuedCharacters);
        clock.ThrowBegin = clock.ThrowEnd = false;
        done.Reset();
        tx.Send("B");
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(reason);
    }

    [Fact]
    public void MesajSositInPauzaFinalaFolosesteAcelasiPtt()
    {
        using var clock = new Clock { BlockAt = 15 }; // 7 asteptari A + 7 LTRS + coada
        var output = new Output();
        using var tx = Tx(output, clock);
        using var done = new ManualResetEventSlim();
        var echo = new List<char>();
        tx.CharacterSent += echo.Add;
        tx.Stopped += _ => done.Set();
        tx.Send("A");
        Assert.True(clock.Entered.Wait(TimeSpan.FromSeconds(5)));
        tx.Send("B");
        clock.Release.Set();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 'A', 'B' }, echo);
        Assert.Equal(1, output.OnCount);
        Assert.False(output.Ptt);
    }

    [Fact]
    public void StopSiRepornireDinCallbackNuPierdeMesajulNou()
    {
        using var clock = new Clock();
        var output = new Output();
        using var tx = Tx(output, clock);
        using var done = new ManualResetEventSlim();
        var echo = new List<char>();
        int stops = 0;
        tx.CharacterSent += c =>
        {
            echo.Add(c);
            if (c == 'A') { tx.Stop(); tx.Send("B"); }
        };
        tx.Stopped += _ => { if (Interlocked.Increment(ref stops) == 2) done.Set(); };
        tx.Send("A");
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 'A', 'B' }, echo);
        Assert.Equal(2, output.OnCount);
        Assert.False(output.Ptt);
        Assert.Equal(0, tx.QueuedCharacters);
    }

    [Fact]
    public void DisposeRespingeMesajeNoi()
    {
        using var clock = new Clock();
        var tx = Tx(new Output(), clock);
        tx.Dispose();
        tx.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tx.Send("CQ"));
        Assert.Throws<ObjectDisposedException>(tx.StartLive);
        Assert.Throws<ObjectDisposedException>(() => tx.SendLive('A'));
        tx.Stop();
    }
}
