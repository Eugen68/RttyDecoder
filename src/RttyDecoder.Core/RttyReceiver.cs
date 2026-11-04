using System.Text;
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
public sealed class RttyReceiver : IDisposable
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
    private double _blockPeak;
    private double _inputLevelDb = -120;

    private DeepRttyModel? _model;
    private RttyInference? _inference;
    private DeepRttyReceiver? _deep;
    private Task? _modelLoad;

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
        _demod.CodeReceived = OnClassicCode;
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

    public bool IsDeepRttyReady => _inference != null;

    /// <summary>Inlocuieste toate setarile.</summary>
    public void ApplySettings(RttySettings settings) => Update(s => CopyInto(settings, s));

    /// <summary>
    /// Modifica setarile: <paramref name="change"/> primeste o copie a setarilor curente (cu Mark-ul
    /// actual) si o modifica; se aplica doar ce s-a schimbat, deci deriva AFC nu se pierde la o
    /// modificare fara legatura (ex. NB).
    /// </summary>
    public void Update(Action<RttySettings> change)
    {
        RttySettings applied;
        lock (_lock)
        {
            var current = _settings.Clone();
            current.MarkFrequencyHz = _demod.frecventaTunedBaza;
            var next = current.Clone();
            change(next);
            Sanitize(next);
            ApplyToDemod(next, current);
            _settings = next;
            applied = next.Clone();
        }
        UpdateDeepRtty(applied);
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
        RttySettings s;
        lock (_lock)
        {
            if (_running) return;
            _running = true;
            _demod.Start(ProcessingSampleRate);
            ApplyToDemod(_settings, null);
            _spectrum.Reset();
            _resampler = null;
            _blockFill = 0;
            lock (_codecLock) _rx.ResetShift();
            s = _settings.Clone();
        }
        UpdateDeepRtty(s);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
        }
        _deep?.Stop();
    }

    /// <summary>Uita textul neconfirmat al DeepRTTY si starea LTRS/FIGS (butonul CLEAR).</summary>
    public void ClearDecoderState()
    {
        lock (_codecLock) _rx.ResetShift();
        _deep?.Reset();
        ProvisionalTextChanged?.Invoke(string.Empty);
    }

    /// <summary>
    /// Proceseaza audio mono (valori +-1) la rata <paramref name="sampleRate"/>. De apelat de pe un
    /// singur fir; ignorat cat timp receptorul e oprit.
    /// </summary>
    public void Process(ReadOnlySpan<float> samples, int sampleRate)
    {
        lock (_lock)
        {
            if (!_running) return;
            ReadOnlySpan<float> input = samples;
            if (sampleRate != ProcessingSampleRate)
            {
                if (_resampler == null || _resampler.InputRate != sampleRate)
                    _resampler = new Resampler(sampleRate, ProcessingSampleRate);
                int max = _resampler.MaxOutput(samples.Length);
                if (_resampled.Length < max) _resampled = new float[max];
                int n = _resampler.Process(samples, _resampled);
                input = _resampled.AsSpan(0, n);
            }

            foreach (float v in input)
            {
                double a = Math.Abs(v);
                if (a > _blockPeak) _blockPeak = a;
                float c = Math.Clamp(v, -1f, 1f);
                _block[_blockFill++] = (short)Math.Round(c * 32767f);
                if (_blockFill == BlockSize)
                {
                    ProcessBlock(_block, BlockSize);
                    _blockFill = 0;
                }
            }
        }
    }

    /// <summary>Proceseaza direct PCM 16-bit la 48 kHz (fara reesantionare).</summary>
    public void Process48k(short[] pcm, int count)
    {
        lock (_lock)
        {
            if (!_running) return;
            ProcessBlock(pcm, count);
        }
    }

    /// <summary>
    /// Sfarsitul unei surse finite (fisier): proceseaza restul din buffer, asteapta DeepRTTY sa
    /// termine si confirma si coada lui provizorie.
    /// </summary>
    public void Flush(TimeSpan? timeout = null)
    {
        DeepRttyReceiver? deep;
        lock (_lock)
        {
            if (!_running) return;
            if (_blockFill > 0)
            {
                ProcessBlock(_block, _blockFill);
                _blockFill = 0;
            }
            deep = _settings.Decoder == RttyDecoderType.DeepRtty ? _deep : null;
        }
        deep?.Flush(timeout ?? TimeSpan.FromMinutes(2));
    }

    /// <summary>Asteapta incarcarea modelului DeepRTTY, daca e in curs.</summary>
    public Task WaitForModelAsync() => _modelLoad ?? Task.CompletedTask;

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

    private void OnClassicCode(int code)
    {
        if (_settings.Decoder == RttyDecoderType.DeepRtty) return; // textul vine de la model
        EmitCodes(new[] { code });
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

    private void UpdateDeepRtty(RttySettings s)
    {
        bool vrea = s.Decoder == RttyDecoderType.DeepRtty;
        if (!vrea)
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
        bool running;
        lock (_lock) running = _running;
        if (running) _deep!.Start();
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

    private void StartModelLoad()
    {
        if (_modelLoad != null) return;
        _modelLoad = Task.Run(() =>
        {
            try
            {
                var model = _modelFactory!();
                _model = model;
                _inference = model.Run;
                Message?.Invoke("DeepRTTY: model incarcat");
                UpdateDeepRtty(Settings);
            }
            catch (Exception e)
            {
                _modelLoad = null;
                Message?.Invoke("DeepRTTY indisponibil (" + e.Message + ") - revin pe Chebyshev");
                Update(x => x.Decoder = RttyDecoderType.Chebyshev);
                SettingsChanged?.Invoke(Settings);
            }
        });
    }

    public void Dispose()
    {
        Stop();
        _deep?.Dispose();
        _model?.Dispose();
    }
}
