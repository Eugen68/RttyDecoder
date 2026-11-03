using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
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

    private RttyDecoderViewModel? _vm;

    public RttyDecoderView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Bind(e.NewValue as RttyDecoderViewModel);
    }

    private void Bind(RttyDecoderViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.TextAppended -= OnText;
            _vm.TerminalCleared -= OnCleared;
            _vm.SpectrumRow -= Waterfall.PushRow;
        }
        _vm = vm;
        if (vm != null)
        {
            vm.TextAppended += OnText;
            vm.TerminalCleared += OnCleared;
            vm.SpectrumRow += Waterfall.PushRow;
        }
    }

    /// <summary>Tot textul din terminal.</summary>
    public string TerminalText => Terminal.Text;

    private void OnText(string text)
    {
        // derulam automat doar daca utilizatorul e deja la capat (nu cand citeste mai sus)
        bool laCapat = Terminal.VerticalOffset + Terminal.ViewportHeight >= Terminal.ExtentHeight - 2;
        Terminal.AppendText(text);
        if (Terminal.Text.Length > MaxTerminalChars)
        {
            Terminal.Text = Terminal.Text[^(MaxTerminalChars * 3 / 4)..];
            laCapat = true;
        }
        if (laCapat) Terminal.ScrollToEnd();
    }

    private void OnCleared()
    {
        Terminal.Clear();
        Waterfall.Clear();
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
            File.WriteAllText(dlg.FileName, Terminal.Text);
        }
        catch (Exception ex)
        {
            if (_vm != null) _vm.Message = "Nu pot salva: " + ex.Message;
        }
    }
}
