using System.Text;
using RttyDecoder.Core.Baudot;

namespace RttyDecoder.Core.Tests;

public class BaudotCodecTests
{
    private static string DusIntors(string text, bool usTtyTx, bool usTtyRx)
    {
        var tx = new BaudotCodec { UsTty = usTtyTx };
        var rx = new BaudotCodec { UsTty = usTtyRx };
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            foreach (int cod in tx.Encode(c))
            {
                char d = rx.Decode(cod);
                if (d != '\0') sb.Append(d);
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void UsTtyApostrofSiGhilimele() =>
        Assert.Equal("DOG'S \"OK\" $5", DusIntors("DOG'S \"OK\" $5", true, true));

    [Fact]
    public void Ita2Neschimbat() => Assert.Equal("DOG'S 1+1", DusIntors("DOG'S 1+1", false, false));

    /// <summary>Apostroful US-TTY e FIGS+J; citit cu tabelul ITA2 iese BELL - motivul optiunii.</summary>
    [Fact]
    public void ApostrofUsTtyCitit()
    {
        var rx = new BaudotCodec();
        rx.Decode(0x1B);
        Assert.Equal('\u0007', rx.Decode(0x0B));
        rx.UsTty = true;
        Assert.Equal('\'', rx.Decode(0x0B));
    }

    [Fact]
    public void LtrsDupaRandInCifre()
    {
        var tx = new BaudotCodec();
        tx.Encode('5');
        Assert.Equal(new[] { 2, 8 }, tx.Encode('\n'));
        Assert.Equal(new[] { 0x1F, 0x07 }, tx.Encode('U'));
        Assert.Equal("599\nUR", DusIntors("599\nUR", false, false).Replace("\n\n", "\n"));
    }
}
