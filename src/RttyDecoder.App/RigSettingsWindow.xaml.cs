using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HamRig;

namespace RttyDecoder.App;

/// <summary>
/// Setari > Conexiune statie: direct (CAT Yaesu) sau OmniRig, plus portul de manipulare si PTT.
/// PTT-ul se alege intr-un singur loc (pinul portului CAT sau al portului de manipulare); fara pin,
/// la conexiunea directa emisia se comanda prin CAT, la OmniRig nu exista PTT. Cat timp fereastra
/// e deschisa, MainWindow tine statia deconectata, ca "Testeaza" sa poata deschide porturile.
/// </summary>
public partial class RigSettingsWindow : Window
{
    private static readonly int[] Bauds = { 4800, 9600, 19200, 38400, 115200 };

    private static readonly (SerialPin Pin, string Text)[] Pins =
    {
        (SerialPin.None, "Fara"),
        (SerialPin.Rts, "RTS"),
        (SerialPin.Dtr, "DTR"),
    };

    private bool _busy;
    private bool _updating;

    public RigSettingsWindow(RigSettings settings)
    {
        InitializeComponent();
        _updating = true;
        foreach (int b in Bauds)
        {
            BaudBox.Items.Add(b);
            KeyBaudBox.Items.Add(b);
        }
        foreach (var (_, text) in Pins)
        {
            CatPttBox.Items.Add(text);
            KeyPttBox.Items.Add(text);
            KeyPinBox.Items.Add(text);
        }
        OmniRigBox.Items.Add("Rig 1");
        OmniRigBox.Items.Add("Rig 2");
        FillPorts();

        CatPortBox.Text = settings.CatPort ?? (CatPortBox.Items.Count > 0 ? (string)CatPortBox.Items[0]! : "");
        BaudBox.SelectedItem = Bauds.Contains(settings.CatBaud) ? settings.CatBaud : 38400;
        CatPttBox.SelectedIndex = Index(settings.CatPttPin);
        OmniRigBox.SelectedIndex = settings.OmniRigNumber == 2 ? 1 : 0;
        KeyPortBox.Text = settings.KeyPort ?? "";
        KeyBaudBox.SelectedItem = Bauds.Contains(settings.KeyBaud) ? settings.KeyBaud : 4800;
        KeyPttBox.SelectedIndex = Index(settings.KeyPortPttPin);
        KeyPinBox.SelectedIndex = Index(settings.KeyPin);
        (settings.Kind switch { RigKind.YaesuCat => KindDirect, RigKind.OmniRig => KindOmni, _ => KindNone }).IsChecked = true;
        _updating = false;
        UpdatePanels();
    }

    /// <summary>Setarile alese (dupa ShowDialog() == true).</summary>
    public RigSettings Result => Read();

    private static int Index(SerialPin pin) => Math.Max(0, Array.FindIndex(Pins, p => p.Pin == pin));

    private static SerialPin Pin(ComboBox box) => box.SelectedIndex >= 0 ? Pins[box.SelectedIndex].Pin : SerialPin.None;

