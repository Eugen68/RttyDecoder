namespace RttyDecoder.Core.DeepRtty;

/// <summary>
/// Aduce audio-ul de receptie RTTY (48 kHz, real) la intrarea DeepRTTY: 3200 Hz, cu semnalul
/// centrat pe 800 Hz si Mark JOS (715 Hz) / Space SUS (885 Hz).
///
/// Mixer complex: semnalul e coborat in banda de baza (centrul RTTY -&gt; 0 Hz), filtrat trece-jos
/// si decimat 48000 -&gt; 3200 intr-un singur FIR (se calculeaza doar esantioanele pastrate), apoi
/// urcat pe 800 Hz. In REV (Mark sus) banda de baza se conjuga, adica spectrul se oglindeste.
///
/// Nu e thread-safe: o instanta pe firul audio.
/// </summary>
public sealed class RttyFrontEnd
{
    private readonly int _fsIn;
    private readonly int _decimare;
    private readonly double[] _h;
    private readonly int _n;
    private readonly double[] _bufRe;
    private readonly double[] _bufIm;
    private int _poz;
    private int _contor;

    private double _osRe = 1.0, _osIm;
    private double _urcRe = 1.0, _urcIm;
    private static readonly double UrcPasRe = Math.Cos(2 * Math.PI * DeepRttyConst.CentruHz / DeepRttyConst.SampleRate);
    private static readonly double UrcPasIm = Math.Sin(2 * Math.PI * DeepRttyConst.CentruHz / DeepRttyConst.SampleRate);

    public RttyFrontEnd(int fsIn = 48000)
    {
        if (fsIn % DeepRttyConst.SampleRate != 0)
            throw new ArgumentException("Rata de intrare trebuie sa fie multiplu de 3200 Hz", nameof(fsIn));
        _fsIn = fsIn;
        _decimare = fsIn / DeepRttyConst.SampleRate;
        // Trece-jos ~1000 Hz (banda modelului e +-150 Hz in jurul centrului), Hamming.
        double fc = 1000.0 / fsIn;
        _n = (int)(3.3 * fsIn / 600.0) | 1;
        _h = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            int m = i - _n / 2;
            double sinc = m == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * m) / (Math.PI * m);
            _h[i] = sinc * (0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (_n - 1)));
        }
        _bufRe = new double[_n];
        _bufIm = new double[_n];
    }

    public void Reset()
    {
        Array.Clear(_bufRe);
        Array.Clear(_bufIm);
        _poz = 0;
        _contor = 0;
    }

    /// <summary>
    /// Proceseaza <paramref name="count"/> esantioane PCM 16-bit; scrie esantioanele de 3200 Hz in
    /// <paramref name="output"/> (de la <paramref name="outOffset"/>) si intoarce cate a scris
    /// (cel mult count / 15 + 1).
    /// </summary>
    /// <param name="centruHz">centrul semnalului RTTY (Mark +/- 85 Hz)</param>
    /// <param name="reverse">true daca in audio Mark e SUS (modul REV)</param>
    public int Process(short[] pcm, int count, double centruHz, bool reverse, float[] output, int outOffset)
    {
        double w = 2 * Math.PI * centruHz / _fsIn;
        double pasRe = Math.Cos(w);
        double pasIm = -Math.Sin(w);
        int scrise = 0;
        for (int i = 0; i < count; i++)
        {
            double x = pcm[i] / 32768.0;
            _bufRe[_poz] = x * _osRe;
            _bufIm[_poz] = x * _osIm;
            _poz = (_poz + 1) % _n;
            double nr = _osRe * pasRe - _osIm * pasIm;
            _osIm = _osRe * pasIm + _osIm * pasRe;
            _osRe = nr;

            if (++_contor < _decimare) continue;
            _contor = 0;
            double yr = 0.0, yi = 0.0;
            int idx = _poz; // cel mai vechi esantion
            for (int k = 0; k < _n; k++)
            {
                yr += _h[k] * _bufRe[idx];
                yi += _h[k] * _bufIm[idx];
                if (++idx == _n) idx = 0;
            }
            if (reverse) yi = -yi;
            output[outOffset + scrise++] = (float)(yr * _urcRe - yi * _urcIm);
            double ur = _urcRe * UrcPasRe - _urcIm * UrcPasIm;
            _urcIm = _urcRe * UrcPasIm + _urcIm * UrcPasRe;
            _urcRe = ur;
        }
        // renormalizare anti-drift a celor doi fazori
        double n1 = Math.Sqrt(_osRe * _osRe + _osIm * _osIm);
        _osRe /= n1; _osIm /= n1;
        double n2 = Math.Sqrt(_urcRe * _urcRe + _urcIm * _urcIm);
        _urcRe /= n2; _urcIm /= n2;
        return scrise;
    }
}
