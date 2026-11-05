using RttyDecoder.Benchmarks;

namespace RttyDecoder.Core.Tests;

public class BenchmarkScoringTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "ABC", 3)]
    [InlineData("ABC", "", 3)]
    [InlineData("CQ DE W6IWI", "CQ DE W6IWI", 0)]
    [InlineData("CQ DE W6IWI", "CQ DE W6IWL", 1)]
    [InlineData("ABC", "ABXC", 1)]
    [InlineData("ABC", "AC", 1)]
    public void DistanceCompteInserariStergeriSiSubstitutii(string expected, string actual, int distance) =>
        Assert.Equal(distance, Scoring.Distance(expected, actual));

    [Fact]
    public void NormalizareaNuAscundeErorileDePunctuatie()
    {
        Assert.Equal("CQ DE W6IWI", Scoring.Normalize(" cq\r\n de  w6iwi\t"));
        Assert.Equal(1, Scoring.Distance(Scoring.Normalize("DOG'S"), Scoring.Normalize("DOGS")));
        Assert.Equal(2, Scoring.CountCall("W6IWI W6IWI. XW6IWI W6IWI/P W6IWIY", "W6IWI"));
    }

    [Fact]
    public void LipsaTranscrieriiNuEsteConfundataCuTextGol()
    {
        Assert.Null(Program.Reference(null));
        Assert.Equal("", Program.Reference("empty"));
        Assert.Equal(100, Program.Reference("numbered-w6iwi")!.Split('\n').Length);
    }
}
