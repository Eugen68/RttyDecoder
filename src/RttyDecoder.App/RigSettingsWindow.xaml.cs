using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HamRig;

namespace RttyDecoder.App;

/// <summary>
/// Setari > Conexiune statie: direct (CAT Yaesu) sau OmniRig. Cat timp fereastra e deschisa,
/// MainWindow tine statia deconectata, ca butonul "Testeaza" sa poata deschide portul.
/// </summary>
public partial class RigSettingsWindow : Window
{
    private static readonly int[] Bauds = { 4800, 9600, 19200, 38400, 115200 };

    private static readonly (PttMethod Method, string Text)[] PttChoices =
    {
        (PttMethod.None, "Fara"),
        (PttMethod.Cat, "CAT (TX1;)"),
        (PttMethod.Rts, "RTS"),
        (PttMethod.Dtr, "DTR"),
    };

    private bool _busy;

    public RigSettingsWindow(RigSettings settings)
    {
        InitializeComponent();
        foreach (int b in Bauds) BaudBox.Items.Add(b);
        foreach (var (_, text) in PttChoices) PttBox.Items.Add(text);
        OmniRigBox.Items.Add("Rig 1");
        OmniRigBox.Items.Add("Rig 2");
        OmniPttBox.Items.Add("Fara");
        OmniPttBox.Items.Add("Prin OmniRig");
        FillPorts();

        CatPortBox.Text = settings.CatPort ?? (CatPortBox.Items.Count > 0 ? (string)CatPortBox.Items[0]! : "");
        BaudBox.SelectedItem = Bauds.Contains(settings.CatBaud) ? settings.CatBaud : 38400;
        PttBox.SelectedIndex = Array.FindIndex(PttChoices, c => c.Method == settings.Ptt);
        PttPortBox.Text = settings.PttPort ?? "";
        OmniRigBox.SelectedIndex = settings.OmniRigNumber == 2 ? 1 : 0;
        OmniPttBox.SelectedIndex = settings.Kind == RigKind.OmniRig && settings.Ptt != PttMethod.None ? 1 : 0;
        (settings.Kind switch { RigKind.YaesuCat => KindDirect, RigKind.OmniRig => KindOmni, _ => KindNone }).IsChecked = true;
        UpdatePanels();
    }

    /// <summary>Setarile alese (dupa ShowDialog() == true).</summary>
    public RigSettings Result => Read();

    private void FillPorts()
    {
        string cat = CatPortBox.Text, ptt = PttPortBox.Text;
        CatPortBox.Items.Clear();
        PttPortBox.Items.Clear();
        PttPortBox.Items.Add("");
        foreach (string p in RigSettings.GetPortNames())
        {
            CatPortBox.Items.Add(p);
            PttPortBox.Items.Add(p);
        }
        CatPortBox.Text = cat;
        PttPortBox.Text = ptt;
    }

    private RigSettings Read()
    {
        var kind = KindDirect.IsChecked == true ? RigKind.YaesuCat : KindOmni.IsChecked == true ? RigKind.OmniRig : RigKind.None;
        var s = new RigSettings
        {
            Kind = kind,
            CatPort = string.IsNullOrWhiteSpace(CatPortBox.Text) ? null : CatPortBox.Text.Trim().ToUpperInvariant(),
            CatBaud = BaudBox.SelectedItem is int b ? b : 38400,
            PttPort = string.IsNullOrWhiteSpace(PttPortBox.Text) ? null : PttPortBox.Text.Trim().ToUpperInvariant(),
            OmniRigNumber = OmniRigBox.SelectedIndex == 1 ? 2 : 1,
        };
        s.Ptt = kind == RigKind.OmniRig
            ? (OmniPttBox.SelectedIndex == 1 ? PttMethod.Cat : PttMethod.None)
            : PttBox.SelectedIndex >= 0 ? PttChoices[PttBox.SelectedIndex].Method : PttMethod.None;
        return s;
    }

    private void UpdatePanels()
    {
        if (DirectPanel == null) return;
        DirectPanel.IsEnabled = KindDirect.IsChecked == true;
        OmniPanel.IsEnabled = KindOmni.IsChecked == true;
        DirectPanel.Opacity = DirectPanel.IsEnabled ? 1 : 0.45;
        OmniPanel.Opacity = OmniPanel.IsEnabled ? 1 : 0.45;
        bool any = KindNone.IsChecked != true;
        TestButton.IsEnabled = any && !_busy;
        PttTestButton.IsEnabled = any && !_busy && Read().Ptt != PttMethod.None;
        PttPortBox.IsEnabled = PttBox.SelectedIndex >= 0 && PttChoices[PttBox.SelectedIndex].Method is PttMethod.Rts or PttMethod.Dtr;
    }

    private void OnKindChanged(object sender, RoutedEventArgs e) => UpdatePanels();

    private void OnPttChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

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

    private void OnTest(object sender, RoutedEventArgs e) => _ = RunTest(false);

    private void OnTestPtt(object sender, RoutedEventArgs e) => _ = RunTest(true);

    private async Task RunTest(bool ptt)
    {
        var s = Read();
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
            if (ptt && st.Connected)
            {
                rig.SetPtt(true);
                await Task.Delay(500);
                rig.SetPtt(false);
                read += "  |  PTT: comutat 0.5 s (statia a trecut pe TX?)";
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
        var s = Read();
        if (s.Kind == RigKind.YaesuCat && string.IsNullOrWhiteSpace(s.CatPort))
        {
            Status("Alege portul CAT.", false);
            return;
        }
        DialogResult = true;
    }
}
