using System.Text;
using System.Diagnostics;
using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.DeepRtty;
using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Core;

/// <summary>Starea curenta a receptiei, raportata de ~10 ori pe secunda.</summary>
public readonly record struct RttyStatus(double SnrDb, double MarkFrequencyHz, bool Reverse, double InputLevelDbfs);

/// <summary>
/// Receptorul RTTY complet - API-ul public al modulului. Nu are fir propriu si nu depinde de UI sau
/// de sursa audio: gazda apeleaza <see cref="Process"/> cu audio mono la orice rata (se reesantioneaza
/// intern la 48 kHz), de pe un singur fir (de regula firul sursei audio).
///
/// Evenimentele vin pe firul audio (decodoarele clasice, spectrul, starea) sau pe firul DeepRTTY;
/// o interfata grafica trebuie sa le treaca pe firul ei.
/// Metodele publice sunt thread-safe (un lacat comun cu <see cref="Process"/>).
/// </summary>
public sealed class RttyReceiver : IDisposable, IAsyncDisposable
{
    public const int ProcessingSampleRate = 48000;
    private const int BlockSize = 3840; // 80 ms la 48 kHz, ca pe Android

    private readonly object _lock = new();
    private readonly object _codecLock = new();
    private readonly RttyDemodulator _demod = new();
    private readonly BaudotCodec _rx = new();
    private readonly SpectrumAnalyzer _spectrum = new();
    private readonly Func<DeepRttyModel>? _modelFactory;
    private RttySettings _settings = new();

    private Resampler? _resampler;
    private float[] _resampled = Array.Empty<float>();
    private readonly short[] _block = new short[BlockSize];
    private int _blockFill;
    private bool _running;
    private long _runId;
    private double _blockPeak;
    private double _inputLevelDb = -120;

    private DeepRttyModel? _model;
    private RttyInference? _inference;
    private DeepRttyReceiver? _deep;
    private readonly TextSquelch _squelch;
    private Task? _modelLoad;
    private bool _disposed;
    private Task _shutdown = Task.CompletedTask;

    /// <summary>Text confirmat (unul sau mai multe caractere; '\n' pentru rand nou, CR ignorat).</summary>
    public event Action<string>? TextReceived;

    /// <summary>Textul provizoriu al DeepRTTY (coada inca neconfirmata; se inlocuieste de fiecare data).</summary>
    public event Action<string>? ProvisionalTextChanged;

    public event Action<RttyStatus>? StatusUpdated;

    /// <summary>Un rand de spectru pentru waterfall: magnitudini liniare, <see cref="SpectrumBinHz"/> Hz/bin, de la 0 Hz.</summary>
    public event Action<float[]>? SpectrumAvailable;

    /// <summary>Setarile s-au schimbat din interior (A-TUNE, revenire din DeepRTTY indisponibil).</summary>
    public event Action<RttySettings>? SettingsChanged;

    /// <summary>Mesaje pentru utilizator (model DeepRTTY incarcat / indisponibil, erori).</summary>
    public event Action<string>? Message;

    public static double SpectrumBinHz => SpectrumAnalyzer.BinHz;
    public static int SpectrumBins => SpectrumAnalyzer.Bins;

    /// <param name="modelFactory">
    /// Cum se incarca modelul DeepRTTY. Implicit: modelul inclus in DLL. Se incarca la prima alegere a
    /// decodorului DeepRTTY, pe un fir de fundal.
    /// </param>
    public RttyReceiver(Func<DeepRttyModel>? modelFactory = null)
    {
        _modelFactory = modelFactory ?? (() => DeepRttyModel.LoadEmbedded());
        // textul decodorului clasic trece prin squelch (fiecare caracter terminat, valid sau nu)
        _squelch = new TextSquelch(code => EmitCodes(new[] { code }));
        _demod.FrameCompleted = OnClassicFrame;
        _demod.StatusUpdated = OnDemodStatus;
        _spectrum.RowAvailable += row => SpectrumAvailable?.Invoke(row);
        ApplyToDemod(_settings, null);
    }

