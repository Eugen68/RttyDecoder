using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace RttyDecoder.Wpf.Contest;

public partial class ContestWindow : Window
{
    private readonly string _applicationVersion;
    private readonly string _buildInfo;

    public ContestViewModel Model { get; }
    public event EventHandler? KeepAboveDecoderChanged;

    public bool KeepAboveDecoder
    {
        get => KeepAboveDecoderItem.IsChecked;
        set => KeepAboveDecoderItem.IsChecked = value;
    }

    private void OnKeepAboveDecoderChanged(object sender, RoutedEventArgs e) =>
        KeepAboveDecoderChanged?.Invoke(this, EventArgs.Empty);

    public ContestWindow(ContestViewModel model, string applicationVersion = "", string buildInfo = "")
    {
        _applicationVersion = applicationVersion;
        _buildInfo = buildInfo;
        InitializeComponent(); Model = model; DataContext = model;
        Loaded += (_, _) => { Model.SyncFrequency(); CallBox.Focus(); };
        Closing += OnClosing;
        Model.FocusRequested += OnFocusRequested;
        // ceasurile de deasupra jurnalului
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => UpdateClocks();
        UpdateClocks(); clock.Start();
        Closed += (_, _) => { Model.FocusRequested -= OnFocusRequested; clock.Stop(); };
    }

