namespace RttyDecoder.Wpf;

/// <summary>Reglajele waterfall-ului (se salveaza direct cu System.Text.Json).</summary>
public sealed class WaterfallSettings
{
    /// <summary>Podeaua urmareste singura zgomotul.</summary>
    public bool AutoFloor { get; set; } = true;

    /// <summary>Podeaua manuala (dB).</summary>
    public double FloorDb { get; set; } = -100;

    /// <summary>Intervalul de culoare peste podea (dB).</summary>
    public double RangeDb { get; set; } = 40;

    /// <summary>Gama paletei (1 = ca in CwDecoder).</summary>
    public double Gamma { get; set; } = 1.0;
}