    /// <summary>Constructor pentru teste: inferenta data direct (fara ONNX Runtime).</summary>
    public RttyReceiver(RttyInference inference) : this(modelFactory: null)
    {
        _inference = inference;
    }

    /// <summary>Copie a setarilor curente (cu Mark-ul actual, inclusiv deriva AFC).</summary>
    public RttySettings Settings
    {
        get
        {
            lock (_lock)
            {
                var s = _settings.Clone();
                s.MarkFrequencyHz = _demod.frecventaTunedBaza;
                return s;
            }
        }
    }

    public bool IsRunning
    {
        get { lock (_lock) return _running; }
    }

    public bool IsDeepRttyReady
    {
        get { lock (_lock) return !_disposed && _inference != null; }
    }

    /// <summary>Inlocuieste toate setarile.</summary>
    public void ApplySettings(RttySettings settings) => Update(s => CopyInto(settings, s));

    /// <summary>
    /// Modifica setarile: <paramref name="change"/> primeste o copie a setarilor curente (cu Mark-ul
    /// actual) si o modifica; se aplica doar ce s-a schimbat, deci deriva AFC nu se pierde la o
    /// modificare fara legatura (ex. NB).
    /// </summary>
    public void Update(Action<RttySettings> change)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var current = _settings.Clone();
            current.MarkFrequencyHz = _demod.frecventaTunedBaza;
            var next = current.Clone();
            change(next);
            Sanitize(next);
            ApplyToDemod(next, current);
            _settings = next;
        }
        UpdateDeepRtty();
    }

    /// <summary>NORM &lt;-&gt; REV pastrand tonurile pe loc (Mark trece pe celalalt ton), ca pe Android.</summary>
    public void ToggleReverse() => Update(s =>
    {
        s.MarkFrequencyHz += s.Reverse ? -RttySettings.ShiftHz : RttySettings.ShiftHz;
        s.Reverse = !s.Reverse;
    });

    /// <summary>Acord pe semnalul cel mai puternic (pereche de tonuri la 170 Hz). Intoarce Mark-ul gasit, sau null.</summary>
    /// <summary>
    /// Tonul de jos al celei mai puternice perechi RTTY (170 Hz) din spectrul mediat, fara sa schimbe
    /// acordul (Hz), sau null daca nu e semnal destul de puternic.
    /// </summary>
    public double? FindLowerTone()
    {
        lock (_lock)
        {
            var avg = _spectrum.AverageSpectrum();
            return avg == null ? null : Dsp.AutoTune.FindLowerTone(avg, SpectrumAnalyzer.BinHz);
        }
    }

    public double? AutoTune()
    {
        double? mark;
        lock (_lock)
        {
            var avg = _spectrum.AverageSpectrum();
            if (avg == null) return null;
            var lower = Dsp.AutoTune.FindLowerTone(avg, SpectrumAnalyzer.BinHz);
            if (lower == null) return null;
            mark = _settings.Reverse ? lower.Value + RttySettings.ShiftHz : lower.Value;
        }
        Update(s => s.MarkFrequencyHz = Math.Round(mark.Value, 1));
        SettingsChanged?.Invoke(Settings);
        return mark;
    }

    /// <summary>Porneste receptia (reseteaza starea de decodare).</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;
            _running = true;
            _runId++;
            _demod.Start(ProcessingSampleRate);
            ApplyToDemod(_settings, null);
            _spectrum.Reset();
            _resampler = null;
            _blockFill = 0;
            _blockPeak = 0;
            _inputLevelDb = -120;
            lock (_codecLock) _rx.ResetShift();
        }
        UpdateDeepRtty();
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            _deep?.Stop();
        }
    }

    /// <summary>Uita textul neconfirmat al DeepRTTY si starea LTRS/FIGS (butonul CLEAR).</summary>
    public void ClearDecoderState()
    {
        lock (_lock)
        {
            _deep?.Reset();
            lock (_codecLock) _rx.ResetShift();
            _squelch.Reset();
        }
        ProvisionalTextChanged?.Invoke(string.Empty);
    }

    /// <summary>
    /// Audio mono +-1, de pe un singur fir. waitForDecoder=true pentru o sursa finita (WAV):
    /// citirea asteapta bufferul DeepRTTY fara sa tina lacatul receptorului. Implicit live:
    /// la suprasarcina decodorul abandoneaza audio-ul vechi si recupereaza de la semnalul nou.
    /// </summary>
    public void Process(ReadOnlySpan<float> samples, int sampleRate, bool waitForDecoder = false)
    {
        long run;
        ReadOnlySpan<float> input = samples;
        lock (_lock)
        {
            if (!_running) return;
            run = _runId;
            if (sampleRate != ProcessingSampleRate)
            {
                if (_resampler == null || _resampler.InputRate != sampleRate)
                    _resampler = new Resampler(sampleRate, ProcessingSampleRate);
                int max = _resampler.MaxOutput(samples.Length);
                if (_resampled.Length < max) _resampled = new float[max];
                int n = _resampler.Process(samples, _resampled);
                input = _resampled.AsSpan(0, n);
            }
        }
        while (!input.IsEmpty)
        {
            if (!WaitForDecoderCapacity(run, waitForDecoder)) return;
            lock (_lock)
            {
                if (!_running || _runId != run) return;
                int n = Math.Min(BlockSize - _blockFill, input.Length);
                foreach (float v in input[..n])
                {
                    _blockPeak = Math.Max(_blockPeak, Math.Abs(v));
                    _block[_blockFill++] = (short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f);
                }
                input = input[n..];
                if (_blockFill == BlockSize)
                {
                    ProcessBlock(_block, BlockSize);
                    _blockFill = 0;
                }
            }
        }
    }

    /// <summary>PCM 16-bit la 48 kHz. waitForDecoder are acelasi sens ca la Process.</summary>
    public void Process48k(short[] pcm, int count, bool waitForDecoder = false)
    {
        long run;
        lock (_lock)
        {
            if (!_running) return;
            run = _runId;
        }
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > pcm.Length) throw new ArgumentOutOfRangeException(nameof(count));
        for (int offset = 0; offset < count; offset += BlockSize)
        {
            if (!WaitForDecoderCapacity(run, waitForDecoder)) return;
            int n = Math.Min(BlockSize, count - offset);
            short[] block = offset == 0 && n == count ? pcm : pcm.AsSpan(offset, n).ToArray();
            lock (_lock)
            {
                if (!_running || _runId != run) return;
                foreach (short v in block.AsSpan(0, n))
                    _blockPeak = Math.Max(_blockPeak, Math.Abs((double)v) / 32768.0);
                ProcessBlock(block, n);
            }
        }
    }

    private bool WaitForDecoderCapacity(long run, bool wait, TimeSpan? timeout = null)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            DeepRttyReceiver? deep;
            lock (_lock)
            {
                if (!_running || _runId != run) return false;
                deep = _settings.Decoder == RttyDecoderType.DeepRtty ? _deep : null;
            }
            if (!wait || deep == null) return true;
            TimeSpan? remaining = timeout is { } limit ? limit - Stopwatch.GetElapsedTime(start) : null;
            if (deep.WaitForCapacity(remaining)) return true;
            if (timeout is { } budget && Stopwatch.GetElapsedTime(start) >= budget) return false;
            // STOP, CLEAR sau schimbarea decodorului pot invalida sesiunea asteptata.
        }
    }

    public DeepRttyQueueStatus DeepRttyQueueStatus
    {
        get { lock (_lock) return _deep?.QueueStatus ?? default; }
    }

    /// <summary>
    /// Sfarsitul unei surse finite (fisier): proceseaza restul din buffer, asteapta DeepRTTY sa
    /// termine si confirma si coada lui provizorie.
    /// </summary>
    public void Flush(TimeSpan? timeout = null) => TryFlush(timeout);

    /// <summary>Ca Flush, dar raporteaza timeout-ul sau invalidarea sesiunii fara a declara fisierul complet.</summary>
    public bool TryFlush(TimeSpan? timeout = null)
    {
        var budget = timeout ?? TimeSpan.FromMinutes(2);
        if (budget < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        long start = Stopwatch.GetTimestamp();
        long run;
        lock (_lock) { if (!_running) return true; run = _runId; }
        if (!WaitForDecoderCapacity(run, wait: true, timeout: budget)) return false;
        DeepRttyReceiver? deep;
        lock (_lock)
        {
            if (!_running || _runId != run) return false;
            if (_blockFill > 0)
            {
                ProcessBlock(_block, _blockFill);
                _blockFill = 0;
            }
            deep = _settings.Decoder == RttyDecoderType.DeepRtty ? _deep : null;
        }
        var remaining = budget - Stopwatch.GetElapsedTime(start);
        return deep == null || deep.Flush(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    /// <summary>Asteapta incarcarea modelului DeepRTTY, daca e in curs.</summary>
    public Task WaitForModelAsync()
    {
        lock (_lock) return _modelLoad ?? Task.CompletedTask;
    }

    private void ProcessBlock(short[] pcm, int count)
    {
        _inputLevelDb = 20 * Math.Log10(Math.Max(_blockPeak, 1e-6));
        _blockPeak = 0;
        _spectrum.Process(pcm, count);
        _demod.ProcessBlock(pcm, count);
        if (_settings.Decoder == RttyDecoderType.DeepRtty && _deep is { IsRunning: true } deep)
        {
            double centru = _demod.frecventaTunedBaza + (_demod.regimReverseSoftware ? -RttySettings.ShiftHz / 2 : RttySettings.ShiftHz / 2);
            deep.Feed(pcm, count, centru, _demod.regimReverseSoftware);
        }
    }

    // ------------------------------------------------------------------
    // Text
    // ------------------------------------------------------------------

    private void OnClassicFrame(bool valid, int code, double startBits)
    {
        if (_settings.Decoder == RttyDecoderType.DeepRtty) return; // textul vine de la model
        FrameProbe?.Invoke(valid, startBits);
        _squelch.Frame(valid, code, startBits);
    }

    /// <summary>Doar pentru teste/diagnostic: fiecare caracter terminat (valid, inceput in biti).</summary>
    internal Action<bool, double>? FrameProbe;

    /// <summary>SQL deschis: textul decodorului clasic iese (pentru afisare).</summary>
    public bool SquelchOpen
    {
        get { lock (_lock) return _squelch.IsOpen || !_squelch.Enabled; }
    }

    private void EmitCodes(int[] codes)
    {
        var sb = new StringBuilder(codes.Length);
        lock (_codecLock)
        {
            foreach (int code in codes)
            {
                char c = _rx.Decode(code);
                if (c == '\0' || c == '\u0007') continue;
                if (c == '\n' && code == 8) continue; // CR ignorat; LF (2) da randul nou
                sb.Append(c);
            }
        }
        if (sb.Length > 0) TextReceived?.Invoke(sb.ToString());
    }

    private void OnProvisionalCodes(int[] codes)
    {
        var sb = new StringBuilder(codes.Length);
        lock (_codecLock)
        {
            // copie: LTRS/FIGS din textul provizoriu nu schimba starea codecului real
            var copie = _rx.Copy();
            foreach (int code in codes)
            {
                char c = copie.Decode(code);
                if (c == '\0' || c == '\u0007') continue;
                if (c == '\n' && code == 8) continue;
                sb.Append(c);
            }
        }
        ProvisionalTextChanged?.Invoke(sb.ToString());
    }

    private void OnDemodStatus(double snrDb, double markHz)
    {
        StatusUpdated?.Invoke(new RttyStatus(snrDb, markHz, _demod.regimReverseSoftware, _inputLevelDb));
    }

    // ------------------------------------------------------------------
    // Setari -> demodulator
    // ------------------------------------------------------------------

    private static void CopyInto(RttySettings src, RttySettings dst)
    {
        dst.MarkFrequencyHz = src.MarkFrequencyHz;
        dst.Reverse = src.Reverse;
        dst.Decoder = src.Decoder;
        dst.Tracking = src.Tracking;
        dst.NoiseBlanker = src.NoiseBlanker;
        dst.NoiseBlankerThreshold = src.NoiseBlankerThreshold;
        dst.Dnr = src.Dnr;
        dst.DnrWidthHz = src.DnrWidthHz;
        dst.SnrGating = src.SnrGating;
        dst.SnrGatingK = src.SnrGatingK;
        dst.Atc = src.Atc;
        dst.Flywheel = src.Flywheel;
        dst.Sensitivity = src.Sensitivity;
        dst.FilterWidthHz = src.FilterWidthHz;
        dst.UsTty = src.UsTty;
        dst.Squelch = src.Squelch;
        dst.SquelchLevel = src.SquelchLevel;
    }

    private static void Sanitize(RttySettings s)
    {
        s.MarkFrequencyHz = Math.Clamp(s.MarkFrequencyHz, 200, 3800);
        s.NoiseBlankerThreshold = Math.Clamp(s.NoiseBlankerThreshold, 2, 12);
        s.DnrWidthHz = Math.Clamp(s.DnrWidthHz, RttySettings.MinDnrWidthHz, RttySettings.MaxDnrWidthHz);
        // Cu Matched Filter, un DNR prea ingust taie din energia Mark/Space inainte de corelator.
        if (s.Decoder == RttyDecoderType.MatchedFilter)
            s.DnrWidthHz = Math.Max(s.DnrWidthHz, RttySettings.MinDnrWidthMatchedFilterHz);
        s.SnrGatingK = Math.Clamp(s.SnrGatingK, 1, 3);
        s.Sensitivity = Math.Clamp(s.Sensitivity, 0, 0.2);
        s.FilterWidthHz = Math.Clamp(s.FilterWidthHz, RttySettings.MinFilterWidthHz, RttySettings.MaxFilterWidthHz);
        s.SquelchLevel = Math.Clamp(s.SquelchLevel, 0.1, 0.8);
    }

    /// <summary>Aplica diferentele (sau totul, daca <paramref name="old"/> e null). Sub _lock.</summary>
    private void ApplyToDemod(RttySettings s, RttySettings? old)
    {
        var d = _demod;
        bool all = old == null;

        d.modUrmarireFrecventa = (int)s.Tracking;
        d.folosesteNoiseBlanker = s.NoiseBlanker;
        d.nbFactorPrag = s.NoiseBlankerThreshold;
        d.folosesteSnrGating = s.SnrGating;
        d.snrGatingK = s.SnrGatingK;
        d.folosesteAtc = s.Atc;
        d.folosesteVolant = s.Flywheel;
        d.pragSensibilitate = s.Sensitivity;
        lock (_codecLock) _rx.UsTty = s.UsTty;
        _squelch.Enabled = s.Squelch;
        _squelch.Threshold = s.SquelchLevel;

        bool mf = s.Decoder == RttyDecoderType.MatchedFilter;
        bool decoderChanged = all || d.folosesteMatchedFilter != mf;
        if (decoderChanged)
        {
            d.folosesteMatchedFilter = mf;
            if (mf && d.mfBuffer == null) d.InitMatchedFilterTemplates(ProcessingSampleRate, RttyDemodulator.BaudRtty);
            d.RecalculeazaSabloaneMf();
            d.solicitaResetFiltre = true;
        }

        double rCheb = 1.0 - s.FilterWidthHz * 2.0 * Math.PI / ProcessingSampleRate;
        if (all || Math.Abs(old!.FilterWidthHz - s.FilterWidthHz) > 1e-9)
        {
            d.rCheb = rCheb;
            d.solicitaResetFiltre = true;
        }

        bool tuneChanged = all
                           || Math.Abs(old!.MarkFrequencyHz - s.MarkFrequencyHz) > 1e-9
                           || old.Reverse != s.Reverse;
        if (tuneChanged)
        {
            d.frecventaTunedBaza = s.MarkFrequencyHz;
            d.regimReverseSoftware = s.Reverse;
            _squelch.Reset();   // alt semnal: se judeca de la capat
        }

        d.folosesteDnr = s.Dnr;
        d.latimeDnrHz = s.DnrWidthHz;
        if (tuneChanged || decoderChanged)
        {
            d.sampleRateFiltre = ProcessingSampleRate;
            d.SincronizeazaCoeficienti(); // include DNR si sabloanele MF; cere reset de filtre
        }
        else if (Math.Abs(old!.DnrWidthHz - s.DnrWidthHz) > 1e-9)
        {
            d.CalculeazaCoeficientiDnr(d.sampleRateFiltre);
        }
    }

    // ------------------------------------------------------------------
    // DeepRTTY
    // ------------------------------------------------------------------

    private void UpdateDeepRtty()
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_settings.Decoder != RttyDecoderType.DeepRtty)
            {
                _deep?.Stop();
                ProvisionalTextChanged?.Invoke(string.Empty);
                return;
            }
            if (_inference == null)
            {
                StartModelLoad();
                return;
            }
            EnsureDeepReceiver();
            if (_running) _deep!.Start();
        }
    }

    private void EnsureDeepReceiver()
    {
        if (_deep != null) return;
        var deep = new DeepRttyReceiver(_inference!);
        deep.Confirmed += EmitCodes;
        deep.Provisional += OnProvisionalCodes;
        deep.Error += e => Message?.Invoke("DeepRTTY: " + e.Message);
        _deep = deep;
    }

    // Apelat sub _lock: o singura incarcare, inclusiv la cereri simultane.
    private void StartModelLoad()
    {
        if (_modelLoad is { IsCompleted: false }) return;
        _modelLoad = Task.Run(() =>
        {
            DeepRttyModel model;
            try
            {
                model = _modelFactory!();
            }
            catch (Exception e)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    Message?.Invoke("DeepRTTY indisponibil (" + e.Message + ")");
                    // O incarcare veche nu schimba alegerea facuta intre timp de utilizator.
                    if (_settings.Decoder != RttyDecoderType.DeepRtty) return;
                    var next = _settings.Clone();
                    next.Decoder = RttyDecoderType.Chebyshev;
                    ApplyToDemod(next, _settings);
                    _settings = next;
                    SettingsChanged?.Invoke(Settings);
                }
                return;
            }
            lock (_lock)
            {
                if (_disposed)
                {
                    model.Dispose();
                    return;
                }
                _model = model;
                _inference = model.Run;
                UpdateDeepRtty();
                Message?.Invoke("DeepRTTY: model incarcat");
            }
        });
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _inference = null;
            _deep?.Dispose();
            var model = _model;
            // Unele inferente native nu pot fi intrerupte. Modelul ramane valid pana ies din ele.
            if (_deep == null || _deep.Completion.IsCompleted) model?.Dispose();
            else _shutdown = _deep.Completion.ContinueWith(_ => model?.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Ca Dispose, dar asteapta si incarcarea/inferenta nativa si eliberarea modelului.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        lock (_lock) return new ValueTask(Task.WhenAll(_modelLoad ?? Task.CompletedTask, _shutdown));
    }
}
