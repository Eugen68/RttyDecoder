using System.Text;
using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.DeepRtty;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>DeepRTTY Rapid / Normal / Precis: acelasi text, confirmat mai repede cu pas mai mic.</summary>
public class DeepRttyLatencyTests : IClassFixture<DeepRttyModelFixture>
{
    private readonly DeepRttyModel _model;
    public DeepRttyLatencyTests(DeepRttyModelFixture fixture) => _model = fixture.Model;

    private (string Text, double MedianLatency) Decode(short[] pcm, DeepRttyLatency latency)
    {
        var fe = new RttyFrontEnd(48000);
        var stream = new DeepRttyStream(_model.Run, DeepRttyConst.HopFor((int)latency));
        var rx = new BaudotCodec();
        var sb = new StringBuilder();
        var lat = new List<double>();
        const int block = 3840;
        var buf = new short[block];
        var output = new float[block / 15 + 2];
        for (int pos = 0; pos < pcm.Length;)
        {
            int n = Math.Min(block, pcm.Length - pos);
            Array.Copy(pcm, pos, buf, 0, n);
            pos += n;
            int m = fe.Process(buf, n, MarkHz + 85, false, output, 0);
            foreach (var t in stream.Push(output, m))
            {
                lat.Add((stream.Total - t.Sample) / (double)DeepRttyConst.SampleRate);
                char c = rx.Decode(t.Index);
                if (c != '\0') sb.Append(c);
            }
        }
        foreach (var t in stream.Preview())
        {
            char c = rx.Decode(t.Index);
            if (c != '\0') sb.Append(c);
        }
        lat.Sort();
        return (sb.ToString(), lat[lat.Count / 2]);
    }

    [Fact]
    public void ShorterHopConfirmsSoonerWithTheSameText()
    {
        string text = Synthetic.Text + Synthetic.Text;
        var pcm = Generate(text, new Options { SnrDb = 0, Seed = 7 });
        var precis = Decode(pcm, DeepRttyLatency.Precis);
        var normal = Decode(pcm, DeepRttyLatency.Normal);
        var rapid = Decode(pcm, DeepRttyLatency.Rapid);
        Assert.True(Cer(text, precis.Text) < 0.01, precis.Text);
        Assert.True(Cer(text, normal.Text) < 0.01, normal.Text);
        Assert.True(Cer(text, rapid.Text) < 0.01, rapid.Text);
        // masurat: ~13 s / ~7 s / ~4 s
        Assert.True(normal.MedianLatency < precis.MedianLatency * 0.7, $"{normal.MedianLatency} vs {precis.MedianLatency}");
        Assert.True(rapid.MedianLatency < normal.MedianLatency, $"{rapid.MedianLatency} vs {normal.MedianLatency}");
        Assert.True(normal.MedianLatency < 8.5, normal.MedianLatency.ToString());
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(4.0)]
    [InlineData(8.0)]
    public void HopIsOnTheOutputFrameGrid(double seconds)
    {
        int hop = DeepRttyConst.HopFor(seconds);
        Assert.Equal(0, hop % DeepRttyConst.OutputHopLength);
        Assert.InRange(hop / (double)DeepRttyConst.SampleRate, seconds - 0.02, seconds + 0.02);
    }

    [Fact]
    public void InvalidHopIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeepRttyStream(_model.Run, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeepRttyStream(_model.Run, DeepRttyConst.Window + DeepRttyConst.OutputHopLength));
    }
}
