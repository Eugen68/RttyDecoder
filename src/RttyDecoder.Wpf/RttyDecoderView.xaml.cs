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

    public RttyDecoderView()
    {
        InitializeComponent();
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
            _vm.CallsignSettingsChanged -= RecolorCallsigns;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = vm;
        if (vm != null)
        {
            vm.TextAppended += OnText;
            vm.InfoAppended += OnInfo;
            vm.TerminalCleared += OnCleared;
            vm.SpectrumRow += Waterfall.PushRow;
            vm.CallsignSettingsChanged += RecolorCallsigns;
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdateStationsColumn();
        }
    }

    /// <summary>Tot textul din terminal.</summary>
    public string TerminalText => _text.ToString();

    // ------------------------------------------------------------------
    // Terminal: text decodat cu indicativele evidentiate
    // ------------------------------------------------------------------

    /// <summary>Text decodat: indicativele se evidentiaza.</summary>
    private void OnText(string text) => Append(text, true);

    /// <summary>Mesaj al aplicatiei (ex. numele fisierului): text simplu.</summary>
    private void OnInfo(string text) => Append(text, false);

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
        string key = m.Status switch
        {
            CallsignStatus.Own => "Call.Own",
            CallsignStatus.Known => "Call.Known",
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
                          CallsignStatus.Known => "\nin MASTER.SCP",
                          _ => "\nnu e in MASTER.SCP (verifica: poate fi o eroare de decodare)",
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

    private void OnSaveStations(object sender, RoutedEventArgs e)
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

    private void OnOpenWav(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Deschide o inregistrare RTTY",
            Filter = "Fisiere WAV (*.wav)|*.wav|Toate fisierele (*.*)|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _vm?.OpenFileCommand.Execute(dlg.FileName);
    }

    private void OnSaveText(object sender, RoutedEventArgs e)
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
