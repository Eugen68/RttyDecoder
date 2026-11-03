namespace RttyDecoder.Core.DeepRtty;

/// <summary>
/// Parametrii modelului DeepRTTY (e04, github.com/e04/DeepRTTY, MIT) - din model.onnx.json.
/// Modelul e inclus ca resursa in RttyDecoder.Core.dll.
/// </summary>
public static class DeepRttyConst
{
    public const string ModelResource = "RttyDecoder.Core.Models.model_rtty.onnx";

    public const int SampleRate = 3200;
    public const double CentruHz = 800.0;
    public const double Baud = 45.45;

    public const int FftLength = 128;
    public const int WinLength = 96;
    public const int HopLength = 24;
    public const int OutputHopLength = 48;

    /// <summary>Bin-urile 650-950 Hz: ceil(650 / 25) .. floor(950 / 25).</summary>
    public const int StartBin = 26;
    public const int StopBin = 39;
    public const int Bins = StopBin - StartBin;

    /// <summary>Indexul clasei blank; clasele 0..31 sunt codurile ITA2.</summary>
    public const int BlankIndex = 32;

    public const double NoiseLevelQuantile = 0.25;
    public const double SilentFrameReferenceQuantile = 0.9;
    public const float SilentFrameRelativeLevel = 0.01f;
    public const float NoiseLevelRayleighFactor = 0.7585276164409321f;
    public const float NoiseLevelMinRelativeToPeak = 0.001f;
    public const float NoiseLevelMin = 0.00001f;

    /// <summary>Fereastra de decodare: 12 s.</summary>
    public const int Window = 12 * SampleRate;

    /// <summary>Pasul ferestrelor: ~8 s, pe grila cadrelor de iesire (533 x 48 = 25584).</summary>
    public static readonly int Hop = (int)Math.Round(8.0 * SampleRate / OutputHopLength, MidpointRounding.AwayFromZero) * OutputHopLength;

    /// <summary>Marginea nesigura a fiecarei ferestre interioare: 0.5 s.</summary>
    public const int EdgeGuard = SampleRate / 2;

    /// <summary>Toleranta de imperechere intre ferestre: 0.45 dintr-un caracter (7.5 biti).</summary>
    public const double Tolerance = 0.45 * 7.5 / Baud * SampleRate;
}
