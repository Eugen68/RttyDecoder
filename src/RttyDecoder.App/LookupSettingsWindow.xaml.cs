using System.Text;
using System.Windows;
using System.Windows.Media;
using HamCallsign.Online;

namespace RttyDecoder.App;

/// <summary>
/// Conturile QRZ.com / HamQTH.com pentru verificarea online (ca in CwDecoder). Nu salveaza nimic
/// singura: MainWindow citeste valorile dupa ShowDialog() == true.
/// </summary>
public partial class LookupSettingsWindow : Window
{
    // clienti doar pentru "Testeaza": nu ating sesiunile verificatorului aplicatiei
    private readonly QrzLookupClient _testQrz = new(agent: "RttyDecoder");
    private readonly HamQthLookupClient _testHamQth = new(agent: "RttyDecoder");

    public LookupSettingsWindow(string qrzUsername, string qrzPassword, string hamQthUsername, string hamQthPassword)
    {
        InitializeComponent();
        QrzUserBox.Text = qrzUsername;
        QrzPasswordBox.Password = qrzPassword;
        HamQthUserBox.Text = hamQthUsername;
        HamQthPasswordBox.Password = hamQthPassword;
    }

    public string QrzUsername => QrzUserBox.Text.Trim();
    public string QrzPassword => QrzPasswordBox.Password;
    public string HamQthUsername => HamQthUserBox.Text.Trim();
    public string HamQthPassword => HamQthPasswordBox.Password;

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        bool qrz = QrzUsername.Length > 0 && QrzPassword.Length > 0;
        bool hamQth = HamQthUsername.Length > 0 && HamQthPassword.Length > 0;
        if (!qrz && !hamQth)
        {
            TestStatus.Foreground = (Brush)FindResource("Rtty.Muted");
            TestStatus.Text = "Completeaza macar un cont (utilizator + parola).";
            return;
        }
        TestButton.IsEnabled = false;
        TestStatus.Foreground = (Brush)FindResource("Rtty.Muted");
        TestStatus.Text = "Se testeaza...";
        var tq = qrz ? _testQrz.TestLoginAsync(QrzUsername, QrzPassword) : Task.FromResult<(bool, string?)>((false, null));
        var th = hamQth ? _testHamQth.TestLoginAsync(HamQthUsername, HamQthPassword) : Task.FromResult<(bool, string?)>((false, null));
        await Task.WhenAll(tq, th);

        bool fail = false;
        var sb = new StringBuilder();
        if (qrz)
        {
            var (ok, err) = tq.Result;
            fail |= !ok;
            sb.AppendLine(ok ? "QRZ.com: ✔ conectat" : $"QRZ.com: ✘ {err}");
        }
        else
        {
            sb.AppendLine("QRZ.com: necompletat");
        }
        if (hamQth)
        {
            var (ok, err) = th.Result;
            fail |= !ok;
            sb.Append(ok ? "HamQTH.com: ✔ conectat" : $"HamQTH.com: ✘ {err}");
        }
        else
        {
            sb.Append("HamQTH.com: necompletat");
        }
        TestStatus.Foreground = fail ? Brushes.IndianRed : (Brush)FindResource("Call.Known");
        TestStatus.Text = sb.ToString();
        TestButton.IsEnabled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
