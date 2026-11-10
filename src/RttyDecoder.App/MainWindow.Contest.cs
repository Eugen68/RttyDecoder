using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RttyDecoder.Wpf;
using RttyDecoder.Wpf.Contest;

namespace RttyDecoder.App;

public partial class MainWindow
{
    private readonly string? _contestSmoke;
    private ContestViewModel? _contestModel;
    private ContestWindow? _contestWindow;
    private bool _contestAboveDecoder = true;

    private void InitializeContest()
    {
        InputBindings.Add(new KeyBinding(new RelayCommand(ShowContestWindow), Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        _vm.CallsignSelected += call => { if (_contestWindow?.IsVisible == true) _contestModel?.SelectCallsign(call.Full); };
        _vm.PropertyChanged += (_, e) =>
        {
            if (_contestWindow?.IsVisible == true && e.PropertyName == nameof(RttyDecoderViewModel.RigText)) _contestModel?.SyncFrequency();
        };
    }
    private void OnOpenContest(object sender, RoutedEventArgs e) => ShowContestWindow();
    private void ShowContestWindow()
    {
        try
        {
            if (_contestWindow != null)
            {
                if (_contestWindow.WindowState == WindowState.Minimized) _contestWindow.WindowState = WindowState.Normal;
                _contestWindow.Activate(); return;
            }
            _contestModel ??= new(new ContestFiles(), ContestStationResolver.Load(CallsignCache), new DecoderContestHost(_vm));
            var assembly = typeof(MainWindow).Assembly;
            var window = new ContestWindow(_contestModel, ApplicationVersion(assembly), BuildInfo(assembly))
            {
                Owner = _contestAboveDecoder ? this : null,
                KeepAboveDecoder = _contestAboveDecoder
            };
            window.KeepAboveDecoderChanged += (_, _) =>
            {
                _contestAboveDecoder = window.KeepAboveDecoder;
                window.Owner = _contestAboveDecoder ? this : null;
                if (!_contestAboveDecoder) Activate();
            };
            window.Closed += (_, _) => _contestWindow = null;
            _contestWindow = window;
            window.Show(); // deliberately modeless: the decoder remains interactive
        }
        catch (Exception error) { _vm.Message = "Contest: " + error.Message; }
    }
    private void OnContestDefinitionsFolder(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(ContestFiles.DefaultRoot, "UserDefinedContests"));
    private void OnContestSupportFolder(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(ContestFiles.DefaultRoot, "SupportFiles"));
    private void OnContestSessionsFolder(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(ContestFiles.DefaultRoot, "Contests", "Sessions"));

    /// <summary>CI uses an isolated data root and a host that cannot transmit or touch CAT/PTT.</summary>
    private async Task RunContestSmoke(string directory)
    {
        int code = 0;
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        try
        {
            await _callsignData;
            var files = new ContestFiles(Path.Combine(directory, "profile"));
            _contestModel = new(files, ContestStationResolver.Load(), new SmokeContestHost()) { OwnCall = "YO9VA" };
            ShowContestWindow();
            var firstWindow = _contestWindow ?? throw new InvalidOperationException("Contest window did not open.");
            if (!IsEnabled || !firstWindow.IsVisible || firstWindow.Owner != this || !firstWindow.KeepAboveDecoder || firstWindow.Topmost) throw new InvalidOperationException("Contest must be a separate modeless owned window.");
            ShowContestWindow();
            if (_contestWindow != firstWindow) throw new InvalidOperationException("Duplicate contest window.");
            _contestModel.CreateSession();
            _contestModel.Entry.UseCat = false; _contestModel.Entry.FrequencyKhz = "14080";
            _contestModel.SelectCallsign("YO3ABC"); _contestModel.Entry.Serial = "12"; _contestModel.Entry.Exchange = "BU";
            _contestModel.ReserveSerial(); _contestModel.LogContact();
            _contestModel.CabrilloHeaders = "CATEGORY-OPERATOR: SINGLE-OP\nCATEGORY-BAND: ALL\nCATEGORY-POWER: LOW\nCATEGORY-ASSISTED: NON-ASSISTED\nCATEGORY-TRANSMITTER: ONE";
            File.WriteAllText(Path.Combine(directory, "contest.adi"), _contestModel.ExportAdif());
            File.WriteAllText(Path.Combine(directory, "contest.log"), _contestModel.ExportCabrillo());
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            firstWindow.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)firstWindow.ActualWidth, (int)firstWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(firstWindow);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, "contest-window.png"))) encoder.Save(output);
            firstWindow.KeepAboveDecoder = false;
            if (firstWindow.Owner != null || firstWindow.Topmost) throw new InvalidOperationException("Contest must allow the decoder to come in front.");
            firstWindow.Close();
            if (!IsEnabled) throw new InvalidOperationException("Parent disabled after closing contest.");
            ShowContestWindow();
            var reopened = _contestWindow ?? throw new InvalidOperationException("Contest window did not reopen.");
            if (_contestModel.Contacts.Count != 1 || reopened == firstWindow) throw new InvalidOperationException("Reopening contest lost the journal.");
            if (reopened.KeepAboveDecoder || reopened.Owner != null) throw new InvalidOperationException("Reopening contest changed its window order.");
            reopened.KeepAboveDecoder = true;
            if (reopened.Owner != this || reopened.Topmost) throw new InvalidOperationException("Contest must stay above the decoder only.");
            reopened.Close();
            File.WriteAllText(Path.Combine(directory, "result.txt"), "PASS: separate modeless window, singleton, relative window order, saved QSO, reopen, ADIF, Cabrillo; no CAT/PTT.");
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "result.txt"), error.ToString()); code = 1; }
        Application.Current.Shutdown(code);
    }
    private sealed class SmokeContestHost : IContestHost
    {
        public long? FrequencyHz => 14_080_000;
        public bool IsBusy => false;
        public string MyCall => "YO9VA";
        public bool TryTransmit(string text) => throw new InvalidOperationException("Smoke must never transmit.");
        public void Stop() { }
    }
}
