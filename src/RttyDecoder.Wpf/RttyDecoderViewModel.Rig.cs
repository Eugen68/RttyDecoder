using System.Windows.Input;
using HamRig;
using RttyDecoder.Core;
using RttyDecoder.Core.Dsp;

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

    /// <summary>
    /// Tonul Mark in modul RTTY al statiei (FSK): fix 2125 Hz, shift 170. Statia emite exact unde e
    /// acordata, deci la FSK se muta statia (QSY), nu decodorul.
    /// </summary>
    public const double FskMarkHz = 2125;

    /// <summary>Statia e conectata si in RTTY-L / RTTY-U (FSK).</summary>
    public bool RigInRtty => _rig != null && _rigState.Connected && RigMath.IsRtty(_rigState.Mode);

    private void OnRigState(RigState s) => _dispatcher.BeginInvoke(() =>
    {
        // VFO mutat (QSY, butonul statiei): urmatorul text decodat primeste ora
        if (s.Connected && _rigState.Connected && s.FrequencyHz != _rigState.FrequencyHz) TuningChanged?.Invoke();
        _rigState = s;
        UpdateRigText();
    });

    private bool CanQsy() =>
        _rig != null && _rigState.Connected && RigMath.QsyDial(_rigState.FrequencyHz, _rigState.Mode, 1000, 2000) != null;

    /// <summary>Shift+click: semnalul auzit la <paramref name="audioHz"/> ajunge pe Mark-ul curent.</summary>
    public void Qsy(double audioHz)
    {
        if (_rig == null || RigMath.QsyDial(_rigState.FrequencyHz, _rigState.Mode, audioHz, MarkFrequencyHz) is not { } dial)
        {
            Message = "QSY: doar cu statia conectata, in USB/LSB/DATA/RTTY.";
            return;
        }
        _rig.SetFrequency(dial);
        Message = $"QSY: statia pe {RigMath.FormatFrequency(dial)} - semnalul de la {audioHz:0} Hz vine pe Mark ({MarkFrequencyHz:0} Hz).";
    }

    // ------------------------------------------------------------------
    // A-TUNE: frecventa statiei pentru emisie (dublu click = QSY acolo)
    // ------------------------------------------------------------------

    private long? _pendingQsyDial;   // unde trebuie dusa statia ca Mark-ul semnalului sa fie pe 2125
    private long _pendingQsyFrom;    // frecventa statiei la calcul (daca s-a mutat intre timp, calculul nu mai e valabil)
    private DateTime _ignoreAutoTuneUntil;

    /// <summary>Frecventa calculata la ultimul A-TUNE pentru emisie, sau null.</summary>
    public long? PendingQsyDial => _pendingQsyDial;

    /// <summary>
    /// Calculeaza (fara sa mute statia) frecventa pe care trebuie sa fie statia ca Mark-ul semnalului,
    /// auzit acum la <paramref name="markHz"/>, sa ajunga pe 2125 - acolo emisia FSK pleaca exact pe
    /// frecventa corespondentului. Intoarce textul pentru mesaj.
    /// </summary>
    private string PrepareQsy(double markHz)
    {
        _pendingQsyDial = null;
        if (_rig == null || !_rigState.Connected) return "";
        if (RigMath.QsyDial(_rigState.FrequencyHz, _rigState.Mode, markHz, FskMarkHz) is not { } dial) return "";
        _pendingQsyFrom = _rigState.FrequencyHz;
        if (Math.Abs(dial - _rigState.FrequencyHz) < 3) return ". Statia e deja pe frecventa pentru emisie.";
        _pendingQsyDial = dial;
        UpdateRigText();
        return $". Pentru emisie: statia pe {RigMath.FormatFrequency(dial)} - dublu click pe A-TUNE.";
    }

    /// <summary>Dublu click pe A-TUNE: statia pe frecventa calculata, decodorul pe 2125.</summary>
    public void QsyToPending()
    {
        if (_pendingQsyDial is not { } dial || _rig == null || !_rigState.Connected)
        {
            Message = "A-TUNE: nu e nimic de mutat (statia deja pe frecventa, sau fara statie).";
            return;
        }
        if (_rigState.FrequencyHz != _pendingQsyFrom)
        {
            _pendingQsyDial = null;
            UpdateRigText();
            Message = "Statia s-a mutat intre timp - apasa din nou A-TUNE.";
            return;
        }
        _pendingQsyDial = null;
        // dublu click: al doilea "click" al butonului vine dupa (la ridicarea mouse-ului) si ar acorda
        // decodorul pe pozitia veche a semnalului - A-TUNE se ignora putin dupa QSY
        _ignoreAutoTuneUntil = DateTime.UtcNow.AddSeconds(1.5);
        _rig.SetFrequency(dial);
        // semnalul vine pe 2125: decodorul il asteapta acolo (dupa SyncFromSettings deja in coada)
        _dispatcher.BeginInvoke(() => MarkFrequencyHz = FskMarkHz, System.Windows.Threading.DispatcherPriority.Background);
        UpdateRigText();
        Message = $"QSY: statia pe {RigMath.FormatFrequency(dial)} - Mark-ul semnalului pe {FskMarkHz:0} Hz, emisia pe frecventa lui.";
    }

    // ultimele secunde de audio, pentru detectia polaritatii la A-TUNE
    private readonly object _audioLock = new();
    private float[] _audioRing = Array.Empty<float>();
    private int _audioPos;
    private int _audioCount;
    private int _audioRate;

    private const double KeepAudioSeconds = 6;

    /// <summary>Pe firul audio.</summary>
    private void KeepAudio(ReadOnlySpan<float> x, int rate)
    {
        lock (_audioLock)
        {
            if (rate != _audioRate || _audioRing.Length == 0)
            {
                _audioRate = rate;
                _audioRing = new float[(int)(rate * KeepAudioSeconds)];
                _audioPos = 0;
                _audioCount = 0;
            }
            foreach (float v in x)
            {
                _audioRing[_audioPos] = v;
                _audioPos = (_audioPos + 1) % _audioRing.Length;
            }
            _audioCount = Math.Min(_audioRing.Length, _audioCount + x.Length);
        }
    }

    private PolarityResult DetectPolarity(double lowerHz)
    {
        float[] a;
        int rate;
        lock (_audioLock)
        {
            rate = _audioRate;
            int n = Math.Min(_audioCount, (int)(rate * 5));
            a = new float[n];
            int start = (_audioPos - n + _audioRing.Length) % Math.Max(1, _audioRing.Length);
            for (int i = 0; i < n; i++) a[i] = _audioRing[(start + i) % _audioRing.Length];
        }
        return RttyPolarity.Detect(a, rate, lowerHz, lowerHz + RttySettings.ShiftHz);
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
        RigText = (RigMath.ToneToRf(s.FrequencyHz, s.Mode, MarkFrequencyHz) is { } rf
                      ? $"{vfo}   Mark {RigMath.FormatFrequency(rf)}"
                      : vfo)
                  + (_pendingQsyDial is { } p && s.FrequencyHz == _pendingQsyFrom ? $"   TX: {RigMath.FormatFrequency(p)}" : "");
        RigToolTip = _rig.Description + "\nVFO " + vfo +
                     (RigMath.ToneToRf(s.FrequencyHz, s.Mode, MarkFrequencyHz) is { } m ? $"\nMark (RF): {RigMath.FormatFrequency(m)} Hz" : "") +
                     (RigMath.QsyDial(s.FrequencyHz, s.Mode, 1000, 2000) != null
                         ? "\nShift+click pe waterfall: QSY - semnalul ajunge pe Mark" +
                           $"\nA-TUNE calculeaza frecventa pentru emisie (Mark-ul semnalului pe {FskMarkHz:0} Hz); dublu click pe A-TUNE muta statia acolo"
                         : "\nQSY: doar in USB/LSB/DATA/RTTY") +
                     (_pendingQsyDial is { } pq && s.FrequencyHz == _pendingQsyFrom ? $"\nPentru emisie: {RigMath.FormatFrequency(pq)}" : "");
        CommandManager.InvalidateRequerySuggested();
    }
}
