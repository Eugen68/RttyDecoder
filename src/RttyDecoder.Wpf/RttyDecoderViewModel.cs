using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using HamCallsign;
using RttyDecoder.Audio;
using RttyDecoder.Core;
using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Wpf;

/// <summary>
/// ViewModel-ul decodorului: leaga <see cref="RttyReceiver"/> de o sursa audio (placa de sunet sau
/// fisier WAV) si de interfata. Se creeaza pe firul UI. Gazda (aplicatia) salveaza/incarca
/// setarile prin <see cref="ExportSettings"/> / <see cref="ImportSettings"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class RttyDecoderViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly object _textLock = new();
    private readonly StringBuilder _pendingText = new();
    private string? _pendingProvisional;
    private object? _pendingStatus; // RttyStatus impachetat, schimbat atomic intre fire
    private bool _syncing;

    private IAudioSource? _source;
    private WavFileSource? _file;

    public RttyDecoderViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Receiver = new RttyReceiver();
        Receiver.TextReceived += t => { lock (_textLock) _pendingText.Append(t); };
        Receiver.ProvisionalTextChanged += t => _pendingProvisional = t;
        Receiver.StatusUpdated += s =>
        {
            Volatile.Write(ref _pendingStatus, s);
            Scope.SetTones(s.MarkFrequencyHz, s.MarkFrequencyHz + (s.Reverse ? -RttySettings.ShiftHz : RttySettings.ShiftHz));
        };
        Receiver.SpectrumAvailable += row => SpectrumRow?.Invoke(row);
        Receiver.SettingsChanged += s => _dispatcher.BeginInvoke(() => SyncFromSettings(s));
        Receiver.Message += m => _dispatcher.BeginInvoke(() => Message = m);

        Monitor = new AudioMonitor();
        Monitor.Volume = (float)(_monitorVolume / 100.0);
        Monitor.Failed += e => _dispatcher.BeginInvoke(() =>
        {
            MonitorOn = false;
            Message = "Monitorizarea s-a oprit: " + e.Message;
        });

        StartStopCommand = new RelayCommand(() => { if (IsRunning) StopSource(); else StartLive(); });
        OpenFileCommand = new RelayCommand(p => { if (p is string path) OpenFile(path); });
        AutoTuneCommand = new RelayCommand(AutoTune, () => IsRunning);
        ReverseCommand = new RelayCommand(() =>
        {
            Receiver.ToggleReverse();
            SyncFromSettings(Receiver.Settings);
        });
        ClearCommand = new RelayCommand(Clear);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        SetFrequencyCommand = new RelayCommand(p => { if (p is double hz) MarkFrequencyHz = hz; });

        ClearStationsCommand = new RelayCommand(ClearStations);
        _callsigns = CreateTracker(new CallsignRecognizer());
        CreateVerifier();
        HeardStationsView = CreateStationsView();

        RefreshDevices();
        SyncFromSettings(Receiver.Settings);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Drain(), _dispatcher);
        _timer.Start();
    }

    public RttyReceiver Receiver { get; }

    /// <summary>Indicatorul de acord (elipsele Mark/Space), alimentat cu acelasi semnal ca decodorul.</summary>
    public TuningScope Scope { get; } = new();

    // ------------------------------------------------------------------
    // Evenimente catre View
    // ------------------------------------------------------------------

    /// <summary>Text decodat de adaugat in terminal (pe firul UI).</summary>
    public event Action<string>? TextAppended;

    /// <summary>Mesaj al aplicatiei de adaugat in terminal, nu text decodat (pe firul UI).</summary>
    public event Action<string>? InfoAppended;

    /// <summary>Terminalul trebuie golit (pe firul UI).</summary>
    public event Action? TerminalCleared;

    /// <summary>Rand de spectru pentru waterfall - pe firul audio (WaterfallControl.PushRow e thread-safe).</summary>
    public event Action<float[]>? SpectrumRow;

    // ------------------------------------------------------------------
    // Comenzi
    // ------------------------------------------------------------------

    public ICommand StartStopCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand AutoTuneCommand { get; }
    public ICommand ReverseCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand SetFrequencyCommand { get; }

    // ------------------------------------------------------------------
    // Sursa audio
    // ------------------------------------------------------------------

    public ObservableCollection<AudioDevice> Devices { get; } = new();

    private AudioDevice? _selectedDevice;
    public AudioDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => Set(ref _selectedDevice, value);
    }

    public IReadOnlyList<AudioChannel> Channels { get; } = Enum.GetValues<AudioChannel>();

    private AudioChannel _channel = AudioChannel.Left;
    public AudioChannel Channel
    {
        get => _channel;
        set => Set(ref _channel, value);
    }

    private bool _realTime = true;
    /// <summary>Fisierele WAV se redau in timp real (altfel cat de repede se poate).</summary>
    public bool RealTime
    {
        get => _realTime;
        set
        {
            if (Set(ref _realTime, value) && _file != null) _file.RealTime = value;
        }
    }

    /// <summary>Redarea in difuzoare a semnalului primit.</summary>
    public AudioMonitor Monitor { get; }

    private bool _monitorOn;
    /// <summary>MONITOR: semnalul primit se aude in difuzoare (placa de iesire implicita).</summary>
    public bool MonitorOn
    {
        get => _monitorOn;
        set
        {
            if (!Set(ref _monitorOn, value)) return;
            if (value)
            {
                Monitor.Start();
                if (_file is { RealTime: false }) Message = "Monitorizare: se aude doar cu \"Timp real\" bifat.";
            }
            else
            {
                Monitor.Stop();
            }
        }
    }

    private double _monitorVolume = 50;
    /// <summary>Volumul monitorizarii, 0-100.</summary>
    public double MonitorVolume
    {
        get => _monitorVolume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Set(ref _monitorVolume, value) && Monitor != null) Monitor.Volume = (float)(value / 100.0);
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(StartStopText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StartStopText => IsRunning ? "STOP" : "START";

    private string _sourceName = "";
    public string SourceName
    {
        get => _sourceName;
        private set => Set(ref _sourceName, value);
    }

    private bool _isFile;
    public bool IsFile
    {
        get => _isFile;
        private set => Set(ref _isFile, value);
    }

    private double _fileProgress;
    public double FileProgress
    {
        get => _fileProgress;
        private set => Set(ref _fileProgress, value);
    }

    // ------------------------------------------------------------------
    // Starea
    // ------------------------------------------------------------------

    private string _snrText = "SNR: -";
    public string SnrText { get => _snrText; private set => Set(ref _snrText, value); }

    private string _levelText = "Nivel: -";
    public string LevelText { get => _levelText; private set => Set(ref _levelText, value); }

    private double _inputLevelDb = -100;
    /// <summary>Nivelul de intrare (dBFS), pentru indicator.</summary>
    public double InputLevelDb { get => _inputLevelDb; private set => Set(ref _inputLevelDb, value); }

    private string _provisionalText = "";
    public string ProvisionalText { get => _provisionalText; private set => Set(ref _provisionalText, value); }

    private string _message = "";
    public string Message { get => _message; set => Set(ref _message, value); }

    // ------------------------------------------------------------------
    // Setarile decodorului (oglinda RttySettings)
    // ------------------------------------------------------------------

    public IReadOnlyList<RttyDecoderType> Decoders { get; } = Enum.GetValues<RttyDecoderType>();
    public IReadOnlyList<RttyTracking> TrackingModes { get; } = Enum.GetValues<RttyTracking>();

    private double _markFrequencyHz;
    public double MarkFrequencyHz
    {
        get => _markFrequencyHz;
        set
        {
            if (Set(ref _markFrequencyHz, value))
            {
                OnPropertyChanged(nameof(MarkText));
                Push(s => s.MarkFrequencyHz = value);
            }
        }
    }

    public string MarkText => $"MARK {MarkFrequencyHz.ToString("0", CultureInfo.InvariantCulture)} Hz  |  SPACE {(MarkFrequencyHz + (Reverse ? -170 : 170)).ToString("0", CultureInfo.InvariantCulture)} Hz";

    private bool _reverse;
    public bool Reverse
    {
        get => _reverse;
        private set
        {
            if (Set(ref _reverse, value))
            {
                OnPropertyChanged(nameof(ReverseText));
                OnPropertyChanged(nameof(MarkText));
            }
        }
    }

    public string ReverseText => Reverse ? "REV" : "NORM";

    private RttyDecoderType _decoder;
    public RttyDecoderType Decoder
    {
        get => _decoder;
        set
        {
            if (Set(ref _decoder, value))
            {
                OnPropertyChanged(nameof(IsChebyshev));
                if (value == RttyDecoderType.DeepRtty && !Receiver.IsDeepRttyReady) Message = "DeepRTTY: se incarca modelul...";
                Push(s => s.Decoder = value);
            }
        }
    }

    /// <summary>Latimea filtrului si ATC au efect doar pe Chebyshev (si DeepRTTY, unde clasicul ramane pentru SNR/AFC).</summary>
    public bool IsChebyshev => Decoder != RttyDecoderType.MatchedFilter;

    private RttyTracking _tracking;
    public RttyTracking Tracking { get => _tracking; set { if (Set(ref _tracking, value)) Push(s => s.Tracking = value); } }

    private bool _noiseBlanker;
    public bool NoiseBlanker { get => _noiseBlanker; set { if (Set(ref _noiseBlanker, value)) Push(s => s.NoiseBlanker = value); } }

    private double _noiseBlankerThreshold;
    public double NoiseBlankerThreshold { get => _noiseBlankerThreshold; set { if (Set(ref _noiseBlankerThreshold, value)) Push(s => s.NoiseBlankerThreshold = value); } }

    private bool _dnr;
    public bool Dnr { get => _dnr; set { if (Set(ref _dnr, value)) Push(s => s.Dnr = value); } }

    private double _dnrWidthHz;
    public double DnrWidthHz { get => _dnrWidthHz; set { if (Set(ref _dnrWidthHz, value)) Push(s => s.DnrWidthHz = value); } }

    private bool _snrGating;
    public bool SnrGating { get => _snrGating; set { if (Set(ref _snrGating, value)) Push(s => s.SnrGating = value); } }

    private double _snrGatingK;
    public double SnrGatingK { get => _snrGatingK; set { if (Set(ref _snrGatingK, value)) Push(s => s.SnrGatingK = value); } }

    private bool _atc;
    public bool Atc { get => _atc; set { if (Set(ref _atc, value)) Push(s => s.Atc = value); } }

    private bool _flywheel;
    public bool Flywheel { get => _flywheel; set { if (Set(ref _flywheel, value)) Push(s => s.Flywheel = value); } }

    private double _sensitivityPercent;
    /// <summary>Sensibilitatea in procente (0-20), ca pe Android.</summary>
    public double SensitivityPercent { get => _sensitivityPercent; set { if (Set(ref _sensitivityPercent, value)) Push(s => s.Sensitivity = value / 100.0); } }

    private double _filterWidthHz;
    public double FilterWidthHz { get => _filterWidthHz; set { if (Set(ref _filterWidthHz, value)) Push(s => s.FilterWidthHz = value); } }

    private bool _usTty;
    public bool UsTty { get => _usTty; set { if (Set(ref _usTty, value)) Push(s => s.UsTty = value); } }

    public RttySettings ExportSettings() => Receiver.Settings;

    public void ImportSettings(RttySettings settings)
    {
        Receiver.ApplySettings(settings);
        SyncFromSettings(Receiver.Settings);
    }

    private void Push(Action<RttySettings> change)
    {
        if (_syncing) return;
        Receiver.Update(change);
        // valorile pot fi corectate (limite, DNR minim pentru Matched Filter)
        SyncFromSettings(Receiver.Settings);
    }

    private void SyncFromSettings(RttySettings s)
    {
        Scope.SetTones(s.MarkFrequencyHz, s.MarkFrequencyHz + (s.Reverse ? -RttySettings.ShiftHz : RttySettings.ShiftHz));
        _syncing = true;
        try
        {
            MarkFrequencyHz = s.MarkFrequencyHz;
            Reverse = s.Reverse;
            Decoder = s.Decoder;
            Tracking = s.Tracking;
            NoiseBlanker = s.NoiseBlanker;
            NoiseBlankerThreshold = s.NoiseBlankerThreshold;
            Dnr = s.Dnr;
            DnrWidthHz = s.DnrWidthHz;
            SnrGating = s.SnrGating;
            SnrGatingK = s.SnrGatingK;
            Atc = s.Atc;
            Flywheel = s.Flywheel;
            SensitivityPercent = Math.Round(s.Sensitivity * 100, 1);
            FilterWidthHz = s.FilterWidthHz;
            UsTty = s.UsTty;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ------------------------------------------------------------------
    // Actiuni
    // ------------------------------------------------------------------

    public void RefreshDevices()
    {
        string? selectat = SelectedDevice?.Id;
        Devices.Clear();
        try
        {
            foreach (var d in WasapiInputSource.ListDevices()) Devices.Add(d);
        }
        catch (Exception e)
        {
            Message = "Nu pot lista placile de sunet: " + e.Message;
        }
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectat)
                         ?? Devices.FirstOrDefault(d => d.IsDefault)
                         ?? Devices.FirstOrDefault();
    }

    /// <summary>Selecteaza dupa Id (din setarile salvate), daca dispozitivul exista.</summary>
    public void SelectDevice(string? id)
    {
        var d = Devices.FirstOrDefault(x => x.Id == id);
        if (d != null) SelectedDevice = d;
    }

    public void StartLive()
    {
        StopSource();
        var src = new WasapiInputSource(SelectedDevice?.Id, Channel);
        try
        {
            Attach(src, file: false);
            Receiver.Start();
            src.Start();
            SourceName = src.Name + $" ({src.SampleRate} Hz)";
            Message = "";
        }
        catch (Exception e)
        {
            Detach();
            Receiver.Stop();
            Message = "Nu pot porni captura: " + e.Message;
        }
    }

    /// <summary>
    /// Decodeaza un fisier WAV. Cu DeepRTTY, asteapta intai incarcarea modelului (altfel inceputul
    /// fisierului s-ar pierde la decodarea rapida).
    /// </summary>
    public async void OpenFile(string path) => await OpenFileAsync(path);

    public async Task OpenFileAsync(string path)
    {
        StopSource();
        WavFileSource src;
        try
        {
            src = new WavFileSource(path, RealTime, Channel);
        }
        catch (Exception e)
        {
            Message = "Nu pot deschide fisierul: " + e.Message;
            return;
        }
        if (Decoder == RttyDecoderType.DeepRtty && !Receiver.IsDeepRttyReady)
        {
            Message = "DeepRTTY: se incarca modelul...";
            await Receiver.WaitForModelAsync();
        }
        _file = src;
        Attach(src, file: true);
        AppendLocal($"\n--- {src.Name} ({src.SampleRate} Hz, {src.Channels} ch, {src.Duration:hh\\:mm\\:ss}) ---\n");
        Receiver.Start();
        SourceName = src.Name;
        Message = "";
        src.Start();
    }

    public void StopSource()
    {
        var src = _source;
        if (src == null) return;
        Detach();
        try
        {
            src.Stop();
        }
        catch
        {
            // sursa deja oprita
        }
        src.Dispose();
        Receiver.Stop();
        IsRunning = false;
        IsFile = false;
    }

    private void Attach(IAudioSource src, bool file)
    {
        _source = src;
        IsFile = file;
        FileProgress = 0;
        IsRunning = true;
        src.SamplesAvailable += OnSamples;
        src.Stopped += OnSourceStopped;
    }

    private void Detach()
    {
        if (_source != null)
        {
            _source.SamplesAvailable -= OnSamples;
            _source.Stopped -= OnSourceStopped;
        }
        _source = null;
        _file = null;
    }

    private void OnSamples(float[] buffer, int count, int sampleRate)
    {
        Receiver.Process(buffer.AsSpan(0, count), sampleRate);
        Scope.Process(buffer.AsSpan(0, count), sampleRate);
        // fisier decodat "cat de repede se poate": sunetul ar fi accelerat - nu il redam
        var file = _file;
        if (file == null || file.RealTime) Monitor.Write(buffer, count, sampleRate);
    }

    /// <summary>Pe firul sursei: sfarsitul fisierului sau o eroare (placa deconectata).</summary>
    private void OnSourceStopped(Exception? error)
    {
        var src = _source;
        if (error == null && src is WavFileSource)
        {
            Receiver.Flush(); // DeepRTTY: decodeaza si coada ramasa
        }
        _dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_source, src)) return; // deja oprita/inlocuita
            if (error != null) Message = "Sursa audio s-a oprit: " + error.Message;
            else if (src is WavFileSource) Message = "Fisier decodat.";
            Drain();
            StopSource();
        });
    }

    private void AutoTune()
    {
        var mark = Receiver.AutoTune();
        Message = mark is { } m
            ? $"A-TUNE: {m.ToString("0", CultureInfo.InvariantCulture)} Hz"
            : "A-TUNE: semnal prea slab - acordeaza manual (click pe waterfall).";
    }

    private void Clear()
    {
        lock (_textLock) _pendingText.Clear();
        Receiver.ClearDecoderState();
        ProvisionalText = "";
        _callsigns.ResetWord();
        TerminalCleared?.Invoke();
    }

    /// <summary>Mesaj al aplicatiei in terminal (nu text decodat: fara indicative).</summary>
    private void AppendLocal(string text) => InfoAppended?.Invoke(text);

    /// <summary>Pe firul UI: transfera textul si starea adunate de pe firele audio/DeepRTTY.</summary>
    private void Drain()
    {
        string? text = null;
        lock (_textLock)
        {
            if (_pendingText.Length > 0)
            {
                text = _pendingText.ToString();
                _pendingText.Clear();
            }
        }
        if (text != null)
        {
            TextAppended?.Invoke(text);
            _callsigns.Feed(text);
        }

        var p = Interlocked.Exchange(ref _pendingProvisional, null);
        if (p != null) ProvisionalText = Decoder == RttyDecoderType.DeepRtty ? p : "";

        if (Interlocked.Exchange(ref _pendingStatus, null) is RttyStatus st)
        {
            SnrText = $"SNR {st.SnrDb.ToString("0.0", CultureInfo.InvariantCulture)} dB";
            LevelText = $"Nivel {st.InputLevelDbfs.ToString("0", CultureInfo.InvariantCulture)} dBFS";
            InputLevelDb = st.InputLevelDbfs;
            if (Math.Abs(st.MarkFrequencyHz - MarkFrequencyHz) > 0.05)
            {
                // deriva AFC: doar afisam, fara sa trimitem inapoi in receptor
                _syncing = true;
                try { MarkFrequencyHz = Math.Round(st.MarkFrequencyHz, 1); }
                finally { _syncing = false; }
            }
        }

        if (_file != null) FileProgress = _file.Progress;
    }

    public void Dispose()
    {
        _timer.Stop();
        StopSource();
        Monitor.Dispose();
        Receiver.Dispose();
    }
}
