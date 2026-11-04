using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RttyDecoder.App;

public partial class App : Application
{
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // testul de fum din CI (--smoke): fara fereastra de mesaj, care l-ar bloca la nesfarsit
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--smoke");
        if (i >= 0)
        {
            try
            {
                if (i + 2 < args.Length) File.WriteAllText(args[i + 2], "[exception] " + e.Exception);
            }
            catch
            {
                // nimic de facut
            }
            e.Handled = true;
            Shutdown(3);
            return;
        }
        MessageBox.Show(e.Exception.Message, "RTTY Decoder - eroare", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
