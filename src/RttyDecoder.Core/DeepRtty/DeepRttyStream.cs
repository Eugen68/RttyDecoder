namespace RttyDecoder.Core.DeepRtty;

/// <summary>Inferenta: primeste [1, 1, T, 13] si intoarce log-probabilitatile [1, T', 33] cu forma lor.</summary>
public delegate (float[] Data, long[] Dims)? RttyInference(float[] input, long[] dims);

/// <summary>
/// Spectrograma de intrare DeepRTTY - port al <c>audio_to_spectrogram</c> din
/// examples/python/rtty_stream.py: STFT centrat (reflect-pad 64), Hann periodica de 96 in cadru de
/// 128, pas 24, bin-urile 26..38 (650-950 Hz) si normalizarea "noise_q25_active_log1p".
/// </summary>
public static class DeepRttySpectrogram
{
    private static readonly float[][] CosTab;
    private static readonly float[][] SinTab;

    static DeepRttySpectrogram()
    {
        var fereastra = new double[DeepRttyConst.FftLength];
        int off = (DeepRttyConst.FftLength - DeepRttyConst.WinLength) / 2;
        for (int i = 0; i < DeepRttyConst.WinLength; i++)
            fereastra[off + i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / DeepRttyConst.WinLength);

        CosTab = new float[DeepRttyConst.Bins][];
        SinTab = new float[DeepRttyConst.Bins][];
        for (int b = 0; b < DeepRttyConst.Bins; b++)
        {
            int k = DeepRttyConst.StartBin + b;
            CosTab[b] = new float[DeepRttyConst.FftLength];
            SinTab[b] = new float[DeepRttyConst.FftLength];
            for (int n = 0; n < DeepRttyConst.FftLength; n++)
            {
                CosTab[b][n] = (float)(fereastra[n] * Math.Cos(2 * Math.PI * k * n / DeepRttyConst.FftLength));
                SinTab[b][n] = (float)(fereastra[n] * Math.Sin(2 * Math.PI * k * n / DeepRttyConst.FftLength));
            }
        }
    }

    /// <returns>datele [1, 1, cadre, 13] si dimensiunile, sau null daca audio-ul e prea scurt.</returns>
    public static (float[] Data, long[] Dims)? Compute(float[] audio)
    {
        const int pad = DeepRttyConst.FftLength / 2;
        if (audio.Length <= pad + 1) return null;
        var p = new float[audio.Length + 2 * pad];
        for (int i = 0; i < pad; i++)
        {
            p[i] = audio[pad - i];
            p[pad + audio.Length + i] = audio[audio.Length - 2 - i];
        }
        Array.Copy(audio, 0, p, pad, audio.Length);

        const int bins = DeepRttyConst.Bins;
        int cadre = (p.Length - DeepRttyConst.FftLength) / DeepRttyConst.HopLength + 1;
        var mag = new float[cadre * bins];
        for (int t = 0; t < cadre; t++)
        {
            int o = t * DeepRttyConst.HopLength;
            for (int b = 0; b < bins; b++)
            {
                float[] c = CosTab[b];
                float[] s = SinTab[b];
                float re = 0f, im = 0f;
                for (int n = 0; n < DeepRttyConst.FftLength; n++)
                {
                    float v = p[o + n];
                    re += v * c[n];
                    im -= v * s[n];
                }
                mag[t * bins + b] = MathF.Sqrt(re * re + im * im);
            }
        }

        // noise_q25_active_log1p: nivelul zgomotului = percentila 25 a magnitudinilor din cadrele
        // "active" (nu tacere), corectata Rayleigh; cadrele tacute ar trage-o in jos.
        int rangCadru = (int)Math.Floor(DeepRttyConst.NoiseLevelQuantile * (bins - 1));
        var niveluri = new float[cadre];
        var tmp = new float[bins];
        for (int t = 0; t < cadre; t++)
        {
            Array.Copy(mag, t * bins, tmp, 0, bins);
            Array.Sort(tmp);
            niveluri[t] = tmp[rangCadru];
        }
        var sortate = (float[])niveluri.Clone();
        Array.Sort(sortate);
        float referinta = sortate[(int)Math.Floor(DeepRttyConst.SilentFrameReferenceQuantile * (cadre - 1))];
        float pragActiv = referinta * DeepRttyConst.SilentFrameRelativeLevel;

        int activeCount = 0;
        for (int t = 0; t < cadre; t++) if (niveluri[t] >= pragActiv) activeCount++;
        var sursa = new float[activeCount * bins];
        int j = 0;
        float maxim = 0f;
        for (int t = 0; t < cadre; t++)
        {
            bool activ = niveluri[t] >= pragActiv;
            for (int b = 0; b < bins; b++)
            {
                float v = mag[t * bins + b];
                if (v > maxim) maxim = v;
                if (activ) sursa[j++] = v;
            }
        }
        Array.Sort(sursa);
        float q25 = sursa.Length == 0 ? 0f : sursa[(int)Math.Floor(DeepRttyConst.NoiseLevelQuantile * (sursa.Length - 1))];
        float zgomot = Math.Max(Math.Max(q25 / DeepRttyConst.NoiseLevelRayleighFactor,
            maxim * DeepRttyConst.NoiseLevelMinRelativeToPeak), DeepRttyConst.NoiseLevelMin);
        for (int i = 0; i < mag.Length; i++) mag[i] = (float)Math.Log(1.0 + mag[i] / zgomot);
        return (mag, new long[] { 1, 1, cadre, bins });
    }
}

