using System.Windows.Input;
using HamRig;

namespace RttyDecoder.Wpf;

/// <summary>
/// Statia (biblioteca HamRig): afisarea frecventei si QSY. Statia o detine gazda (aplicatia) si o
/// da modulului prin <see cref="AttachRig"/> - in aplicatia mare, acelasi obiect e folosit de toate
/// modulele.
/// </summary>
public sealed partial class RttyDecoderViewModel
{
    private IRig? _rig;
    private RigState _rigState = RigState.Disconnected("fara statie");

    /// <summary>Shift+click pe waterfall: statia se muta ca semnalul de sub mouse sa ajunga pe Mark.</summary>
    public ICommand QsyCommand { get; private set; } = null!;

    /// <summary>Statia curenta (null = fara).</summary>
    public IRig? Rig => _rig;

    public RigState RigState => _rigState;

    private string _rigText = "";
    /// <summary>"FTX-1  14.083.500 DATA-U   Mark 14.085.625" - frecventa VFO si frecventa RF a Mark-ului.</summary>
    public string RigText
    {
        get => _rigText;
        private set => Set(ref _rigText, value);
    }

    private string _rigToolTip = "";
    public string RigToolTip
    {
        get => _rigToolTip;
        private set => Set(ref _rigToolTip, value);
    }

    private bool _rigTransmitting;
    public bool RigTransmitting
    {
        get => _rigTransmitting;
        private set => Set(ref _rigTransmitting, value);
    }

    private bool _rigConnected;
    public bool RigConnected
    {
        get => _rigConnected;
        private set => Set(ref _rigConnected, value);
    }

    private void InitRig()
    {
        QsyCommand = new RelayCommand(p => { if (p is double hz) Qsy(hz); }, p => CanQsy());
        UpdateRigText();
    }

    /// <summary>Leaga statia de modul (sau null). Gazda o conecteaza/deconecteaza; modulul doar o foloseste.</summary>
    public void AttachRig(IRig? rig)
    {
        if (_rig != null) _rig.StateChanged -= OnRigState;
        _rig = rig;
        _rigState = rig?.State ?? RigState.Disconnected("fara statie");
        if (rig != null) rig.StateChanged += OnRigState;
        UpdateRigText();
    }

    private void OnRigState(RigState s) => _dispatcher.BeginInvoke(() =>
    {
        // VFO mutat (QSY, butonul statiei): urmatorul text decodat primeste ora
        if (s.Connected && _rigState.Connected && s.FrequencyHz != _rigState.FrequencyHz) TuningChanged?.Invoke();
        _rigState = s;
        UpdateRigText();
    });

    private bool CanQsy() =>
        _rig != null && _rigState.Connected && RigMath.QsyDial(_rigState.FrequencyHz, _rigState.Mode, 1000, 2000) != null;

    /// <summary>QSY: semnalul auzit la <paramref name="audioHz"/> ajunge pe Mark-ul curent.</summary>
    public void Qsy(double audioHz)
    {
        if (_rig == null || RigMath.QsyDial(_rigState.FrequencyHz, _rigState.Mode, audioHz, MarkFrequencyHz) is not { } dial)
        {
            Message = "QSY: doar cu statia conectata, in USB/LSB/DATA.";
            return;
        }
        _rig.SetFrequency(dial);
        Message = $"QSY: statia pe {RigMath.FormatFrequency(dial)} - semnalul de la {audioHz:0} Hz vine pe Mark ({MarkFrequencyHz:0} Hz).";
    }

    /// <summary>Si la schimbarea Mark-ului (acord, AFC) se reface frecventa RF afisata.</summary>
    partial void OnMarkChangedForRig() => UpdateRigText();

    private void UpdateRigText()
    {
        var s = _rigState;
        RigConnected = s.Connected;
        RigTransmitting = s.Connected && s.Transmitting;
        if (_rig == null)
        {
            RigText = "";
            RigToolTip = "Fara statie: Setari > Conexiune statie";
            return;
        }
        if (!s.Connected)
        {
            RigText = "Statie: " + s.StatusText;
            RigToolTip = _rig.Description + " - " + s.StatusText;
            return;
        }
        string vfo = $"{RigMath.FormatFrequency(s.FrequencyHz)} {RigMath.ModeName(s.Mode)}";
        RigText = RigMath.ToneToRf(s.FrequencyHz, s.Mode, MarkFrequencyHz) is { } rf
            ? $"{vfo}   Mark {RigMath.FormatFrequency(rf)}"
            : vfo;
        RigToolTip = _rig.Description + "\nVFO " + vfo +
                     (RigMath.ToneToRf(s.FrequencyHz, s.Mode, MarkFrequencyHz) is { } m ? $"\nMark (RF): {RigMath.FormatFrequency(m)} Hz" : "") +
                     (RigMath.IsUpperSideband(s.Mode) || RigMath.IsLowerSideband(s.Mode)
                         ? "\nShift+click pe waterfall: QSY - semnalul ajunge pe Mark"
                         : "\nQSY prin Shift+click: doar in USB/LSB/DATA");
        CommandManager.InvalidateRequerySuggested();
    }
}
