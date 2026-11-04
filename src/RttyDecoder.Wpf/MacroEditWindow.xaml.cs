using System.Runtime.Versioning;
using System.Windows;

namespace RttyDecoder.Wpf;

/// <summary>Editarea unui macro (click dreapta pe buton).</summary>
[SupportedOSPlatform("windows")]
public partial class MacroEditWindow : Window
{
    private readonly int _index;

    public MacroEditWindow(string key, string name, string text)
    {
        InitializeComponent();
        Title = "Macro " + key;
        _index = int.TryParse(key.TrimStart('F'), out int k) ? k - 1 : -1;
        NameBox.Text = name;
        TextBox.Text = text;
        Loaded += (_, _) => TextBox.Focus();
    }

    public string MacroName => NameBox.Text.Trim();

    public string MacroText => TextBox.Text.Trim();

    private void OnDefault(object sender, RoutedEventArgs e)
    {
        if (_index < 0 || _index >= RttyDecoderViewModel.DefaultMacros.Count) return;
        NameBox.Text = RttyDecoderViewModel.DefaultMacros[_index].Name;
        TextBox.Text = RttyDecoderViewModel.DefaultMacros[_index].Text;
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