/// <summary>
/// Decodarea continua DeepRTTY (port al StreamingDecoder din rtty_stream.py): ferestre de 12 s
/// pornite la ~8 s pe grila cadrelor de iesire, imbinate cu <see cref="OverlapMerger"/> - fiecare
/// caracter iese o singura data. <see cref="Preview"/> decodeaza coada inca neconfirmata.
/// Nu e thread-safe: un singur fir de decodare.
/// </summary>
public sealed class DeepRttyStream
{
    private readonly RttyInference _inferenta;
    private float[] _buf = Array.Empty<float>();
    private long _bufStart;
    private long _nextWindow;
    private long _lastWindowStart = -1;
    private OverlapMerger _merger = NewMerger();
    private long _ultimulConfirmat = long.MinValue;

    public DeepRttyStream(RttyInference inferenta) => _inferenta = inferenta;

    private static OverlapMerger NewMerger() => new(DeepRttyConst.EdgeGuard, DeepRttyConst.Tolerance);

    public long Total => _bufStart + _buf.Length;

    public void Reset()
    {
        _buf = Array.Empty<float>();
        _bufStart = 0;
        _nextWindow = 0;
        _lastWindowStart = -1;
        _merger = NewMerger();
        _ultimulConfirmat = long.MinValue;
    }

    /// <summary>Adauga audio (3200 Hz); intoarce caracterele confirmate de ferestrele complete noi.</summary>
    public List<CtcToken> Push(float[] x, int n)
    {
        if (n > 0)
        {
            var nou = new float[_buf.Length + n];
            Array.Copy(_buf, nou, _buf.Length);
            Array.Copy(x, 0, nou, _buf.Length, n);
            _buf = nou;
        }
        var confirmate = new List<CtcToken>();
        while (_nextWindow + DeepRttyConst.Window <= Total)
        {
            int off = (int)(_nextWindow - _bufStart);
            var fereastra = _buf.AsSpan(off, DeepRttyConst.Window).ToArray();
            var tokeni = Decode(fereastra, _nextWindow) ?? new List<CtcToken>();
            var c = _merger.Push(_nextWindow, DeepRttyConst.Window, tokeni);
            if (c.Count > 0) _ultimulConfirmat = c[^1].Sample;
            confirmate.AddRange(c);
            _lastWindowStart = _nextWindow;
            _nextWindow += DeepRttyConst.Hop;
        }
        // Pastram doar ce mai trebuie: de la inceputul ultimei ferestre (pentru preview) incolo.
        long pastreazaDe = Math.Max(_bufStart, _lastWindowStart >= 0 ? _lastWindowStart : _bufStart);
        if (pastreazaDe > _bufStart)
        {
            _buf = _buf.AsSpan((int)(pastreazaDe - _bufStart)).ToArray();
            _bufStart = pastreazaDe;
        }
        return confirmate;
    }

    /// <summary>
    /// Coada neconfirmata: decodeaza ultimele &lt;= 12 s (completate in oglinda pana la 12 s, ca in
    /// referinta) si intoarce caracterele de dupa textul deja confirmat.
    /// </summary>
    public List<CtcToken> Preview()
    {
        long start = Math.Max(Math.Max(_lastWindowStart >= 0 ? _lastWindowStart : _bufStart, Total - DeepRttyConst.Window), _bufStart);
        int lungime = (int)(Total - start);
        if (lungime < DeepRttyConst.SampleRate) return new List<CtcToken>();
        var audio = _buf.AsSpan((int)(start - _bufStart)).ToArray();
        var tokeni = Decode(Oglinda(audio, DeepRttyConst.Window), start);
        if (tokeni == null) return new List<CtcToken>();
        double dupa = Math.Max(_merger.CommittedUntil, _ultimulConfirmat + DeepRttyConst.Tolerance);
        long total = Total;
        return tokeni.Where(t => t.Sample > dupa && t.Sample < total).ToList();
    }

    /// <summary>Ultima eroare de inferenta (fereastra respectiva e tratata ca goala), sau null.</summary>
    public Exception? LastError { get; set; }

    private List<CtcToken>? Decode(float[] audio, long start)
    {
        var spec = DeepRttySpectrogram.Compute(audio);
        if (spec == null) return null;
        (float[] Data, long[] Dims)? rez;
        try
        {
            rez = _inferenta(spec.Value.Data, spec.Value.Dims);
        }
        catch (Exception e1)
        {
            // O singura reincercare; daca tot nu merge, fereastra e goala. Exceptia NU are voie sa
            // iasa din Push: ar arunca si caracterele deja confirmate de ferestrele dinainte.
            try
            {
                rez = _inferenta(spec.Value.Data, spec.Value.Dims);
            }
            catch (Exception e2)
            {
                LastError = new AggregateException(e1, e2);
                return null;
            }
        }
        if (rez == null) return null;
        return CtcTokens.Greedy(rez.Value.Data, rez.Value.Dims, DeepRttyConst.BlankIndex)
            .Select(t => new CtcToken(start + (long)Math.Round((double)(t.Frame * DeepRttyConst.OutputHopLength), MidpointRounding.AwayFromZero), t.Index, t.Score))
            .ToList();
    }

    /// <summary>Completare "symmetric" (ca numpy.pad) pana la lungime.</summary>
    private static float[] Oglinda(float[] a, int lungime)
    {
        if (a.Length >= lungime) return a;
        int perioada = 2 * a.Length;
        var r = new float[lungime];
        for (int i = 0; i < lungime; i++)
        {
            int k = i % perioada;
            r[i] = k < a.Length ? a[k] : a[perioada - 1 - k];
        }
        return r;
    }
}
