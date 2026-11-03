using System.Windows;
using System.Windows.Threading;

namespace RttyDecoder.App;

public partial class App : Application
{
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "RTTY Decoder - eroare", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
