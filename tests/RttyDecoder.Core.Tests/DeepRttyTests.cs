using System.Text;
using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.DeepRtty;
using Xunit.Abstractions;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>Modelul se incarca o singura data pentru toate testele DeepRTTY.</summary>
public sealed class DeepRttyModelFixture : IDisposable
{
    public DeepRttyModel Model { get; } = DeepRttyModel.LoadEmbedded();

    public void Dispose() => Model.Dispose();
}

/// <summary>
/// DeepRTTY cap-coada pe RTTY sintetic de 48 kHz (port al DeepRttyTest.kt): RttyFrontEnd (NORM si
/// REV) -&gt; DeepRttyStream (ferestre de 12 s + coada provizorie) -&gt; modelul inclus -&gt; BaudotCodec.
/// </summary>
public class DeepRttyTests : IClassFixture<DeepRttyModelFixture>
{
    private const string TextDeep =
        "CQ CQ CQ DE YO9VA YO9VA YO9VA PSE K THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 1234567890 " +
        "UR RST 599 599 NAME EUGEN QTH PLOIESTI KN 35 73 SK ";

    private readonly DeepRttyModel _model;
    private readonly ITestOutputHelper _out;

    public DeepRttyTests(DeepRttyModelFixture fixture, ITestOutputHelper output)
    {
        _model = fixture.Model;
        _out = output;
    }

    /// <summary>Decodeaza ca ecranul RTTY: blocuri de 3840 esantioane, confirmate + coada de la final.</summary>
    private string DecodeazaDeep(short[] pcm, double markHz, bool reverse)
    {
        var fe = new RttyFrontEnd(48000);
        var stream = new DeepRttyStream(_model.Run);
        var rx = new BaudotCodec();
        var sb = new StringBuilder();
        const int bloc = 3840;
        var buf = new short[bloc];
        var iesire = new float[bloc / 15 + 2];
        double centru = markHz + (reverse ? -85.0 : 85.0);
        int pos = 0;
        while (pos < pcm.Length)
        {
            int n = Math.Min(bloc, pcm.Length - pos);
            Array.Copy(pcm, pos, buf, 0, n);
            pos += n;
            int m = fe.Process(buf, n, centru, reverse, iesire, 0);
            foreach (var t in stream.Push(iesire, m))
            {
                char c = rx.Decode(t.Index);
                if (c != '\0') sb.Append(c);
            }
        }
        foreach (var t in stream.Preview())
        {
            char c = rx.Decode(t.Index);
            if (c != '\0') sb.Append(c);
        }
        return sb.ToString();
    }

    private double Medie(double snr, bool reverse)
    {
        double s = 0;
        for (long seed = 1; seed <= 3; seed++)
        {
            var pcm = Generate(TextDeep + TextDeep, new Options { SnrDb = snr, Seed = seed, SpaceBelow = reverse });
            string d = DecodeazaDeep(pcm, MarkHz, reverse);
            // text + text: comparam a doua jumatate, ca sa nu conteze inceputul
            int i = d.IndexOf("SK ", StringComparison.Ordinal);
            string doua = i >= 0 ? d[(i + 3)..] : d;
            s += Cer(TextDeep, doua) / 3;
        }
        return s;
    }

    [Fact]
    public void SemnalSlabNorm()
    {
        var r = new[] { -3.0, -6.0, -9.0 }.Select(snr => (snr, cer: Medie(snr, false))).ToList();
        _out.WriteLine("DEEPRTTY NORM " + string.Join(", ", r.Select(x => $"{x.snr:+0;-0} dB: {x.cer * 100:0.0}%")));
        Assert.True(r[1].cer < 0.06, $"CER la -6 dB = {r[1].cer}");
    }

    [Fact]
    public void SemnalSlabReverse()
    {
        double c = Medie(-6.0, true);
        _out.WriteLine($"DEEPRTTY REV -6 dB: {c * 100:0.0}%");
        Assert.True(c < 0.06, $"CER REV la -6 dB = {c}");
    }
}
