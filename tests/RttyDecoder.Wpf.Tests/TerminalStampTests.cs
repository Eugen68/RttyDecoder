using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

/// <summary>Ora din terminal: periodic la :00 / :30, fara sa rupa traficul (pauza de 3 s, cel mult 10 s).</summary>
public class TerminalStampTests
{
    private static DateTime T(int m, int s, int ms = 0) => new(2026, 11, 15, 12, m, s, ms, DateTimeKind.Utc);

    [Fact]
    public void LimiteleLa00Si30()
    {
        Assert.Equal(T(34, 30), TerminalStamps.LastBoundary(T(34, 56)));
        Assert.Equal(T(34, 0), TerminalStamps.LastBoundary(T(34, 29, 999)));
        Assert.Equal(T(35, 0), TerminalStamps.LastBoundary(T(35, 0)));
    }

    [Fact]
    public void DupaLinisteOraLaPrimulText()
    {
        // ultimul text la :20, ultima ora la :00; textul nou la :41 (dupa :30, cu pauza)
        Assert.True(TerminalStamps.PeriodicDue(T(34, 41), T(34, 20), T(34, 0)));
    }

    [Fact]
    public void TraficContinuuAsteaptaPauza()
    {
        // :30 a trecut, textul curge (ultimul caracter acum 0.2 s), abia 4 s de la :30: nu rupe
        Assert.False(TerminalStamps.PeriodicDue(T(34, 34), T(34, 33, 800), T(34, 0)));
        // pauza de 3 s in trafic: ora
        Assert.True(TerminalStamps.PeriodicDue(T(34, 36), T(34, 33), T(34, 0)));
    }

    [Fact]
    public void Dupa10SecundeRupeOricum()
    {
        Assert.True(TerminalStamps.PeriodicDue(T(34, 40), T(34, 39, 850), T(34, 0)));
    }

    [Fact]
    public void ODataPePerioada()
    {
        // ora pusa la :36 pentru :30: urmatorul text nu mai primeste pana la :00
        Assert.False(TerminalStamps.PeriodicDue(T(34, 50), T(34, 45), T(34, 36)));
        Assert.True(TerminalStamps.PeriodicDue(T(35, 5), T(34, 59), T(34, 36)));
    }

    [Fact]
    public void OraRecentaInlocuiestePeCeaPeriodica()
    {
        // QSY / emisie la :20 (10 s inainte de :30): fara ora periodica la :30
        Assert.False(TerminalStamps.PeriodicDue(T(34, 45), T(34, 40), T(34, 20)));
        // la :10 (20 s inainte): ora periodica ramane
        Assert.True(TerminalStamps.PeriodicDue(T(34, 45), T(34, 40), T(34, 10)));
    }

    [Fact]
    public void Formatul()
    {
        Assert.Equal("[12:34:56]:", TerminalStamps.Header(T(34, 56), null));
        Assert.Equal("[12:34:56] [3505.20]:", TerminalStamps.Header(T(34, 56), "3505.20"));
    }

    [Theory]
    [InlineData(null, 3_505_200.0, true)]        // prima
    [InlineData(3_505_200.0, 3_505_209.0, false)] // 9 Hz: click mic, A-TUNE pe acelasi semnal
    [InlineData(3_505_200.0, 3_505_210.0, true)]
    [InlineData(3_505_200.0, 3_505_030.0, true)]  // NORM/REV: Mark-ul trece pe celalalt ton
    [InlineData(2125.0, 2120.0, false)]           // fara statie: tonul audio
    public void FrecventaDoarPeste10Hz(double? last, double now, bool stamp)
    {
        Assert.Equal(stamp, TerminalStamps.FrequencyChanged(last, now));
    }
}
