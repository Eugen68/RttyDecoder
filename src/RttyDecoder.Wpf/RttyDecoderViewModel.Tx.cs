using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using RttyDecoder.Core.Tx;

namespace RttyDecoder.Wpf;

/// <summary>Un macro (F1..F8): nume scurt pe buton si textul trimis, cu variabile.</summary>
public sealed class MacroSettings
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Un buton de macro din interfata.</summary>
public sealed class MacroItem : ObservableObject
{
    public MacroItem(int index, string name, string text)
    {
        Index = index;
        _name = name;
        _text = text;
    }

    public int Index { get; }

    /// <summary>"F1".."F8".</summary>
    public string Key => "F" + (Index + 1);

    private string _name;
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) OnPropertyChanged(nameof(Header)); }
    }

    private string _text;
    public string Text
    {
        get => _text;
        set { if (Set(ref _text, value)) OnPropertyChanged(nameof(ToolTip)); }
    }

    public string Header => $"{Key}  {Name}";

    private string _preview = "";
    /// <summary>Textul care pleaca, cu variabilele inlocuite (si ce lipseste).</summary>
    public string Preview
    {
        get => _preview;
        internal set { if (Set(ref _preview, value)) OnPropertyChanged(nameof(ToolTip)); }
    }

    public string ToolTip => (Preview.Length > 0 ? Preview : Text) + "\n\nClick sau " + Key + ": trimite.  Click dreapta: editeaza.";
}

/// <summary>
/// Emisia RTTY prin FSK: macro-uri, text trimis la Enter, LIVE. PTT-ul si pinul FSK sunt cele din
/// Setari > Conexiune statie (HamRig: <c>IRig.SetPtt</c> / <c>IRig.SetKey</c>). Cat timp se emite,
/// decodarea sta (statia nu receptioneaza).
/// </summary>
public sealed partial class RttyDecoderViewModel
{
    public const int MacroCount = 8;

    /// <summary>Macro-urile implicite (F1..F8).</summary>
    public static IReadOnlyList<MacroSettings> DefaultMacros { get; } = new[]
    {
        new MacroSettings { Name = "CQ", Text = "CQ CQ CQ DE {MYCALL} {MYCALL} {MYCALL} PSE K" },
        new MacroSettings { Name = "Raspuns", Text = "{CALL} DE {MYCALL} {MYCALL} K" },
        new MacroSettings { Name = "Raport", Text = "{CALL} DE {MYCALL} TNX FER CALL UR RST {RST} {RST} BK" },
        new MacroSettings { Name = "TU 73", Text = "{CALL} TNX FER QSO 73 DE {MYCALL} SK" },
        new MacroSettings { Name = "QRZ?", Text = "QRZ? DE {MYCALL} K" },
        new MacroSettings { Name = "AGN", Text = "AGN AGN PSE" },
        new MacroSettings { Name = "Indicativ", Text = "DE {MYCALL} {MYCALL} {MYCALL}" },
        new MacroSettings { Name = "RYRY", Text = "RYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRY" },
    };

    private readonly StringBuilder _pendingEcho = new();
    private RttyFskTransmitter _tx = null!;

    /// <summary>Emitatorul FSK.</summary>
    public RttyFskTransmitter Tx => _tx;

    public ObservableCollection<MacroItem> Macros { get; } = new();

    public ICommand MacroCommand { get; private set; } = null!;
    public ICommand SendTextCommand { get; private set; } = null!;
    public ICommand StopTxCommand { get; private set; } = null!;

    /// <summary>Text trimis pe ecran (galben), pe firul UI.</summary>
    public event Action<string>? TxEcho;

    /// <summary>Setarile emisiei (gazda le salveaza).</summary>
    public RttyTxSettings TxSettings
    {
        get => _tx.Settings;
        set => _tx.Settings = value.Clone();
    }

    private string _txText = "";
    /// <summary>Campul de emisie (Enter = trimite; in LIVE caracterele pleaca pe masura ce le scrii).</summary>
    public string TxText
    {
        get => _txText;
        set => Set(ref _txText, value ?? "");
    }

    private bool _liveMode;
    /// <summary>LIVE: emisia sta pornita, caracterele pleaca pe masura ce se scriu (diddle intre ele).</summary>
    public bool LiveMode
    {
        get => _liveMode;
        set
        {
            if (value == _liveMode) return;
            if (value && !CanTransmit())
            {
                OnPropertyChanged();          // butonul revine
                return;
            }
            _liveMode = value;
            OnPropertyChanged();
            if (value)
            {
                PrepareTx();
                _tx.StartLive();
            }
            else
            {
                _tx.StopLive();
            }
        }
    }

