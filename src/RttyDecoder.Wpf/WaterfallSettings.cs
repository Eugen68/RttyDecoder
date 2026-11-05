namespace RttyDecoder.Wpf;

/// <summary>Setari serializabile. Versiunea 0 este scala FFT bruta din versiunile vechi.</summary>
public sealed class WaterfallSettings
{
    public int LevelScaleVersion { get; set; }
    public bool AutoFloor { get; set; } = true;
    public bool LockLevels { get; set; }
    public double FloorDb { get; set; } = -100;
    public double RangeDb { get; set; } = 40;
    public double Gamma { get; set; } = 1;
    public WaterfallPalette Palette { get; set; } = WaterfallPalette.Contrast;
    public int SpectrumSize { get; set; } = 512;
    public double HistorySeconds { get; set; } = 15;
    public double MinFrequencyHz { get; set; }
    public double MaxFrequencyHz { get; set; } = 4000;
    public bool ShowSpectrum { get; set; } = true;
    public bool FollowSignal { get; set; } = true;
    public bool SnapToSignal { get; set; }
    /// <summary>Marcajele perechilor RTTY gasite in banda (sus, pe waterfall).</summary>
    public bool ShowSignals { get; set; } = true;
    public double Height { get; set; } = 230;

    public static double NormalizedFloor(WaterfallSettings value)
    {
        double floor = double.IsFinite(value.FloorDb) ? value.FloorDb : -100;
        // FFT Hamming 512: sum(w)/2 = 138.01, deci 42.80 dB fata de amplitudinea de varf.
        if (value.LevelScaleVersion < 2) floor -= 20 * Math.Log10(new RttyDecoder.Core.Dsp.SimpleFft(512).WindowSum / 2);
        return Math.Clamp(floor, -160, 0);
    }
}

public sealed record SpectrumResolutionChoice(int Size, string Label);
public sealed record WaterfallDurationChoice(double Seconds, string Label);
public static class WaterfallOptions
{
    public static IReadOnlyList<SpectrumResolutionChoice> Resolutions { get; } = new[]
    {
        new SpectrumResolutionChoice(512, "Rapid · 512"), new SpectrumResolutionChoice(1024, "Detaliat · 1024")
    };
    public static IReadOnlyList<WaterfallDurationChoice> Durations { get; } = new[]
    {
        new WaterfallDurationChoice(8, "8 s"), new WaterfallDurationChoice(15, "15 s"), new WaterfallDurationChoice(30, "30 s")
    };
}