    private void UpdateClocks()
    {
        var now = DateTime.Now;
        UtcClock.Text = "UTC: " + now.ToUniversalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        LocalClock.Text = "LOCAL: " + now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Click dreapta pe un QSO il selecteaza (DataGrid nu o face singur), ca meniul sa lucreze pe el.</summary>
    private void OnJournalRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not DataGridRow) element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (element is DataGridRow row) { row.IsSelected = true; Journal.SelectedItem = row.Item; }
    }

    private void OnJournalMenuOpened(object sender, RoutedEventArgs e)
    {
        bool selected = Model.SelectedQso != null;
        QrzItem.IsEnabled = selected;
        DeleteItem.IsEnabled = selected && Model.HasSession;
        QrzItem.Header = selected ? $"_Look-up QRZ.com ({Model.SelectedQso!.Call})" : "_Look-up QRZ.com";
    }

    /// <summary>Pagina indicativului selectat pe QRZ.com, in browserul implicit.</summary>
    private void OnJournalQrz(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedQso is not { } selected) return;
        try { Process.Start(new ProcessStartInfo("https://www.qrz.com/db/" + Uri.EscapeDataString(selected.Call)) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "QRZ.com", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    /// <summary>ESM: dupe raport cursorul trece la numarul/schimbul primit, dupa logare la indicativ.</summary>
    private void OnFocusRequested(string target)
    {
        var box = target == "received" ? (Model.ReceivesSerial && string.IsNullOrEmpty(SerialBox.Text) ? SerialBox : ExchangeBox) : CallBox;
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!Model.Flush()) { e.Cancel = true; MessageBox.Show(this, Model.Status, "Salvare concurs", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (Model.HasSession && MessageBox.Show(this, "Sesiunea curenta va ramane salvata. Configurati un concurs nou?", "Sesiune noua", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Model.Run(Model.EndSession);
    }
    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Sesiuni concurs (*.contest.json)|*.contest.json", InitialDirectory = Model.Files.SessionsFolder };
        if (dialog.ShowDialog(this) == true) Model.Run(() => Model.LoadSession(dialog.FileName));
    }
    private void OnResume(object sender, RoutedEventArgs e) => Model.Run(() => Model.LoadSession(Model.Files.LastSession() ?? throw new FileNotFoundException("Nu exista o sesiune anterioara.")));
    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Definitii concurs (*.udc;*.sec)|*.udc;*.sec", Multiselect = true };
        if (dialog.ShowDialog(this) == true) Model.Run(() => Model.ImportFiles(dialog.FileNames));
    }
    private void OpenFolder(string path) => Model.Run(() => { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); });
    private void OnDefinitionsFolder(object sender, RoutedEventArgs e) => OpenFolder(Model.Files.DefinitionsFolder);
    private void OnSupportFolder(object sender, RoutedEventArgs e) => OpenFolder(Model.Files.SupportFolder);
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedQso is not { } selected) return;
        if (MessageBox.Show(this, $"Stergeti QSO-ul cu {selected.Call} din {selected.Utc} UTC?", "Stergere QSO", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Model.Run(Model.DeleteSelected);
    }
    private void Export(bool adif)
    {
        Model.Run(() =>
        {
            string text;
            try { text = adif ? Model.ExportAdif() : Model.ExportCabrillo(); }
            catch { if (!adif) HeadersExpander.IsExpanded = true; throw; }
            Directory.CreateDirectory(ContestFiles.DefaultExportFolder);
            var dialog = new SaveFileDialog
            {
                InitialDirectory = ContestFiles.DefaultExportFolder, FileName = Model.ExportBaseName + (adif ? ".adi" : ".log"),
                Filter = adif ? "ADIF (*.adi)|*.adi" : "Cabrillo (*.log)|*.log", AddExtension = true, DefaultExt = adif ? ".adi" : ".log"
            };
            if (dialog.ShowDialog(this) == true) ContestFiles.AtomicWrite(dialog.FileName, text);
        });
    }
    private void OnAdif(object sender, RoutedEventArgs e) => Export(true);
    private void OnCabrillo(object sender, RoutedEventArgs e) => Export(false);
    /// <summary>Ajutor > Ajutor: gazda deschide fereastra de ajutor a aplicatiei la subiectul cerut.</summary>
    public event Action<string>? HelpRequested;

    private void OnHelp(object sender, RoutedEventArgs e)
    {
        const string topic = "10-contest";
        if (HelpRequested != null) { HelpRequested(topic); return; }
        // fara gazda (fereastra folosita separat): propria fereastra de ajutor
        new Help.HelpWindow(topic) { Owner = this }.Show();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var assembly = typeof(HamContest.ContestSession).Assembly;
        string libraryVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                                ?? assembly.GetName().Version?.ToString() ?? "";
        string title = string.IsNullOrWhiteSpace(_applicationVersion)
            ? "Contest - RTTY Decoder" : $"Contest - RTTY Decoder {_applicationVersion}";
        MessageBox.Show(this,
            $"{title}\nCopyright (c) 2026 Eugen Trifu, YO9VA - licență MIT\n\n" +
            "Modul pentru concursuri RTTY cu jurnal QSO, punctaj, multiplicatori și verificarea dublelor.\n" +
            $"Regulamente UDC / liste SEC și calculul punctajului: HamContest {libraryVersion}.\n" +
            "Indicative și date de țară: HamCallsign (cty.dat).\n" +
            "Export jurnal: ADIF și Cabrillo.\n\n" +
            "https://github.com/Eugen68/RttyDecoder\nhttps://github.com/Eugen68/HamContest" +
            (_buildInfo.Length > 0 ? "\n\n" + _buildInfo : ""),
            "Despre Contest - RTTY Decoder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDetailsExpanded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, HeadersExpander)) DiagnosticsExpander.IsExpanded = false;
        else HeadersExpander.IsExpanded = false;
    }
    private void OnMacroTooltip(object sender, ToolTipEventArgs e)
    {
        if (sender is Border { Tag: string tag, ToolTip: ToolTip tip } && int.TryParse(tag, out int slot))
            tip.Content = Model.MacroTooltip(slot);
    }

    private void OnMacroEdit(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag } || !int.TryParse(tag, out int slot)) return;
        e.Handled = true;
        Model.Run(() =>
        {
            var macro = Model.GetMacro(slot);
            var defaults = ContestMacros.Defaults().Get(Model.SearchAndPounce, slot);
            string mode = Model.SearchAndPounce ? "S&P" : "Run";
            MacroEditWindow? dialog = null;
            dialog = new MacroEditWindow($"F{slot + 1} · {mode}", macro.Name, macro.Text,
                (defaults.Name, defaults.Text),
                "{MYCALL}: indicativ propriu. {CALL}: corespondent. {RST}: raport transmis. " +
                "In F2, {EXCHANGE}: serialul (daca este cerut) si schimbul fix.",
                (name, text) =>
                {
                    if (Model.Run(() => Model.SaveMacro(slot, new(name, text)))) return true;
                    MessageBox.Show(dialog!, Model.Status, "Macro", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                })
            {
                Owner = this
            };
            dialog.ShowDialog();
        });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (ContestMenu.IsKeyboardFocusWithin || ContestMenu.Items.OfType<MenuItem>().Any(item => item.IsSubmenuOpen)) return;

        ICommand? command = e.Key switch
        {
            Key.Escape => Model.StopCommand, Key.F1 => Model.CqCommand, Key.F2 => Model.ReportCommand,
            Key.F3 => Model.AgnCommand, Key.F4 => Model.TuCommand,
            Key.Enter when EntryArea.IsKeyboardFocusWithin && Keyboard.FocusedElement is not TextBox { AcceptsReturn: true } => Model.EnterCommand,
            _ => null
        };
        if (command?.CanExecute(null) == true) { command.Execute(null); e.Handled = true; }
    }
}
