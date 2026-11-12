using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using HamRig;
using RttyDecoder.Audio;
using RttyDecoder.Core;
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
/// Emisia RTTY: macro-uri, text trimis la Enter, LIVE. FSK: PTT-ul si pinul FSK din Setari &gt;
/// Conexiune statie (HamRig: <c>IRig.SetPtt</c> / <c>IRig.SetKey</c>). AFSK: PTT-ul prin CAT si
/// tonurile pe placa de sunet, pe perechea Mark/Space a decodorului. Cat timp se emite, decodarea sta.
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
        set
        {
            _tx.Settings = value.Clone();
            ConfigureTxOutput();
        }
    }

    private RigFskOutput _fskOutput = null!;
    private AfskOutput? _afskOutput;
    private string? _afskDeviceId;
    private volatile object _afskTones = new AfskTones(2125, 2295);

    /// <summary>Placa de sunet pentru AFSK, dupa id (testele o inlocuiesc).</summary>
    internal Func<string, IAfskSink> AfskSinkFactory { get; set; } = id => new WasapiAfskSink(id);

    /// <summary>Emisia AFSK (tonuri pe placa de sunet) in loc de FSK.</summary>
    public bool IsAfsk => _tx?.Settings.Method == TxMethod.Afsk;

    /// <summary>Iesirea emitatorului dupa metoda aleasa; se aplica de la urmatoarea emisie.</summary>
    private void ConfigureTxOutput()
    {
        var s = _tx.Settings;
        if (s.Method == TxMethod.Afsk)
        {
            if (_afskOutput == null || _afskDeviceId != s.AfskDeviceId)
            {
                _afskDeviceId = s.AfskDeviceId;
                _afskOutput = new AfskOutput(AfskSinkFactory(s.AfskDeviceId),
                    on => { if (_rig is { } rig) rig.SetPtt(on); else if (on) throw new InvalidOperationException("statia nu e conectata"); },
                    () => (AfskTones)_afskTones,
                    () => _tx.Settings.AfskLevel);
            }
            _tx.Output = _afskOutput;
        }
        else _tx.Output = _fskOutput;
        OnPropertyChanged(nameof(IsAfsk));
        UpdateRigText();
    }

    /// <summary>Statia intr-un mod in care trimite audio de la placa (SSB / DATA); Unknown = nu stim, lasam.</summary>
    public static bool IsAfskMode(RigMode mode) =>
        mode is RigMode.Usb or RigMode.Lsb or RigMode.DataU or RigMode.DataL or RigMode.Unknown;

    /// <summary>
    /// Tonurile AFSK: perechea decodorului (emiti unde asculti). Polaritatea standard pe aer din modul
    /// statiei (USB/DATA-U: Mark = tonul de sus; LSB/DATA-L: tonul de jos); mod necunoscut = ca la
    /// receptie (NORM/REV). InvertFsk inverseaza.
    /// </summary>
    public static AfskTones AfskTonesFor(double markHz, bool reverse, RigMode mode, bool invert)
    {
        double lower = reverse ? markHz - RttySettings.ShiftHz : markHz;
        bool? usb = mode switch
        {
            RigMode.Usb or RigMode.DataU => true,
            RigMode.Lsb or RigMode.DataL => false,
            _ => null,
        };
        return AfskTones.ForSideband(lower, RttySettings.ShiftHz, usb ?? reverse, invert);
    }

    /// <summary>Tonul de jos al perechii decodorului.</summary>
    private double DecoderLowerHz => Reverse ? MarkFrequencyHz - RttySettings.ShiftHz : MarkFrequencyHz;

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
        _fskOutput = new RigFskOutput(this);
        _tx = new RttyFskTransmitter(_fskOutput);
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

    /// <summary>Includes the short auto-QSY wait, so a contest QSO cannot be changed during queued TX.</summary>
    public bool ContestTxBusy => _afterQsy != null || _tx.IsTransmitting || _liveMode || IsTransmitting || RigTransmitting;

    /// <summary>
    /// Sends an already expanded contest message through the existing FSK path. Auto-QSY only when
    /// <paramref name="allowAutoQsy"/> (S&amp;P); in Run the station stays on its own frequency.
    /// </summary>
    public bool TrySendContestText(string text, bool allowAutoQsy)
    {
        text = text.Replace('\t', ' ');   // ca la emisie (RttyFskTransmitter.Normalize)
        if (ContestTxBusy) { Message = "Contest: asteptati terminarea emisiei sau apasati STOP."; return false; }
        if (!IsAfsk && !RigInRtty) { Message = "Contest: statia trebuie conectata in RTTY-L sau RTTY-U pentru emisie FSK."; return false; }
        var codec = new RttyDecoder.Core.Baudot.BaudotCodec { UsTty = UsTty };
        if (string.IsNullOrWhiteSpace(text) || text.ToUpperInvariant().Any(c => !codec.CanEncode(c)))
        { Message = "Contest: mesajul contine caractere care nu pot fi transmise in Baudot."; return false; }
        if (!CanTransmit()) return false;
        Transmit(text, allowAutoQsy);
        return true;
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
    private void Transmit(string text, bool allowAutoQsy = true)
    {
        if (_afterQsy != null)
        {
            _afterQsy.Add(text);   // al doilea mesaj in timpul pauzei: pleaca dupa primul
            return;
        }
        if (IsCq(text)) ForgetPendingQsy();
        // AFSK emite pe perechea decodorului: nu e nevoie de QSY
        else if (allowAutoQsy && !IsAfsk && _tx.Settings.AutoQsy && !_tx.IsTransmitting && TryAutoQsy())
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
        if (IsAfsk)
        {
            if (!IsAfskMode(_rigState.Mode))
            {
                Message = $"Emisie AFSK: statia e in {RigMath.ModeName(_rigState.Mode)} - pune-o in DATA-U / USB (sau DATA-L / LSB); in RTTY statia nu ia tonurile de la placa.";
                return false;
            }
            return true;
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
        _afskTones = AfskTonesFor(MarkFrequencyHz, Reverse, _rigState.Mode, _tx.Settings.InvertAfsk);
        if (_tx.Settings.UsTty != UsTty || _tx.Settings.InvertFsk != RigFskInverted)
        {
            var s = _tx.Settings.Clone();
            s.UsTty = UsTty;
            s.InvertFsk = RigFskInverted;
            _tx.Settings = s;
        }
    }

    /// <summary>
    /// Polaritatea pinului FSK a statiei curente (Conexiune statie): false = pin activ = Mark (Yaesu),
    /// true = pin activ = Space (Icom pe setarile din fabrica). Gazda o pune odata cu statia.
    /// </summary>
    public bool RigFskInverted { get; set; }

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
