using System.Globalization;
using System.Windows;
using RttyDecoder.Audio;
using RttyDecoder.Core.Tx;
using RttyDecoder.Wpf;

namespace RttyDecoder.App;

/// <summary>Setari > Setari transmisie (FSK / AFSK).</summary>
public partial class TxSettingsWindow : Window
{
    private readonly List<AudioDevice> _devices = new();
    private readonly RttyTxSettings _original;

    public TxSettingsWindow(RttyTxSettings settings)
    {
        InitializeComponent();
        _original = settings.Clone();
        _devices.Add(new AudioDevice("", "Placa implicita a sistemului", false));
        try { _devices.AddRange(WasapiAfskSink.ListDevices()); }
        catch (Exception e) { Error.Text = "Placile de sunet nu pot fi listate: " + e.Message; }
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
        AutoQsyBox.IsChecked = s.AutoQsy;
        InvertBox.IsChecked = s.InvertAfsk;
        (s.Method == TxMethod.Afsk ? Afsk : Fsk).IsChecked = true;
        var devices = new List<AudioDevice>(_devices);
        if (s.AfskDeviceId.Length > 0 && devices.All(d => d.Id != s.AfskDeviceId))
            devices.Add(new AudioDevice(s.AfskDeviceId, "(placa salvata, neconectata acum)", false));
        AfskDeviceBox.ItemsSource = devices;
        AfskDeviceBox.SelectedItem = devices.First(d => d.Id == s.AfskDeviceId);
        AfskLevelBox.Value = Math.Round(Math.Clamp(s.AfskLevel, 0.01, 1) * 100);
        UpdateMethod();
    }

    private void OnMethodChanged(object sender, RoutedEventArgs e) => UpdateMethod();

    private void UpdateMethod()
    {
        if (AfskPanel == null) return;   // in timpul InitializeComponent
        bool afsk = Afsk.IsChecked == true;
        AfskPanel.IsEnabled = afsk;
        AfskPanel.Opacity = AfskLevelHint.Opacity = afsk ? 1 : 0.5;
        AutoQsyBox.IsEnabled = !afsk;
    }

    private void OnLevelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AfskLevelText != null) AfskLevelText.Text = $"{AfskLevelBox.Value:0}%";
    }

    /// <summary>Implicit pentru emisie; placa si metoda raman cele alese (tin de statie, nu de reglaj).</summary>
    private void OnDefaults(object sender, RoutedEventArgs e)
    {
        var d = new RttyTxSettings
        {
            Method = Afsk.IsChecked == true ? TxMethod.Afsk : TxMethod.Fsk,
            AfskDeviceId = (AfskDeviceBox.SelectedItem as AudioDevice)?.Id ?? "",
        };
        Show(d);
    }

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
            AutoQsy = AutoQsyBox.IsChecked == true,
            InvertAfsk = InvertBox.IsChecked == true,
            InvertFsk = false,   // tine de statie: Conexiune statie
            Method = Afsk.IsChecked == true ? TxMethod.Afsk : TxMethod.Fsk,
            AfskDeviceId = (AfskDeviceBox.SelectedItem as AudioDevice)?.Id ?? "",
            AfskLevel = AfskLevelBox.Value / 100.0,
            UsTty = _original.UsTty,
        };
        DialogResult = true;
    }
}