    private static string? Port(ComboBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim().ToUpperInvariant();

    private void FillPorts()
    {
        string cat = CatPortBox.Text, key = KeyPortBox.Text;
        CatPortBox.Items.Clear();
        KeyPortBox.Items.Clear();
        KeyPortBox.Items.Add("");
        foreach (string p in RigSettings.GetPortNames())
        {
            CatPortBox.Items.Add(p);
            KeyPortBox.Items.Add(p);
        }
        CatPortBox.Text = cat;
        KeyPortBox.Text = key;
    }

    private RigKind Kind => KindDirect.IsChecked == true ? RigKind.YaesuCat : KindOmni.IsChecked == true ? RigKind.OmniRig : RigKind.None;

    private RigSettings Read() => new()
    {
        Kind = Kind,
        CatPort = Port(CatPortBox),
        CatBaud = BaudBox.SelectedItem is int b ? b : 38400,
        // la OmniRig portul CAT e al lui OmniRig: pinul lui nu se foloseste
        CatPttPin = Kind == RigKind.YaesuCat ? Pin(CatPttBox) : SerialPin.None,
        OmniRigNumber = OmniRigBox.SelectedIndex == 1 ? 2 : 1,
        KeyPort = Port(KeyPortBox),
        KeyBaud = KeyBaudBox.SelectedItem is int kb ? kb : 4800,
        KeyPortPttPin = Pin(KeyPttBox),
        KeyPin = Pin(KeyPinBox),
    };

    private void UpdatePanels()
    {
        if (_updating || DirectPanel == null) return;
        _updating = true;
        try
        {
            bool direct = Kind == RigKind.YaesuCat, omni = Kind == RigKind.OmniRig, any = Kind != RigKind.None;
            SetEnabled(DirectPanel, direct);
            SetEnabled(OmniPanel, omni);
            SetEnabled(KeyPanel, any);

            // PTT intr-un singur loc: daca e ales un pin intr-o parte, cealalta trece pe Fara si nu se mai poate alege
            bool catPtt = direct && Pin(CatPttBox) != SerialPin.None;
            bool keyPtt = Pin(KeyPttBox) != SerialPin.None;
            if (catPtt && keyPtt) KeyPttBox.SelectedIndex = 0;
            keyPtt = Pin(KeyPttBox) != SerialPin.None;
            CatPttBox.IsEnabled = direct && !keyPtt;
            KeyPttBox.IsEnabled = any && !catPtt;

            var s = Read();
            TestButton.IsEnabled = any && !_busy;
            // fara pin: la conexiunea directa PTT prin CAT; la OmniRig nu exista PTT
            PttTestButton.IsEnabled = any && !_busy && (direct || s.PttLine.Pin != SerialPin.None);
            KeyTestButton.IsEnabled = any && !_busy && s.HasKeying;
        }
        finally
        {
            _updating = false;
        }
    }

    private static void SetEnabled(UIElement panel, bool on)
    {
        panel.IsEnabled = on;
        panel.Opacity = on ? 1 : 0.45;
    }

    private void OnKindChanged(object sender, RoutedEventArgs e) => UpdatePanels();

    private void OnPinChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

    private void OnRefreshPorts(object sender, RoutedEventArgs e) => FillPorts();

    private void Status(string text, bool? ok)
    {
        TestStatus.Text = text;
        TestStatus.Foreground = ok switch
        {
            true => (Brush)FindResource("Call.Known"),
            false => Brushes.IndianRed,
            null => (Brush)FindResource("Rtty.Muted"),
        };
    }

    private enum Test { Read, Ptt, Key }

    private void OnTest(object sender, RoutedEventArgs e) => _ = RunTest(Test.Read);

    private void OnTestPtt(object sender, RoutedEventArgs e) => _ = RunTest(Test.Ptt);

    private void OnTestKey(object sender, RoutedEventArgs e) => _ = RunTest(Test.Key);

    private async Task RunTest(Test test)
    {
        var s = Read();
        if (s.Validate() is { } problem)
        {
            Status(problem, false);
            return;
        }
        if (s.CreateRig() is not { } rig) return;
        _busy = true;
        UpdatePanels();
        Status("Se conecteaza...", null);
        try
        {
            await rig.ConnectAsync();
            await Task.Delay(300); // modul si starea TX vin imediat dupa frecventa
            var st = rig.State;
            string read = st.Connected
                ? $"✔ {RigMath.FormatFrequency(st.FrequencyHz)} {RigMath.ModeName(st.Mode)} - {rig.Description}"
                : "✘ " + st.StatusText;
            if (test == Test.Ptt && st.Connected)
            {
                rig.SetPtt(true);
                await Task.Delay(500);
                rig.SetPtt(false);
                var (port, pin) = s.PttLine;
                read += pin == SerialPin.None
                    ? "  |  PTT prin CAT (TX1;) 0.5 s"
                    : $"  |  PTT: {pin} pe {port} 0.5 s";
            }
            if (test == Test.Key && st.Connected)
            {
                rig.SetKey(true);
                await Task.Delay(500);
                rig.SetKey(false);
                read += $"  |  manipulare: {s.KeyPin} pe {s.KeyPort} 0.5 s";
            }
            Status(read, st.Connected);
        }
        catch (Exception ex)
        {
            Status("✘ " + ex.Message, false);
        }
        finally
        {
            rig.Dispose();
            _busy = false;
            UpdatePanels();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Read().Validate() is { } problem)
        {
            Status(problem, false);
            return;
        }
        DialogResult = true;
    }
}
