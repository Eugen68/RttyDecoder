using RttyDecoder.Benchmarks;

namespace RttyDecoder.Core.Tests;

public class ReceptionProfileTests
{
    [Theory]
    [InlineData(RttyReceptionProfile.GoodSignal, "MatchedFilter")]
    [InlineData(RttyReceptionProfile.WeakSignal, "DeepRTTY")]
    [InlineData(RttyReceptionProfile.QrnFlutter, "Chebyshev")]
    public void ProfilulPastreazaAcordulSiFolosesteConfiguratiaMasurata(RttyReceptionProfile profile, string measured)
    {
        var settings = new RttySettings
        {
            MarkFrequencyHz = 1445.5, Reverse = true, UsTty = true, Tracking = RttyTracking.Afc,
            NoiseBlanker = true, Atc = true, Dnr = true, SnrGating = true, Flywheel = false,
            Squelch = false, SquelchLevel = 0.8, Sensitivity = 0.2, FilterWidthHz = 110
        };
        RttyProfiles.Apply(profile, settings);
        Assert.Equal(1445.5, settings.MarkFrequencyHz);
        Assert.True(settings.Reverse);
        Assert.True(settings.UsTty);
        Assert.Equal(RttyTracking.Afc, settings.Tracking);
        Assert.True(RttyProfiles.Matches(profile, settings));
        Assert.True(RttyProfiles.Matches(profile, Program.Settings(measured, 2123)));
        settings.Sensitivity = 0.12;
        Assert.False(RttyProfiles.Matches(profile, settings));
    }
}