    private bool _isTransmitting;
    public bool IsTransmitting
    {
        get => _isTransmitting;
        private set => Set(ref _isTransmitting, value);
    }

    private string _lastCallsign = "";
    /// <summary>Indicativul pentru {CALL}: ultimul pe care s-a dat click (terminal sau lista).</summary>
    public string LastCallsign
    {
        get => _lastCallsign;
        set { if (Set(ref _lastCallsign, (value ?? "").Trim().ToUpperInvariant())) RefreshMacroPreviews(); }
    }

    /// <summary>Tooltip-urile macro-urilor: textul exact care pleaca acum.</summary>
    internal void RefreshMacroPreviews()
    {
        if (Macros.Count == 0) return;
        foreach (var m in Macros) m.Preview = Preview(m.Text);
    }

    private string Preview(string text)
    {
        var lipsa = new List<string>();
        if (text.Contains("{MYCALL}", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(MyCall))
            lipsa.Add("{MYCALL}: completeaza \"Indicativul meu\" (in lista statiilor)");
        if (text.Contains("{CALL}", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(LastCallsign))
            lipsa.Add("{CALL}: da click pe indicativul corespondentului");
        string t = text.Replace("{RST}", "599", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(MyCall)) t = t.Replace("{MYCALL}", MyCall, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(LastCallsign)) t = t.Replace("{CALL}", LastCallsign, StringComparison.OrdinalIgnoreCase);
        return lipsa.Count == 0 ? t : t + "\n\nLipseste:\n" + string.Join("\n", lipsa);
    }

    private void InitTx()
    {
        _tx = new RttyFskTransmitter(new RigFskOutput(this));
        _tx.CharacterSent += c => { lock (_pendingEcho) _pendingEcho.Append(c); };
        _tx.Started += () => _dispatcher.BeginInvoke(() => IsTransmitting = true);
        _tx.Stopped += reason => _dispatcher.BeginInvoke(() =>
        {
            IsTransmitting = false;
            if (_liveMode && !_tx.IsLive)
            {
                _liveMode = false;
                OnPropertyChanged(nameof(LiveMode));
            }
            if (reason != null) Message = "Emisie oprita: " + reason;
        });
        for (int i = 0; i < MacroCount; i++)
        {
            var m = new MacroItem(i, DefaultMacros[i].Name, DefaultMacros[i].Text);
            m.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MacroItem.Text)) RefreshMacroPreviews(); };
            Macros.Add(m);
        }
        RefreshMacroPreviews();
        MacroCommand = new RelayCommand(p => { if (p is MacroItem m) SendMacro(m.Index); else if (p is int i) SendMacro(i); });
        SendTextCommand = new RelayCommand(SendText);
        StopTxCommand = new RelayCommand(StopTx);
    }

    /// <summary>Macro-urile salvate (lipsa = cele implicite).</summary>
    public void ImportMacros(IReadOnlyList<MacroSettings>? macros)
    {
        for (int i = 0; i < MacroCount; i++)
        {
            var m = macros != null && i < macros.Count && macros[i] != null ? macros[i] : DefaultMacros[i];
            Macros[i].Name = m.Name ?? "";
            Macros[i].Text = m.Text ?? "";
        }
        RefreshMacroPreviews();
    }

    public List<MacroSettings> ExportMacros() =>
        Macros.Select(m => new MacroSettings { Name = m.Name, Text = m.Text }).ToList();

    /// <summary>Trimite macro-ul F(index+1). Daca nu incepe cu CQ e un raspuns (QSY automat, daca e pornit).</summary>
    public void SendMacro(int index)
    {
        if (index < 0 || index >= Macros.Count) return;
        if (Expand(Macros[index].Text) is not { } text || !CanTransmit()) return;
        Transmit(text);
    }

    /// <summary>Enter in campul de emisie: ca macro-urile, raspuns daca nu incepe cu CQ.</summary>
    public void SendText()
    {
        if (_liveMode || string.IsNullOrWhiteSpace(TxText)) return;
        if (Expand(TxText) is not { } text || !CanTransmit()) return;
        Transmit(text);
        TxText = "";
    }

    /// <summary>Mesajul incepe cu CQ (chemare generala, pe frecventa ta): nu e raspuns, nu se face QSY.</summary>
    public static bool IsCq(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith("CQ", StringComparison.OrdinalIgnoreCase)
               && (t.Length == 2 || !char.IsLetterOrDigit(t[2]));
    }

    // mesaje care asteapta sa termine statia QSY-ul automat (null = niciun QSY in curs)
    private List<string>? _afterQsy;

    /// <summary>
    /// Pune mesajul in emisie. La un raspuns (nu incepe cu CQ), cu QSY automat pornit si emisia oprita,
    /// statia se muta intai unde a calculat ultimul A-TUNE (<see cref="TryAutoQsy"/>), iar PTT-ul vine
    /// dupa o pauza scurta. CQ-ul uita QSY-ul pregatit: chemi pe frecventa ta.
    /// </summary>
    private void Transmit(string text)
    {
        if (_afterQsy != null)
        {
            _afterQsy.Add(text);   // al doilea mesaj in timpul pauzei: pleaca dupa primul
            return;
        }
        if (IsCq(text)) ForgetPendingQsy();
        else if (_tx.Settings.AutoQsy && !_tx.IsTransmitting && TryAutoQsy())
        {
            var batch = new List<string> { text };
            _afterQsy = batch;
            _ = SendAfterQsyAsync(batch);
            return;
        }
        PrepareTx();
        _tx.Send(text);
    }

    private async Task SendAfterQsyAsync(List<string> batch)
    {
        try
        {
            await Task.Delay(AutoQsySettleMs);   // continuarea revine pe firul UI
            if (!ReferenceEquals(_afterQsy, batch)) return;   // STOP intre timp
            _afterQsy = null;
            if (!CanTransmit()) return;
            PrepareTx();
            foreach (var text in batch) _tx.Send(text);
        }
        catch (Exception e)
        {
            _afterQsy = null;
            Message = "Emisie dupa QSY: " + e.Message;
        }
    }

    /// <summary>LIVE: un caracter scris (Enter = '\n').</summary>
    public void SendLiveChar(char c)
    {
        if (!_liveMode) return;
        _tx.SendLive(c);
    }

    /// <summary>Esc / STOP: emisia se opreste imediat.</summary>
    public void StopTx()
    {
        _afterQsy = null;   // un raspuns care astepta QSY-ul nu mai pleaca
        _liveMode = false;
        OnPropertyChanged(nameof(LiveMode));
        _tx.Stop();
    }

    /// <summary>
    /// Variabilele: {MYCALL} indicativul propriu, {CALL} ultimul indicativ pe care s-a dat click,
    /// {RST} 599. Null daca lipseste o valoare (cu mesaj).
    /// </summary>
    public string? Expand(string text)
    {
        if (text.Contains("{MYCALL}", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(MyCall))
        {
            Message = "Completeaza \"Indicativul meu\" (in lista statiilor) pentru {MYCALL}.";
            return null;
        }
        if (text.Contains("{CALL}", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(LastCallsign))
        {
            Message = "Da click pe indicativul corespondentului (terminal sau lista) pentru {CALL}.";
            return null;
        }
        return text.Replace("{MYCALL}", MyCall, StringComparison.OrdinalIgnoreCase)
                   .Replace("{CALL}", LastCallsign, StringComparison.OrdinalIgnoreCase)
                   .Replace("{RST}", "599", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanTransmit()
    {
        if (_rig == null || !_rigState.Connected)
        {
            Message = "Emisie: statia nu e conectata (Setari > Conexiune statie).";
            return false;
        }
        if (!_rig.CanKey)
        {
            Message = "Emisie FSK: alege portul si pinul de manipulare (Setari > Conexiune statie).";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Emisia urmeaza tabelul US-TTY de la receptie. NORM/REV nu: e doar pentru receptie, pinul FSK
    /// pleaca mereu normal (vezi RttyFskTransmitter).
    /// </summary>
    private void PrepareTx()
    {
        if (_tx.Settings.UsTty != UsTty)
        {
            var s = _tx.Settings.Clone();
            s.UsTty = UsTty;
            _tx.Settings = s;
        }
    }

    /// <summary>Din Drain (firul UI): ecoul caracterelor trimise.</summary>
    private void DrainTxEcho()
    {
        string? echo = null;
        lock (_pendingEcho)
        {
            if (_pendingEcho.Length > 0)
            {
                echo = _pendingEcho.ToString();
                _pendingEcho.Clear();
            }
        }
        if (echo != null) TxEcho?.Invoke(echo);
    }

    /// <summary>PTT si pinul FSK prin statia curenta (HamRig).</summary>
    private sealed class RigFskOutput : IFskOutput
    {
        private readonly RttyDecoderViewModel _vm;

        public RigFskOutput(RttyDecoderViewModel vm) => _vm = vm;

        public void SetPtt(bool on) => _vm._rig?.SetPtt(on);

        public void SetLine(bool active) => _vm._rig?.SetKey(active);
    }
}
