namespace RttyDecoder.Core;

/// <summary>Detectorul de text.</summary>
public enum RttyDecoderType
{
    /// <summary>Rezonatoare Chebyshev pe Mark/Space (implicit).</summary>
    Chebyshev = 0,

    /// <summary>Corelatie pe durata unui bit (Matched Filter).</summary>
    MatchedFilter = 1,

    /// <summary>Modelul neural DeepRTTY (ONNX). Demodulatorul clasic ramane pornit pentru SNR si AFC.</summary>
    DeepRtty = 2,
}

/// <summary>Urmarirea frecventei.</summary>
public enum RttyTracking
{
    Off = 0,

    /// <summary>AFC lent (5% din eroarea filtrata la fiecare ~44 ms).</summary>
    Afc = 1,

    /// <summary>Urmarire rapida (25%).</summary>
    Pll = 2,
}

/// <summary>
/// Setarile receptorului RTTY. Clasa simpla, serializabila (System.Text.Json), ca aplicatia gazda sa
/// le poata salva unde vrea. Valorile implicite sunt cele ale aplicatiei Android.
/// </summary>
public sealed class RttySettings
{
    public const double ShiftHz = 170.0;
    public const double MinFilterWidthHz = 54.5;   // 1.2 x baud
    public const double MaxFilterWidthHz = 114.6;  // rCheb = 0.985
    public const double MinDnrWidthHz = 250;
    public const double MaxDnrWidthHz = 1500;
    public const double MinDnrWidthMatchedFilterHz = 400;

    /// <summary>Frecventa audio a tonului Mark (Hz).</summary>
    public double MarkFrequencyHz { get; set; } = 2125.0;

    /// <summary>REV: Space e SUB Mark (Mark = tonul de sus).</summary>
    public bool Reverse { get; set; }

    public RttyDecoderType Decoder { get; set; } = RttyDecoderType.Chebyshev;

    public RttyTracking Tracking { get; set; } = RttyTracking.Off;

    /// <summary>Noise Blanker (impulsuri QRN).</summary>
    public bool NoiseBlanker { get; set; }

    /// <summary>Pragul NB, multiplu al nivelului mediu (2..12).</summary>
    public double NoiseBlankerThreshold { get; set; } = 5.0;

    /// <summary>DNR: bandpass centrat intre Mark si Space.</summary>
    public bool Dnr { get; set; }

    public double DnrWidthHz { get; set; } = 250.0 + 9 * (1250.0 / 20.0); // ~812 Hz, ca pe Android (pasul 9)

    /// <summary>SNR gating: la energie prea mica decizia deriva spre Mark (idle).</summary>
    public bool SnrGating { get; set; }

    /// <summary>Pragul SNR-G, multiplu al podelei de zgomot (1..3).</summary>
    public double SnrGatingK { get; set; } = 1.3;

    /// <summary>ATC W7AY - ajuta la fading selectiv; doar pentru Chebyshev.</summary>
    public bool Atc { get; set; }

    /// <summary>Volantul de caracter (sincronizare pe RTTY continuu).</summary>
    public bool Flywheel { get; set; } = true;

    /// <summary>Sensibilitatea deciziei (0..0.20); sub prag bitul e considerat Mark.</summary>
    public double Sensitivity { get; set; } = 0.05;

    /// <summary>Latimea rezonatoarelor Chebyshev (Hz).</summary>
    public double FilterWidthHz { get; set; } = (1.0 - 0.992) * 48000.0 / (2 * Math.PI);

    /// <summary>Tabelul FIGS US-TTY in loc de ITA2.</summary>
    public bool UsTty { get; set; }

    public RttySettings Clone() => (RttySettings)MemberwiseClone();
}
