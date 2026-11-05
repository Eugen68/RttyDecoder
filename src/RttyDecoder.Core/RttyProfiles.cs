namespace RttyDecoder.Core;

public enum RttyReceptionProfile { GoodSignal, WeakSignal, QrnFlutter }

/// <summary>Configuratii de pornire masurate pe corpusul W6IWI, nu detectie automata a canalului.</summary>
public static class RttyProfiles
{
    public static void Apply(RttyReceptionProfile profile, RttySettings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var preset = Configuration(profile);
        // Acordul, polaritatea, AFC/PLL si alfabetul raman ale utilizatorului.
        target.Decoder = preset.Decoder;
        target.NoiseBlanker = preset.NoiseBlanker;
        target.NoiseBlankerThreshold = preset.NoiseBlankerThreshold;
        target.Dnr = preset.Dnr;
        target.DnrWidthHz = preset.DnrWidthHz;
        target.SnrGating = preset.SnrGating;
        target.SnrGatingK = preset.SnrGatingK;
        target.Atc = preset.Atc;
        target.Flywheel = preset.Flywheel;
        target.Sensitivity = preset.Sensitivity;
        target.FilterWidthHz = preset.FilterWidthHz;
        target.Squelch = preset.Squelch;
        target.SquelchLevel = preset.SquelchLevel;
    }

    public static bool Matches(RttyReceptionProfile profile, RttySettings value)
    {
        var expected = Configuration(profile);
        return value.Decoder == expected.Decoder && value.NoiseBlanker == expected.NoiseBlanker
            && value.NoiseBlankerThreshold == expected.NoiseBlankerThreshold && value.Dnr == expected.Dnr
            && value.DnrWidthHz == expected.DnrWidthHz && value.SnrGating == expected.SnrGating
            && value.SnrGatingK == expected.SnrGatingK && value.Atc == expected.Atc
            && value.Flywheel == expected.Flywheel && value.Sensitivity == expected.Sensitivity
            && value.FilterWidthHz == expected.FilterWidthHz && value.Squelch == expected.Squelch
            && value.SquelchLevel == expected.SquelchLevel;
    }

    private static RttySettings Configuration(RttyReceptionProfile profile) => new()
    {
        Decoder = profile switch
        {
            RttyReceptionProfile.GoodSignal => RttyDecoderType.MatchedFilter,
            RttyReceptionProfile.WeakSignal => RttyDecoderType.DeepRtty,
            RttyReceptionProfile.QrnFlutter => RttyDecoderType.Chebyshev,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        }
        // SQL 35%, volant activ; NB/ATC/DNR/SNR-G raman oprite. NB+ATC nu a imbunatatit
        // proba QRN din corpus. Se pot activa manual pentru alt tip de interferenta.
    };
}
