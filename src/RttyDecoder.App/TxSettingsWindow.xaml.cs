using System.Globalization;
using System.Windows;
using RttyDecoder.Core.Tx;

namespace RttyDecoder.App;

/// <summary>Setari > Setari transmisie (FSK).</summary>
public partial class TxSettingsWindow : Window
{
    public TxSettingsWindow(RttyTxSettings settings)
    {
        InitializeComponent();
        Show(settings);
    }

    public RttyTxSettings Result { get; private set; } = new();

    private void Show(RttyTxSettings s)
    {
        PreambleBox.Text = s.PreambleLtrs.ToString(CultureInfo.InvariantCulture);
        TailBox.Text = s.TailMs.ToString(CultureInfo.InvariantCulture);
        MaxBox.Text = s.MaxTxSeconds.ToString(CultureInfo.InvariantCulture);
        CrLfBox.IsChecked = s.AutoCrLf;
        DiddleBox.IsChecked = s.Diddle;
        InvertBox.IsChecked = s.InvertFsk;
    }

    private void OnDefaults(object sender, RoutedEventArgs e) => Show(new RttyTxSettings());

    private static bool Int(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!Int(PreambleBox.Text, 0, 20, out int pre)) { Error.Text = "Preambul: intre 0 si 20 de caractere."; return; }
        if (!Int(TailBox.Text, 0, 3000, out int tail)) { Error.Text = "Pauza la sfarsit: intre 0 si 3000 ms."; return; }
        if (!Int(MaxBox.Text, 10, 1800, out int max)) { Error.Text = "Limita: intre 10 si 1800 de secunde."; return; }
        Result = new RttyTxSettings
        {
            PreambleLtrs = pre,
            TailMs = tail,
            MaxTxSeconds = max,
            AutoCrLf = CrLfBox.IsChecked == true,
            Diddle = DiddleBox.IsChecked == true,
            InvertFsk = InvertBox.IsChecked == true,
        };
        DialogResult = true;
    }
}
