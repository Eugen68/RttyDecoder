using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RttyDecoder.Core.Tx;

/// <summary>Ceasul emisiei (abstract, ca testele sa ruleze fara sa astepte).</summary>
public interface ITxClock
{
    /// <summary>Timpul curent, in secunde de la un reper oarecare.</summary>
    double Now { get; }

    /// <summary>Asteapta pana la <paramref name="time"/> (sau iese imediat la oprire).</summary>
    void WaitUntil(double time, CancellationToken ct);

    /// <summary>Inceputul / sfarsitul unei emisii (ex. rezolutia timerului Windows).</summary>
    void BeginPrecise();

    void EndPrecise();
}

/// <summary>
/// Ceasul real: asteptare hibrida ca in Android - somn pana aproape de moment, apoi cedare, apoi
/// bucla scurta pentru precizie (un bit are 22 ms; timerul implicit Windows are pasi de 15.6 ms,
/// de aceea in timpul emisiei se cere rezolutia de 1 ms).
/// </summary>
public sealed class StopwatchTxClock : ITxClock
{
    private static readonly double TickSeconds = 1.0 / Stopwatch.Frequency;

    public double Now => Stopwatch.GetTimestamp() * TickSeconds;

    public void WaitUntil(double time, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            double left = time - Now;
            if (left <= 0) return;
            if (left > 0.003) Thread.Sleep((int)((left - 0.002) * 1000));
            else if (left > 0.0005) Thread.Yield();
            // sub 0.5 ms: bucla pura
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    public void BeginPrecise()
    {
        if (OperatingSystem.IsWindows()) timeBeginPeriod(1);
    }

    public void EndPrecise()
    {
        if (OperatingSystem.IsWindows()) timeEndPeriod(1);
    }
}
