using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using HamCallsign;
using Microsoft.Win32;

namespace RttyDecoder.Wpf;

/// <summary>
/// Decodorul RTTY ca UserControl, de pus in orice fereastra WPF:
/// <code>&lt;rtty:RttyDecoderView DataContext="{Binding Rtty}" /&gt;</code>
/// cu un <see cref="RttyDecoderViewModel"/> ca DataContext.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class RttyDecoderView : UserControl
{
    /// <summary>Peste atatea caractere, terminalul pastreaza doar ultima parte.</summary>
    private const int MaxTerminalChars = 400_000;

    /// <summary>O bucata de text simplu din terminal se lungeste pana la atatea caractere, apoi incepe alta.</summary>
    private const int MaxRunChars = 256;

    private RttyDecoderViewModel? _vm;

    /// <summary>Tot textul din terminal, in paralel cu documentul (pentru salvare si limita de marime).</summary>
    private readonly StringBuilder _text = new();
    private readonly CallsignHighlighter _highlighter;
    private Run? _plain;        // ultima bucata de text simplu, se lungeste
    private Run? _pendingRun;   // cuvantul neterminat, mereu ultimul
    private GridLength _stationsWidth = new(380);

    private readonly System.Windows.Threading.DispatcherTimer _levelTimer;

    public RttyDecoderView()
    {
        InitializeComponent();
        _levelTimer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250),
            System.Windows.Threading.DispatcherPriority.Background, (_, _) => UpdateLevels(), Dispatcher);
        Loaded += (_, _) => _levelTimer.Start();
        Unloaded += (_, _) => _levelTimer.Stop();
        // F1..F8 si Esc oriunde in fereastra, nu doar cand modulul are focusul
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } w)
            {
                w.PreviewKeyDown -= OnWindowKeyDown;
                w.PreviewKeyDown += OnWindowKeyDown;
            }
        };
        _highlighter = new CallsignHighlighter(w => _vm?.RecognizeWord(w));
        DataContextChanged += (_, e) => Bind(e.NewValue as RttyDecoderViewModel);
    }

    private void Bind(RttyDecoderViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.TextAppended -= OnText;
            _vm.InfoAppended -= OnInfo;
            _vm.TerminalCleared -= OnCleared;
            _vm.SpectrumRow -= Waterfall.PushRow;
            Tuning.Scope = null;
            _vm.CallsignSettingsChanged -= RecolorCallsigns;
            _vm.TxEcho -= OnTxEcho;
            _vm.TuningChanged -= OnTuningChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        if (vm != null)
        {
            vm.TextAppended += OnText;
            vm.InfoAppended += OnInfo;
            vm.TerminalCleared += OnCleared;
            vm.SpectrumRow += Waterfall.PushRow;
            Tuning.Scope = vm.Scope;
            vm.CallsignSettingsChanged += RecolorCallsigns;
            vm.TxEcho += OnTxEcho;
            vm.TuningChanged += OnTuningChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdateStationsColumn();
        }
    }

    /// <summary>Tot textul din terminal.</summary>
    public string TerminalText => _text.ToString();

    // ------------------------------------------------------------------
    // Terminal: text decodat cu indicativele evidentiate
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // Reglajele waterfall-ului
    // ------------------------------------------------------------------

    /// <summary>Reglajele waterfall-ului (gazda le salveaza).</summary>
    public WaterfallSettings WaterfallSettings
    {
        get => new()
        {
            AutoFloor = Waterfall.AutoFloor,
            FloorDb = Waterfall.FloorDb,
            RangeDb = Waterfall.RangeDb,
            Gamma = Waterfall.Gamma,
        };
        set
        {
            Waterfall.FloorDb = Math.Clamp(value.FloorDb, -160, 0);
            Waterfall.RangeDb = Math.Clamp(value.RangeDb, 10, 120);
            Waterfall.Gamma = Math.Clamp(value.Gamma, 0.2, 3);
            Waterfall.AutoFloor = value.AutoFloor;
            UpdateLevels();
        }
    }

    private void UpdateClocks()
    {
        if (UtcClock == null || LocalClock == null) return;
        var now = DateTime.Now;
        UtcClock.Text = now.ToUniversalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        LocalClock.Text = now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void UpdateLevels()
    {
        UpdateClocks();
        // bifa Auto isi primeste valoarea inca din InitializeComponent, inainte sa existe textele de langa ea
        if (SignalText == null || FloorText == null || FloorSlider == null) return;
        SignalText.Text = Waterfall.CurrentLevelRangeDb is { } r ? $"{r.MinDb:0}..{r.MaxDb:0} dB" : "-";
        double floor = Waterfall.AutoFloor ? Waterfall.CurrentAutoFloorDb - 3 : Waterfall.FloorDb;
        FloorText.Text = double.IsNaN(floor) ? "auto" : $"{floor:0} dB";
        // cu Auto, cursorul arata unde e podeaua automata (ca la debifare sa nu sara)
        if (Waterfall.AutoFloor && !double.IsNaN(Waterfall.CurrentAutoFloorDb))
            FloorSlider.Value = Math.Clamp(Math.Round(Waterfall.CurrentAutoFloorDb - 3), FloorSlider.Minimum, FloorSlider.Maximum);
    }

    private void OnAutoFloorChanged(object sender, RoutedEventArgs e) => UpdateLevels();

    /// <summary>Textul trimis: galben, fara evidentierea indicativelor.</summary>
    private void OnTxEcho(string text)
    {
        foreach (var seg in _highlighter.Flush())
        {
            if (seg.Match is { } m) AddCallsign(seg.Text, m);
            else AddPlain(seg.Text);
        }
        if (_pendingRun != null)
        {
            TerminalParagraph.Inlines.Remove(_pendingRun);
            _pendingRun = null;
        }
        text = text.Replace("\r", "");
        bool laCapat = Terminal.VerticalOffset + Terminal.ViewportHeight >= Terminal.ExtentHeight - 2;
        text = MaybeStamp(text, Speaker.Tx, () => { });
        var inlines = TerminalParagraph.Inlines;
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            int end = nl < 0 ? text.Length : nl;
            if (end > i)
            {
                inlines.Add(new Run(text.Substring(i, end - i)) { Foreground = TxBrush });
                _plain = null;
            }
            if (nl < 0) break;
            inlines.Add(new LineBreak());
            _plain = null;
            i = nl + 1;
        }
        _text.Append(text);
        if (_text.Length > MaxTerminalChars)
        {
            TrimStart(_text.Length - MaxTerminalChars * 3 / 4);
            laCapat = true;
        }
        if (laCapat) Terminal.ScrollToEnd();
    }

    private static readonly Brush TxBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));

    // ------------------------------------------------------------------
    // Emisie: tastatura si macro-uri
    // ------------------------------------------------------------------

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key == Key.Escape && _vm.IsTransmitting)
        {
            _vm.StopTx();
            e.Handled = true;
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key >= Key.F1 && key <= Key.F8 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _vm.SendMacro(key - Key.F1);
            e.Handled = true;
        }
    }

    private void OnTxKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null || e.Key != Key.Enter) return;
        if (_vm.LiveMode)
        {
            _vm.SendLiveChar('\n');
            _vm.TxText = "";
        }
        else
        {
            _vm.SendText();
        }
        e.Handled = true;
    }

    private void OnTxTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_vm == null || !_vm.LiveMode) return;
        foreach (char c in e.Text) _vm.SendLiveChar(c);
    }

    private void OnEditMacro(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MacroItem m }) return;
        var dlg = new MacroEditWindow(m.Key, m.Name, m.Text) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            m.Name = dlg.MacroName;
            m.Text = dlg.MacroText;
            MacrosChanged?.Invoke();
        }
        e.Handled = true;
    }

    /// <summary>Un macro a fost editat (gazda salveaza setarile).</summary>
    public event Action? MacrosChanged;

    /// <summary>Text decodat: indicativele se evidentiaza.</summary>
    private void OnText(string text) => Append(text, true);

    /// <summary>Mesaj al aplicatiei (ex. numele fisierului): text simplu.</summary>
    private void OnInfo(string text)
    {
        Append(text, false);
        _lastSpeaker = Speaker.None;   // dupa un mesaj al aplicatiei, urmatorul text primeste ora
    }

    // ------------------------------------------------------------------
    // Ora UTC in fata mesajelor: [23:44:34]: - verde la receptie, rosu la emisie
    // ------------------------------------------------------------------

    private enum Speaker { None, Rx, Tx }

    /// <summary>Peste atata pauza, urmatorul mesaj (receptie sau emisie) incepe cu ora.</summary>
    private static readonly TimeSpan StampGap = TimeSpan.FromSeconds(5);

    private static readonly Brush StampTxBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));

    private Speaker _lastSpeaker = Speaker.None;
    private DateTime _lastRxUtc = DateTime.MinValue;
    private DateTime _lastTxUtc = DateTime.MinValue;
    private bool _stampNextRx = true;

    /// <summary>Acordul s-a schimbat: urmatorul text decodat incepe obligatoriu cu ora.</summary>
    private void OnTuningChanged() => _stampNextRx = true;

    /// <summary>
    /// Pune ora in fata textului daca e un mesaj nou: dupa o pauza de cel putin 5 s, la trecerea
    /// intre receptie si emisie, sau (la receptie) dupa o schimbare de acord. Ora incepe un rand nou;
    /// spatiile / randurile goale de la inceputul mesajului se sar. Un text doar din spatii nu conteaza
    /// ca activitate. <paramref name="beforeStamp"/> incheie ce era inceput (cuvantul neterminat).
    /// </summary>
    private string MaybeStamp(string text, Speaker who, Action beforeStamp)
    {
        int first = 0;
        while (first < text.Length && char.IsWhiteSpace(text[first])) first++;
        if (first == text.Length) return text;

        var now = DateTime.UtcNow;
        var last = who == Speaker.Rx ? _lastRxUtc : _lastTxUtc;
        bool need = who != _lastSpeaker || now - last >= StampGap || (who == Speaker.Rx && _stampNextRx);
        if (who == Speaker.Rx)
        {
            _lastRxUtc = now;
            _stampNextRx = false;
        }
        else
        {
            _lastTxUtc = now;
        }
        _lastSpeaker = who;
        if (!need) return text;

        beforeStamp();
        bool lineStart = _text.Length == 0 || _text[^1] == '\n';
        string stamp = $"[{now:HH:mm:ss}]: ";
        var inlines = TerminalParagraph.Inlines;
        if (!lineStart)
        {
            inlines.Add(new LineBreak());
            _text.Append('\n');
        }
        var run = new Run(stamp);
        if (who == Speaker.Tx) run.Foreground = StampTxBrush;   // la receptie: verdele textului
        inlines.Add(run);
        _text.Append(stamp);
        _plain = null;
        return text.Substring(first);
    }

    private void FlushPendingWord()
    {
        foreach (var seg in _highlighter.Flush())
        {
            if (seg.Match is { } m) AddCallsign(seg.Text, m);
            else AddPlain(seg.Text);
        }
    }

    private void Append(string text, bool decoded)
    {
        text = text.Replace("\r", "");
        if (text.Length == 0) return;
        // derulam automat doar daca utilizatorul e deja la capat (nu cand citeste mai sus)
        bool laCapat = Terminal.VerticalOffset + Terminal.ViewportHeight >= Terminal.ExtentHeight - 2;
        var inlines = TerminalParagraph.Inlines;
        if (_pendingRun != null)
        {
            inlines.Remove(_pendingRun);
            _pendingRun = null;
        }

        if (decoded)
        {
            text = MaybeStamp(text, Speaker.Rx, FlushPendingWord);
            foreach (var seg in _highlighter.Feed(text))
            {
                if (seg.Match is { } m) AddCallsign(seg.Text, m);
                else AddPlain(seg.Text);
            }
            string pending = _highlighter.PendingWord;
            if (pending.Length > 0)
            {
                _pendingRun = new Run(pending);
                inlines.Add(_pendingRun);
            }
        }
        else
        {
            // cuvantul inceput se incheie aici, ca text simplu (mesajul incepe cu '\n')
            foreach (var seg in _highlighter.Flush())
            {
                if (seg.Match is { } m) AddCallsign(seg.Text, m);
                else AddPlain(seg.Text);
            }
            AddPlain(text);
        }
        _text.Append(text);

        if (_text.Length > MaxTerminalChars)
        {
            TrimStart(_text.Length - MaxTerminalChars * 3 / 4);
            laCapat = true;
        }
        if (laCapat) Terminal.ScrollToEnd();
    }

    private void AddPlain(string s)
    {
        var inlines = TerminalParagraph.Inlines;
        int i = 0;
        while (i < s.Length)
        {
            int nl = s.IndexOf('\n', i);
            int end = nl < 0 ? s.Length : nl;
            if (end > i)
            {
                string part = s.Substring(i, end - i);
                if (_plain != null && _plain.Text.Length + part.Length <= MaxRunChars && inlines.LastInline == _plain)
                {
                    _plain.Text += part;
                }
                else
                {
                    _plain = new Run(part);
                    inlines.Add(_plain);
                }
            }
            if (nl < 0) break;
            inlines.Add(new LineBreak());
            _plain = null;
            i = nl + 1;
        }
    }

    private void AddCallsign(string text, CallsignMatch m)
    {
        var run = new Run(text) { FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
        ApplyCallsignStyle(run, m);
        TerminalParagraph.Inlines.Add(run);
        _plain = null;
    }

    private void ApplyCallsignStyle(Run run, CallsignMatch m)
    {
        // necunoscut local, dar confirmat de QRZ.com / HamQTH.com: verde ca un indicativ cunoscut
        var online = _vm?.Verifier.GetState(m.Callsign) ?? HamCallsign.Online.CallsignVerificationState.Pending;
        var result = _vm?.Verifier.GetResult(m.Callsign);
        string key = m.Status switch
        {
            CallsignStatus.Own => "Call.Own",
            CallsignStatus.Known => "Call.Known",
            _ when online == HamCallsign.Online.CallsignVerificationState.Confirmed => "Call.Known",
            _ => "Call.FormatOnly",
        };
        run.Tag = m;
        run.Foreground = (Brush)FindResource(key);
        run.Background = (Brush)FindResource(key + ".Back");
        var e = m.Entity;
        run.ToolTip = (e == null ? m.Callsign.Full : $"{m.Callsign.Full} - {e.Name} ({e.Continent}), CQ {e.CqZone}, ITU {e.ItuZone}") +
                      m.Status switch
                      {
                          CallsignStatus.Own => "\nindicativul propriu",
                          _ => "\n" + HeardStationRow.KnownText(m.KnownIn) + HeardStationRow.OnlineText(online, result),
                      } +
                      "\nClick: copiaza   Dublu click: QRZ.com";
    }

    /// <summary>Baza sau indicativul propriu s-au schimbat: refacem culorile indicativelor afisate.</summary>
    private void RecolorCallsigns()
    {
        if (_vm == null) return;
        foreach (var inline in TerminalParagraph.Inlines)
        {
            if (inline is Run { Tag: CallsignMatch } run && _vm.RecognizeWord(run.Text) is { } m) ApplyCallsignStyle(run, m);
        }
    }

    private void TrimStart(int chars)
    {
        var inlines = TerminalParagraph.Inlines;
        int removed = 0;
        while (removed < chars && inlines.FirstInline is { } first && first != _pendingRun)
        {
            removed += first is Run r ? r.Text.Length : 1; // LineBreak = '\n'
            if (first == _plain) _plain = null;
            inlines.Remove(first);
        }
        _text.Remove(0, Math.Min(removed, _text.Length));
    }

    private void OnCleared()
    {
        TerminalParagraph.Inlines.Clear();
        _text.Clear();
        _plain = null;
        _pendingRun = null;
        _highlighter.Reset();
        _lastSpeaker = Speaker.None;
        _stampNextRx = true;
        Waterfall.Clear();
    }

    private CallsignMatch? CallsignAt(MouseEventArgs e)
    {
        var pos = Terminal.GetPositionFromPoint(e.GetPosition(Terminal), false);
        return pos?.Parent is Run { Tag: CallsignMatch m } ? m : null;
    }

    private void OnTerminalClick(object sender, MouseButtonEventArgs e)
    {
        if (!Terminal.Selection.IsEmpty) return; // utilizatorul selecteaza text
        if (CallsignAt(e) is { } m) _vm?.SelectCallsign(m.Callsign);
    }

    private void OnTerminalDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CallsignAt(e) is not { } m) return;
        _vm?.OpenQrz(m.Callsign);
        e.Handled = true;
    }

    private void OnTerminalMouseMove(object sender, MouseEventArgs e)
    {
        // RichTextBox arata mereu cursorul de text: mana doar deasupra indicativelor
        Terminal.Cursor = CallsignAt(e) != null ? Cursors.Hand : null;
    }

    // ------------------------------------------------------------------
    // Lista statiilor
    // ------------------------------------------------------------------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RttyDecoderViewModel.ShowStations)) UpdateStationsColumn();
    }

    private void UpdateStationsColumn()
    {
        if (_vm == null) return;
        if (_vm.ShowStations)
        {
            StationsColumn.Width = _stationsWidth;
        }
        else
        {
            if (StationsColumn.Width.Value > 0) _stationsWidth = StationsColumn.Width;
            StationsColumn.Width = new GridLength(0);
        }
    }

    private static HeardStationRow? RowAt(MouseButtonEventArgs e) =>
        (e.OriginalSource as FrameworkElement)?.DataContext as HeardStationRow ??
        (e.OriginalSource as FrameworkContentElement)?.DataContext as HeardStationRow;

    private void OnStationClick(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e) is { } row) _vm?.SelectCallsign(row.Callsign);
    }

    private void OnStationDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e) is { } row) _vm?.OpenQrz(row.Callsign);
    }

    private void OnMyCallKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
    }

    private void OnSaveStations(object sender, RoutedEventArgs e) => SaveStationsDialog();

    /// <summary>Dialogul de salvare a listei statiilor (CSV) - si pentru meniul gazdei.</summary>
    public void SaveStationsDialog()
    {
        if (_vm == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Salveaza lista statiilor",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"statii_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _vm.Callsigns.ToCsv());
        }
        catch (Exception ex)
        {
            _vm.Message = "Nu pot salva: " + ex.Message;
        }
    }

    /// <summary>Dialogul de deschidere a unui fisier WAV - si pentru meniul gazdei.</summary>
    public void OpenWavDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Deschide o inregistrare RTTY",
            Filter = "Fisiere WAV (*.wav)|*.wav|Toate fisierele (*.*)|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _vm?.OpenFileCommand.Execute(dlg.FileName);
    }

    private void OnSaveText(object sender, RoutedEventArgs e) => SaveTextDialog();

    /// <summary>Dialogul de salvare a textului receptionat - si pentru meniul gazdei.</summary>
    public void SaveTextDialog()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Salveaza textul receptionat",
            Filter = "Text (*.txt)|*.txt",
            FileName = $"rtty_{DateTime.Now:yyyyMMdd_HHmm}.txt",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _text.ToString());
        }
        catch (Exception ex)
        {
            if (_vm != null) _vm.Message = "Nu pot salva: " + ex.Message;
        }
    }
}
