using System.Runtime.Versioning;
using System.Windows;

namespace RttyDecoder.Wpf;

/// <summary>Editarea unui macro (click dreapta pe buton).</summary>
[SupportedOSPlatform("windows")]
public partial class MacroEditWindow : Window
{
    private readonly int _index;

    private readonly (string Name, string Text)? _defaults;
    private readonly Func<string, string, bool>? _save;

    public MacroEditWindow(string key, string name, string text,
        (string Name, string Text)? defaults = null, string? variableHelp = null,
        Func<string, string, bool>? save = null)
    {
        InitializeComponent();
        Title = "Macro " + key;
        _index = int.TryParse(key.TrimStart('F'), out int k) ? k - 1 : -1;
        _defaults = defaults;
        _save = save;
        if (variableHelp != null) VariableHelp.Text = variableHelp;
        NameBox.Text = name;
        TextBox.Text = text;
        Loaded += (_, _) => TextBox.Focus();
    }

    public string MacroName => NameBox.Text.Trim();

    public string MacroText => TextBox.Text.Trim();

    private void OnDefault(object sender, RoutedEventArgs e)
    {
        if (_defaults is { } defaults)
        {
            NameBox.Text = defaults.Name;
            TextBox.Text = defaults.Text;
            return;
        }
        if (_index < 0 || _index >= RttyDecoderViewModel.DefaultMacros.Count) return;
        NameBox.Text = RttyDecoderViewModel.DefaultMacros[_index].Name;
        TextBox.Text = RttyDecoderViewModel.DefaultMacros[_index].Text;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_save == null || _save(MacroName, MacroText)) DialogResult = true;
    }
}
