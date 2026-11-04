namespace RttyDecoder.Core.Dsp;

/// <summary>
/// Demodulatorul RTTY clasic (45.45 baud, shift 170 Hz) - port 1:1 din RttyDemodulator.java
/// (aplicatia Android). Primeste esantioane PCM 16-bit la 48 kHz si livreaza coduri Baudot de 5 biti.
///
/// Lantul, in ordine: DC blocker -&gt; Noise Blanker -&gt; DNR (bandpass) -&gt; detector Mark/Space
/// (rezonatoare Chebyshev sau Matched Filter) -&gt; LPF de decizie -&gt; ATC -&gt; AFC -&gt; SNR gating /
/// integrator -&gt; decizie de bit -&gt; clock recovery + volant + framing (start, 5 biti, stop).
///
/// Nu e thread-safe: configurarea se schimba doar de pe firul care apeleaza ProcessBlock
/// (vezi <see cref="RttyDecoder.Core.RttyReceiver"/>, care trece setarile printr-o coada).
/// Numele interne sunt pastrate ca in Java, ca modificarile sa se poata urmari intre cele doua.
/// </summary>
public sealed class RttyDemodulator
{
    public const double BaudRtty = 45.45;
    public const double ShiftHz = 170.0;

    /// <summary>Cod Baudot de 5 biti cu stop-bit valid.</summary>
    public Action<int>? CodeReceived;

    /// <summary>
    /// La sfarsitul fiecarui caracter (valid sau nu): bitul de stop a fost Mark, codul. Pentru squelch:
    /// pe un semnal RTTY aproape toate caracterele se incadreaza, pe zgomot doar cam jumatate.
    /// </summary>
    /// <remarks>Al treilea parametru: inceputul caracterului, in biti de la pornire (ritmul caracterelor).</remarks>
    public Action<bool, int, double>? FrameCompleted;

    /// <summary>Apelat periodic (~10/s la 48 kHz) cu SNR-ul (dB) si frecventa Mark curenta.</summary>
    public Action<double, double>? StatusUpdated;

    // ---- Configurare ----
    internal bool regimReverseSoftware;
    internal double frecventaTunedBaza = 2125.0;
    internal double sampleRateFiltre = 48000.0;
    internal double rCheb = 0.992;
    internal double pragSensibilitate = 0.05;
    internal int modUrmarireFrecventa;

    // Noise Blanker - suprima pulsurile scurte (QRN) direct in audio, inainte de restul lantului.
    internal bool folosesteNoiseBlanker;
    internal double nbFactorPrag = 5.0;

    // DNR - filtru bandpass biquad centrat intre Mark si Space
    internal bool folosesteDnr;
    internal double latimeDnrHz = 600.0;
    private double dnrB0 = 1.0, dnrB1, dnrB2;
    private double dnrA1, dnrA2;

    // SNR Gating - la energie insuficienta decizia deriva lin spre Mark
    internal bool folosesteSnrGating;
    internal double snrGatingK = 1.5;

    internal bool folosesteMatchedFilter;
    internal bool solicitaResetFiltre;

    /// <summary>
    /// Butterworth ordin 2 pe fiecare ton, inainte de decizia de bit. 1x rata de baud a iesit
    /// optim in teste (Chebyshev la -3 dB: ~67% -&gt; ~0% CER). 0 = dezactivat.
    /// </summary>
    internal double lpfDecizieHz = BaudRtty;
    private readonly Biquad lpfMark = new();
    private readonly Biquad lpfSpace = new();

    /// <summary>
    /// ATC "optimal" W7AY (ca in fldigi): pondereaza decizia cu puterea curenta a fiecarui ton.
    /// Ajuta mult la fading selectiv, costa putin fara fading - de aceea e optional. Doar Chebyshev.
    /// </summary>
    internal bool folosesteAtc;

    /// <summary>
    /// Volant de caracter: la RTTY continuu, dupa cateva caractere valide decodorul prezice unde
    /// incepe urmatorul. Masurat: -6 dB 19.7% -&gt; 8.6% CER; tastarea manuala nu e afectata.
    /// </summary>
    internal bool folosesteVolant = true;
    private const int VolantPragBlocare = 3;
    private const int VolantMaxRatari = 3;
    private const double VolantCorectie = 0.5;
    private const double VolantFereastraBiti = 1.0;
    private const double VolantTolerantaBiti = 0.2;
    private bool fantomaActiva;
    private double fantomaGrila, fantomaPerioada;
    private bool volantBlocat;
    private int volantPotriviri, ratariVolant;
    private bool areStartAnterior;
    private double startCadru, startAnterior, startPrezis, perioadaCaracter;
    private long esantionAbs;

    private double atcMarkEnv, atcSpaceEnv, atcMarkZgomot, atcSpaceZgomot;

    // ---- Coeficienti derivati din frecventa ----
    private double qMarkA, qSpaceA;

    // Matched Filter: corelatie glisanta recursiva O(1) per esantion
    internal double[]? mfBuffer;
    private int mfBufferPos;
    private int mfSamplesPerBit = 1056;
    private double mfMarkRotRe, mfMarkRotIm;
    private double mfSpaceRotRe, mfSpaceRotIm;
    private double mfMarkPowNRe, mfMarkPowNIm;
    private double mfSpacePowNRe, mfSpacePowNIm;
    private double mfMarkAccRe, mfMarkAccIm;
    private double mfSpaceAccRe, mfSpaceAccIm;
    private double noiseFloor = 1e-9;

    // Matched Filter - doua corectii fata de varianta Android, gasite pe inregistrari reale (W6IWI):
    //
    // 1. Fading selectiv. Corelatorul integreaza un bit intreg, deci la o tranzitie |Mark| si |Space|
    //    se schimba liniar pe toata durata bitului; cand un ton e mai slab, trecerea prin zero a
    //    diferentei se muta spre el, cu pana la 1/2 bit - fronturile de start si momentele de
    //    esantionare aluneca, iar caracterele pica la bitul de stop. Fiecare ton e raportat acum la
    //    amplitudinea lui, invatata la momentele de esantionare (fereastra aliniata pe bit, SNR maxim):
    //    trecerea prin zero ramane la mijlocul tranzitiei. Cu MF, startul se confirma dupa 0.35 biti
    //    (semnalul MF e deja integrat pe un bit; 0.7 biti respingea starturile scurtate de fading).
    //    Sintetic, fading 0.2-1 Hz: 24-32% -> 0.2-9% CER; zgomot fara fading: neschimbat.
    //
    // 2. Faza instabila in interiorul unui bit (flutter, propagare cu Doppler): doua jumatati de
    //    fereastra cu faze diferite se anuleaza partial in suma coerenta. In paralel ruleaza un
    //    corelator de 1/2 bit; doua jumatati consecutive adunate necoerent (|S1| + |S2|) nu depind de
    //    faza. Coerenta |S1 + S2| / (|S1| + |S2|) se masoara la fiecare bit; sub 0.93 (medie pe ~200
    //    de biti) se trece pe jumatati, peste 0.97 inapoi. Pe semnal stabil nu comuta (suma coerenta
    //    e cu ~1 dB mai buna in zgomot). Inregistrari W6IWI: 14089 kHz 171 -> 97 erori, 7086 kHz
    //    1 -> 16 randuri perfecte.
    //
    // Limita: la flutter rapid (fading complex de 5-10 Hz) MF ramane slab (~25% CER) - integreaza
    // un bit intreg, iar canalul se schimba mai repede. Acolo Chebyshev (~1%) sau DeepRTTY.
    private const double MfPragStartBiti = 0.35;
    private const double MfDdAlfa = 0.4;
    private const double MfDdPodea = 0.1;
    private const double MfPragNecoerent = 0.93;
    private const double MfPragCoerent = 0.97;
    private const double MfAlfaCoerenta = 0.005;
    private double ddAm, ddAs;
    private readonly CorelatorGlisant jumMark = new(), jumSpace = new();
    private double[] jumIstMark = new double[1], jumIstSpace = new double[1];
    private int jumPoz;
    private double magCohMark, magCohSpace, magNcMark, magNcSpace;
    internal double coerentaMedie = 1.0;
    internal bool mfNecoerent;

    // ---- Stare de decodare ----
    private int sampleRate = 48000;
    private double samplesPerBit;
    private double mx1, mx2, sx1, sx2;
    private double dcBlockerInVech, dcBlockerOutVech;
    private double integratVech;
    private const double AlfaIntegrator = 0.08;
    private double nivelZgomotMediu;
    private bool inFrame;
    private double fazaCeasAcumulator;
    private int bitCounter;
    private int currentByte;

    // Debounce la startul de cadru: "space" trebuie sa persiste 0.7 biti.
    private int contorConfirmareStart;
    private int pragConfirmareStart, pragConfirmareStartMf;

    // Esantionare la mijlocul bitului (testat pe hardware: marja maxima fata de jitter).
    private const double PctEsantionare = 0.5;

    private int contorEroriFramingConsecutive;
    private const int PragEroriFramingResync = 5;

    // Noise Blanker
    private double nbNivelMediu;
    private double nbUltimulEsantionValid;
    private const double NbEmaAlpha = 0.0008;
    private int nbContorBlankConsecutiv;
    private const int NbMaxBlankConsecutiv = 150; // ~3 ms la 48 kHz

    private double dnrX1, dnrX2, dnrY1, dnrY2;

    // AFC / PLL - discriminator de frecventa pe ambele tonuri (vezi ActualizeazaAfc).
    // Varianta Android corela audio-ul cu un fazor e^{+j w_mark t} pe ferestre de 2 biti: semnul
    // erorii iesea INVERSAT (reactie pozitiva - acordul fugea de semnal: +9..+13 Hz pe inregistrarile
    // W6IWI), iar fereastra amesteca Mark cu Space. Pe semnal stabil conditia
    // magMark > 2 x nivelZgomotMediu bloca de altfel complet bucla.
    private readonly AfcTon afcMark = new();
    private readonly AfcTon afcSpace = new();
    private double afcSumRe, afcSumIm, afcSumAbs;
    private int afcContor, afcDominant, afcDominantDecodor;
    private bool afcFereastraAnterioaraValida;
    private int afcFereastra;
    private int afcIntarziere;
    private double freqErrorFiltrat;
    /// <summary>Banda trece-jos a fiecarui ton in banda de baza (Hz): taie celalalt ton (la 170 Hz).</summary>
    private const double AfcLpfHz = 50.0;
    /// <summary>Intarzierea pe care se masoara rotatia fazei: 4 ms (domeniu neambiguu +-125 Hz).</summary>
    private const double AfcIntarziereSec = 0.004;
    /// <summary>Un ton "domina" daca anvelopa lui e de cel putin atatea ori mai mare decat a celuilalt.</summary>
    private const double AfcDominanta = 2.0;
    /// <summary>Coerenta minima |suma| / suma |.| a masuratorilor dintr-o fereastra (zgomotul da ~0).</summary>
    private const double AfcCoerentaMin = 0.5;
    /// <summary>
    /// Fractia minima din fereastra in care un ton domina clar. Un semnal RTTY are mereu unul din
    /// tonuri activ (in afara tranzitiilor); in zgomot asta se intampla doar la intamplare.
    /// </summary>
    private const double AfcFractieDominantaMin = 0.6;
    private const double AfcEmaAlpha = 0.25;
    private const double AfcMaxCorectieHz = 5.0;
    private const double AfcPragOutlierHz = 60.0;

    private const double SnrGatingMarkBiasAlpha = 0.001;
    private const double SnrGatingMarkBiasMargine = 0.1;

    public double MarkFrequencyHz => frecventaTunedBaza;

    internal double SpaceOffsetHz => regimReverseSoftware ? -ShiftHz : ShiftHz;

    /// <summary>Math.round din Java (rotunjire in sus la .5), pentru rezultate identice cu Android.</summary>
    private static long JRound(double x) => (long)Math.Floor(x + 0.5);

    // ============================================================
    // Coeficienti
    // ============================================================

    internal void InitMatchedFilterTemplates(int sr, double baud)
    {
        mfSamplesPerBit = (int)JRound(sr / baud);
        mfBuffer = new double[mfSamplesPerBit];
        int jumatate = Math.Max(1, (int)JRound(sr / baud / 2));
        jumMark.Configureaza(jumatate);
        jumSpace.Configureaza(jumatate);
        jumIstMark = new double[jumatate];
        jumIstSpace = new double[jumatate];
        jumPoz = 0;
        mfBufferPos = 0;
        mfMarkAccRe = mfMarkAccIm = 0;
        mfSpaceAccRe = mfSpaceAccIm = 0;
        SetareaConstantelorMf(frecventaTunedBaza, frecventaTunedBaza + SpaceOffsetHz, sr);
    }

    internal void RecalculeazaSabloaneMf()
    {
        if (mfBuffer != null)
        {
            SetareaConstantelorMf(frecventaTunedBaza, frecventaTunedBaza + SpaceOffsetHz, sampleRateFiltre);
        }
    }

    /// <summary>
    /// Suma exacta a corelatorului glisant din buffer: S = suma b_i e^{j w i}, b_0 = esantionul cel
    /// mai vechi - forma inchisa a recursiei S(n+1) = e^{-jw} (S(n) - x_iesit + x_nou e^{jwN}).
    /// </summary>
    private static (double Re, double Im) SumaExacta(double[] buf, int pozCelMaiVechi, double w)
    {
        double re = 0, im = 0, pasRe = Math.Cos(w), pasIm = Math.Sin(w), fRe = 1, fIm = 0;
        int n = buf.Length, idx = pozCelMaiVechi;
        for (int i = 0; i < n; i++)
        {
            double x = buf[idx];
            re += x * fRe;
            im += x * fIm;
            double t = fRe * pasRe - fIm * pasIm;
            fIm = fRe * pasIm + fIm * pasRe;
            fRe = t;
            if (++idx == n) idx = 0;
        }
        return (re, im);
    }

    /// <summary>Depanare/teste: abaterea relativa a sumelor recursive MF fata de cele exacte.</summary>
    internal double EroareCorelatorMf()
    {
        if (mfBuffer == null) return 0;
        double jum = Math.Max(jumMark.Eroare(), jumSpace.Eroare());
        var m = SumaExacta(mfBuffer, mfBufferPos, -Math.Atan2(mfMarkRotIm, mfMarkRotRe));
        var sp = SumaExacta(mfBuffer, mfBufferPos, -Math.Atan2(mfSpaceRotIm, mfSpaceRotRe));
        double dif = Math.Sqrt(Math.Pow(m.Re - mfMarkAccRe, 2) + Math.Pow(m.Im - mfMarkAccIm, 2))
                     + Math.Sqrt(Math.Pow(sp.Re - mfSpaceAccRe, 2) + Math.Pow(sp.Im - mfSpaceAccIm, 2));
        double niv = Math.Sqrt(m.Re * m.Re + m.Im * m.Im) + Math.Sqrt(sp.Re * sp.Re + sp.Im * sp.Im);
        return Math.Max(dif / Math.Max(niv, 1e-12), jum);
    }

    private void SetareaConstantelorMf(double fMark, double fSpace, double sr)
    {
        double wMark = 2.0 * Math.PI * fMark / sr;
        double wSpace = 2.0 * Math.PI * fSpace / sr;

        mfMarkRotRe = Math.Cos(-wMark);
        mfMarkRotIm = Math.Sin(-wMark);
        mfSpaceRotRe = Math.Cos(-wSpace);
        mfSpaceRotIm = Math.Sin(-wSpace);

        double wMarkN = wMark * mfSamplesPerBit;
        double wSpaceN = wSpace * mfSamplesPerBit;
        jumMark.SetFrecventa(wMark);
        jumSpace.SetFrecventa(wSpace);
        mfMarkPowNRe = Math.Cos(wMarkN);
        mfMarkPowNIm = Math.Sin(wMarkN);
        mfSpacePowNRe = Math.Cos(wSpaceN);
        mfSpacePowNIm = Math.Sin(wSpaceN);

        // CORECTIE (gasita de Eugen, YO9VA): recursia S(n+1) = e^{-jw} (S(n) - x_iesit + x_nou e^{jwN})
        // e corecta doar cat w ramane acelasi. La o schimbare de frecventa esantioanele din fereastra au
        // intrat cu vechea referinta de faza si ar fi scazute cu cea noua - eroarea ramanea in suma
        // pentru totdeauna (recursia nu uita nimic). Cu AFC-ul care corecteaza la ~2 biti, se aduna:
        // 7-28% din nivelul semnalului. Sumele se recalculeaza exact din buffer (~2 x 1056 inmultiri
        // la ~2 biti, neglijabil).
        if (mfBuffer != null)
        {
            (mfMarkAccRe, mfMarkAccIm) = SumaExacta(mfBuffer, mfBufferPos, wMark);
            (mfSpaceAccRe, mfSpaceAccIm) = SumaExacta(mfBuffer, mfBufferPos, wSpace);
        }
    }

    /// <summary>Coeficientii bandpass-ului DNR (RBJ cookbook).</summary>
    internal void CalculeazaCoeficientiDnr(double sr)
    {
        double f0 = frecventaTunedBaza + SpaceOffsetHz / 2.0;
        double q = f0 / Math.Max(latimeDnrHz, 1.0);
        double w0 = 2.0 * Math.PI * f0 / sr;
        double alpha = Math.Sin(w0) / (2.0 * q);
        double cosW0 = Math.Cos(w0);

        double b0 = alpha, b1 = 0.0, b2 = -alpha;
        double a0 = 1.0 + alpha, a1 = -2.0 * cosW0, a2 = 1.0 - alpha;

        dnrB0 = b0 / a0; dnrB1 = b1 / a0; dnrB2 = b2 / a0;
        dnrA1 = a1 / a0; dnrA2 = a2 / a0;
    }

    /// <summary>Recalculeaza tot ce depinde de Mark / reverse si cere reset de filtre.</summary>
    internal void SincronizeazaCoeficienti()
    {
        RecalculeazaCoeficientiFrecventa();
        RecalculeazaSabloaneMf();
        solicitaResetFiltre = true;
    }

    private void RecalculeazaCoeficientiFrecventa()
    {
        afcMark.SetFrecventa(frecventaTunedBaza, sampleRateFiltre);
        afcSpace.SetFrecventa(frecventaTunedBaza + SpaceOffsetHz, sampleRateFiltre);

        CalculeazaCoeficientiDnr(sampleRateFiltre);

        qMarkA = 2.0 * Math.Cos(2.0 * Math.PI * frecventaTunedBaza / sampleRateFiltre);
        double fSpace = frecventaTunedBaza + SpaceOffsetHz;
        qSpaceA = 2.0 * Math.Cos(2.0 * Math.PI * fSpace / sampleRateFiltre);
    }

    // ============================================================
    // Decodare
    // ============================================================

    /// <summary>Initializeaza starea de decodare.</summary>
    public void Start(int sr)
    {
        sampleRate = sr;
        samplesPerBit = (double)sr / BaudRtty;

        mx1 = 0; mx2 = 0; sx1 = 0; sx2 = 0;
        dcBlockerInVech = 0; dcBlockerOutVech = 0;
        integratVech = 0;
        nivelZgomotMediu = 0.05;
        inFrame = false;
        fazaCeasAcumulator = 0;
        bitCounter = 0;
        currentByte = 0;
        contorConfirmareStart = 0;
        pragConfirmareStart = (int)JRound(samplesPerBit * 0.7);
        pragConfirmareStartMf = (int)JRound(samplesPerBit * MfPragStartBiti);
        ddAm = ddAs = 0;
        coerentaMedie = 1.0;
        mfNecoerent = false;
        contorEroriFramingConsecutive = 0;
        nbNivelMediu = 0.05;
        nbUltimulEsantionValid = 0;
        nbContorBlankConsecutiv = 0;
        dnrX1 = 0; dnrX2 = 0; dnrY1 = 0; dnrY2 = 0;
        afcIntarziere = Math.Max(1, (int)JRound(AfcIntarziereSec * sr));
        afcFereastra = Math.Max(1, (int)JRound(samplesPerBit)) * 2;
        afcMark.Configureaza(AfcLpfHz, sr, afcIntarziere);
        afcSpace.Configureaza(AfcLpfHz, sr, afcIntarziere);
        ReseteazaAfc();

        noiseFloor = 1e-9;
        if (lpfDecizieHz > 0)
        {
            lpfMark.ConfigureazaTreceJos(lpfDecizieHz, sr);
            lpfSpace.ConfigureazaTreceJos(lpfDecizieHz, sr);
        }
        ReseteazaAtc();
        DeblocheazaVolant();
        esantionAbs = 0;

        if (folosesteMatchedFilter)
        {
            InitMatchedFilterTemplates(sr, BaudRtty);
        }

        sampleRateFiltre = sr;
        SincronizeazaCoeficienti();
    }

    /// <summary>Proceseaza primele <paramref name="read"/> esantioane PCM 16-bit din buffer.</summary>
    public void ProcessBlock(short[] audioBuffer, int read)
    {
        if (solicitaResetFiltre)
        {
            mx1 = 0; mx2 = 0; sx1 = 0; sx2 = 0;
            integratVech = 0;
            inFrame = false;
            fazaCeasAcumulator = 0;
            bitCounter = 0;
            currentByte = 0;
            contorConfirmareStart = 0;
            contorEroriFramingConsecutive = 0;
            // nbNivelMediu nu se reseteaza: nivelul invatat de NB nu depinde de filtre.
            nbContorBlankConsecutiv = 0;
            noiseFloor = 1e-9;
            dnrX1 = 0; dnrX2 = 0; dnrY1 = 0; dnrY2 = 0;
            lpfMark.Reseteaza();
            lpfSpace.Reseteaza();
            ReseteazaAfc();
            ReseteazaAtc();
            DeblocheazaVolant();
            if (folosesteMatchedFilter && mfBuffer != null)
            {
                Array.Clear(mfBuffer);
                mfBufferPos = 0;
                mfMarkAccRe = mfMarkAccIm = 0;
                mfSpaceAccRe = mfSpaceAccIm = 0;
                jumMark.Reseteaza();
                jumSpace.Reseteaza();
                Array.Clear(jumIstMark);
                Array.Clear(jumIstSpace);
                jumPoz = 0;
            }
            // amplitudinile invatate tin de semnal (acord, statie); coerenta tine de emitator - ramane
            ddAm = ddAs = 0;
            solicitaResetFiltre = false;
        }

        for (int i = 0; i < read; i++)
        {
            double rawSample = audioBuffer[i] / 32768.0;
            double sample = rawSample - dcBlockerInVech + 0.995 * dcBlockerOutVech;
            dcBlockerInVech = rawSample;
            dcBlockerOutVech = sample;

            // Noise Blanker: nivelul mediu se invata mereu, ca la activare pragul sa fie realist.
            double sampleAbsNb = Math.Abs(sample);
            if (folosesteNoiseBlanker)
            {
                double pragNb = nbNivelMediu * nbFactorPrag;
                if (sampleAbsNb > pragNb && nbContorBlankConsecutiv < NbMaxBlankConsecutiv)
                {
                    sample = nbUltimulEsantionValid; // "hold" pe ultimul esantion valid
                    nbContorBlankConsecutiv++;
                }
                else
                {
                    nbUltimulEsantionValid = sample;
                    nbNivelMediu = NbEmaAlpha * sampleAbsNb + (1.0 - NbEmaAlpha) * nbNivelMediu;
                    nbContorBlankConsecutiv = 0;
                }
            }
            else
            {
                nbNivelMediu = NbEmaAlpha * sampleAbsNb + (1.0 - NbEmaAlpha) * nbNivelMediu;
                nbUltimulEsantionValid = sample;
                nbContorBlankConsecutiv = 0;
            }

            if (folosesteDnr)
            {
                double dnrOut = dnrB0 * sample + dnrB1 * dnrX1 + dnrB2 * dnrX2
                                - dnrA1 * dnrY1 - dnrA2 * dnrY2;
                dnrX2 = dnrX1; dnrX1 = sample;
                dnrY2 = dnrY1; dnrY1 = dnrOut;
                sample = dnrOut;
            }

            double magMark, magSpace;
            double diferentaBruta;

            if (folosesteMatchedFilter)
            {
                // S(n+1) = psi * ( S(n) - x_iesit + x_nou * PsiN )
                double[] mf = mfBuffer!;
                double xIesit = mf[mfBufferPos];
                mf[mfBufferPos] = sample;
                mfBufferPos = (mfBufferPos + 1) % mfSamplesPerBit;

                double markInRe = mfMarkAccRe - xIesit + sample * mfMarkPowNRe;
                double markInIm = mfMarkAccIm + sample * mfMarkPowNIm;
                mfMarkAccRe = mfMarkRotRe * markInRe - mfMarkRotIm * markInIm;
                mfMarkAccIm = mfMarkRotRe * markInIm + mfMarkRotIm * markInRe;

                double spaceInRe = mfSpaceAccRe - xIesit + sample * mfSpacePowNRe;
                double spaceInIm = mfSpaceAccIm + sample * mfSpacePowNIm;
                mfSpaceAccRe = mfSpaceRotRe * spaceInRe - mfSpaceRotIm * spaceInIm;
                mfSpaceAccIm = mfSpaceRotRe * spaceInIm + mfSpaceRotIm * spaceInRe;

                double scalareMf = 2.0 / mfSamplesPerBit;
                magMark = Math.Sqrt(mfMarkAccRe * mfMarkAccRe + mfMarkAccIm * mfMarkAccIm) * scalareMf;
                magSpace = Math.Sqrt(mfSpaceAccRe * mfSpaceAccRe + mfSpaceAccIm * mfSpaceAccIm) * scalareMf;

                // jumatatile de bit: corelatorul de 1/2 bit acum si cel de acum 1/2 bit
                jumMark.Proceseaza(sample);
                jumSpace.Proceseaza(sample);
                double jm = jumMark.Magnitudine * scalareMf, js = jumSpace.Magnitudine * scalareMf;
                magCohMark = magMark;
                magCohSpace = magSpace;
                magNcMark = jm + jumIstMark[jumPoz];
                magNcSpace = js + jumIstSpace[jumPoz];
                jumIstMark[jumPoz] = jm;
                jumIstSpace[jumPoz] = js;
                if (++jumPoz == jumIstMark.Length) jumPoz = 0;
                if (mfNecoerent)
                {
                    magMark = magNcMark;
                    magSpace = magNcSpace;
                }
            }
            else
            {
                double m0 = sample - rCheb * rCheb * mx2 + qMarkA * rCheb * mx1;
                mx2 = mx1; mx1 = m0;
                magMark = Math.Abs(m0);

                double s0 = sample - rCheb * rCheb * sx2 + qSpaceA * rCheb * sx1;
                sx2 = sx1; sx1 = s0;
                magSpace = Math.Abs(s0);
            }

            // Filtrul de decizie pe fiecare ton, ca ATC sa primeasca anvelopele netezite.
            double markFiltrat = lpfDecizieHz > 0 ? lpfMark.Proceseaza(magMark) : magMark;
            double spaceFiltrat = lpfDecizieHz > 0 ? lpfSpace.Proceseaza(magSpace) : magSpace;
            diferentaBruta = folosesteMatchedFilter
                ? DecizieMf(markFiltrat, spaceFiltrat)
                : folosesteAtc
                    ? DecizieAtc(markFiltrat, spaceFiltrat)
                    : markFiltrat - spaceFiltrat;

            if (modUrmarireFrecventa > 0) ActualizeazaAfc(sample, markFiltrat, spaceFiltrat);

            double magTotala = magMark + magSpace;

            if (magTotala < noiseFloor) noiseFloor = magTotala;
            else noiseFloor = 0.999 * noiseFloor + 0.001 * magTotala;

            nivelZgomotMediu = 0.0005 * magTotala + 0.9995 * nivelZgomotMediu;

            double integratCurent;
            if (folosesteSnrGating && magTotala < noiseFloor * snrGatingK)
            {
                double tintaMark = Math.Max(nivelZgomotMediu, 1e-9) * SnrGatingMarkBiasMargine;
                integratCurent = integratVech + (tintaMark - integratVech) * SnrGatingMarkBiasAlpha;
            }
            else
            {
                integratCurent = AlfaIntegrator * diferentaBruta + (1.0 - AlfaIntegrator) * integratVech;
            }
            integratVech = integratCurent;

            if (i % 4800 == 0 && StatusUpdated != null)
            {
                double signalPeak = Math.Max(magMark, magSpace);
                double snrDb = 20 * Math.Log10(signalPeak / Math.Max(noiseFloor, 1e-9));
                StatusUpdated(Math.Max(0, snrDb), frecventaTunedBaza);
            }

            bool isMark = Math.Abs(integratCurent) < nivelZgomotMediu * pragSensibilitate || integratCurent > 0;
            int bitCurent = isMark ? 1 : 0;

            esantionAbs++;

            // Clock Recovery
            if (!inFrame)
            {
                if (bitCurent == 0) contorConfirmareStart++;
                else contorConfirmareStart = 0;

                if (folosesteVolant && volantBlocat)
                {
                    long mijlocStart = JRound(startPrezis + 0.5 * samplesPerBit);
                    long sfarsitStart = JRound(startPrezis + VolantFereastraBiti * samplesPerBit);
                    if (esantionAbs >= mijlocStart && (bitCurent == 0 || esantionAbs >= sfarsitStart))
                    {
                        if (bitCurent == 0 && (esantionAbs == mijlocStart
                                               || contorConfirmareStart >= samplesPerBit * 0.3))
                        {
                            // CORECTIE (Eugen, YO9VA): frontul real (esantionAbs - contorConfirmareStart) e
                            // cunoscut si cand space-ul a inceput chiar inainte de mijlocStart (contor < 0.3
                            // biti) - inainte, pe acea ramura cadrul pornea pe startul prezis, necorectat. Acolo
                            // frontul e mereu intarziat (mediana +0.3 biti), deci biti cititi prea devreme si
                            // grila decalata si pentru caracterul urmator. Apare in 1-12% din cadre la
                            // Chebyshev si 8-20% la MF (trecerea MF prin zero e o rampa pe un bit, mai
                            // imprastiata). Acum se corecteaza mereu, cu aceeasi regula (castig 0.5, plafon
                            // +-0.25 biti). W6IWI 14089 kHz: Chebyshev 144 -> 38 erori, MF 83 -> 35.
                            double abatere = (esantionAbs - contorConfirmareStart) - startPrezis;
                            double limita = 0.25 * samplesPerBit;
                            double start = startPrezis + VolantCorectie * Math.Max(-limita, Math.Min(limita, abatere));
                            IncepeCadru(start);
                        }
                        else if (esantionAbs >= sfarsitStart)
                        {
                            // Mark pe tot inceputul startului prezis: pauza de tastare. Inapoi la
                            // cautarea libera; grila veche ramane "fantoma" ca sa se reblocheze repede.
                            double grila = startPrezis, perioada = perioadaCaracter;
                            DeblocheazaVolant();
                            fantomaGrila = grila;
                            fantomaPerioada = perioada;
                            fantomaActiva = true;
                        }
                    }
                }
                else if (bitCurent == 0 && contorConfirmareStart >= (folosesteMatchedFilter ? pragConfirmareStartMf : pragConfirmareStart))
                {
                    // Faza porneste de la inceputul real al starii "space", nu de la confirmare.
                    IncepeCadru(esantionAbs - contorConfirmareStart);
                }
            }
            else
            {
                fazaCeasAcumulator += 1.0;
                double punctEsantionareTinta = bitCounter == 0
                    ? samplesPerBit * (1.0 + PctEsantionare)
                    : samplesPerBit;

                if (fazaCeasAcumulator >= punctEsantionareTinta)
                {
                    fazaCeasAcumulator -= punctEsantionareTinta;
                    if (folosesteMatchedFilter) InvataMf(bitCurent, markFiltrat, spaceFiltrat);
                    if (bitCounter < 5)
                    {
                        if (bitCurent == 1) currentByte |= 1 << bitCounter;
                        bitCounter++;
                    }
                    else
                    {
                        bool volantEraBlocat = folosesteVolant && volantBlocat;
                        if (folosesteVolant) ActualizeazaVolant(bitCurent == 1);
                        FrameCompleted?.Invoke(bitCurent == 1, currentByte & 0x1F, startCadru / samplesPerBit);
                        if (bitCurent == 1)
                        {
                            CodeReceived?.Invoke(currentByte & 0x1F);
                            contorEroriFramingConsecutive = 0;
                        }
                        else if (!volantEraBlocat)
                        {
                            contorEroriFramingConsecutive++;
                            if (contorEroriFramingConsecutive >= PragEroriFramingResync)
                            {
                                solicitaResetFiltre = true;
                                contorEroriFramingConsecutive = 0;
                            }
                        }
                        inFrame = false;
                    }
                }
            }
        }
    }

    private void ReseteazaAfc()
    {
        afcMark.Reseteaza();
        afcSpace.Reseteaza();
        afcSumRe = afcSumIm = afcSumAbs = 0;
        afcContor = afcDominant = afcDominantDecodor = 0;
        afcFereastraAnterioaraValida = false;
        freqErrorFiltrat = 0;
    }

    /// <summary>
    /// AFC / PLL. Fiecare ton e coborat in banda de baza la frecventa lui nominala si filtrat
    /// (~50 Hz); rotatia fazei pe 4 ms, z[n]·conj(z[n-D]), da direct abaterea tonului fata de
    /// acord, cu semnul corect. Se masoara doar tonul care domina clar in acel moment (nu la
    /// tranzitii), iar produsele se aduna vectorial pe ~2 biti: un ton curat le aliniaza
    /// (coerenta ~1), zgomotul le imprastie (~0) - sub prag corectia nu se aplica, deci pauzele
    /// dintre transmisii nu muta acordul. Castigurile (AFC 5%, PLL 25% pe fereastra de ~44 ms)
    /// si limitele raman cele din aplicatia Android.
    /// </summary>
    private void ActualizeazaAfc(double sample, double markFiltrat, double spaceFiltrat)
    {
        afcMark.Proceseaza(sample);
        afcSpace.Proceseaza(sample);
        // Dominanta se judeca pe filtrele proprii AFC (raspund in cateva ms), nu pe anvelopele
        // decodorului: cele MF intarzie ~1/2 bit, iar dupa fiecare tranzitie AFC-ul masura inca pe
        // tonul disparut (coada filtrului + scurgerea celuilalt ton) - cu Mark mai slab decat Space,
        // acordul aluneca in sus (MF + PLL: +9..+10 Hz pe inregistrarile W6IWI).
        double pm = afcMark.Putere, ps = afcSpace.Putere;
        double d2 = AfcDominanta * AfcDominanta;
        if (pm > d2 * ps)
        {
            afcMark.Acumuleaza(ref afcSumRe, ref afcSumIm, ref afcSumAbs);
            afcDominant++;
        }
        else if (ps > d2 * pm)
        {
            afcSpace.Acumuleaza(ref afcSumRe, ref afcSumIm, ref afcSumAbs);
            afcDominant++;
        }
        // CORECTIE (Eugen, YO9VA): filtrele AFC (50 Hz, cativa ms) sunt prea rapide ca sa deosebeasca
        // singure semnalul de zgomot - pe zgomot un ton pare des "dominant", iar AFC-ul muta acordul
        // (3 min de zgomot, Chebyshev + PLL: 2125 -> 2072 Hz; QRN W6IWI: 2048 -> 2036 Hz). Ca fereastra
        // sa conteze trebuie sa fie semnal si dupa anvelopele decodorului (integrate pe bit): cu ele pe
        // zgomot acordul nu se misca, iar masuratoarea ramane pe filtrele rapide.
        if (markFiltrat > AfcDominanta * spaceFiltrat || spaceFiltrat > AfcDominanta * markFiltrat)
            afcDominantDecodor++;

        if (++afcContor < afcFereastra) return;
        afcContor = 0;
        bool semnal = afcDominant >= AfcFractieDominantaMin * afcFereastra
                      && afcDominantDecodor >= AfcFractieDominantaMin * afcFereastra;
        afcDominant = afcDominantDecodor = 0;
        double sumAbs = afcSumAbs;
        double coerenta = sumAbs > 0 ? Math.Sqrt(afcSumRe * afcSumRe + afcSumIm * afcSumIm) / sumAbs : 0;
        double eroareHz = Math.Atan2(afcSumIm, afcSumRe) * sampleRate / (2 * Math.PI * afcIntarziere);
        afcSumRe = afcSumIm = afcSumAbs = 0;
        afcMark.Renormalizeaza();
        afcSpace.Renormalizeaza();
        bool valida = semnal && sumAbs > 0 && coerenta >= AfcCoerentaMin && Math.Abs(eroareHz) <= AfcPragOutlierHz;
        // CORECTIE (Eugen, YO9VA): corectia se aplica doar daca si fereastra anterioara a fost valida.
        // Pe zgomot trec izolat ~0.6% din ferestre (niciodata doua la rand in 2 x 3 min), destul ca
        // MF + PLL sa derive cu ~8 Hz in 3 minute; pe semnal ferestrele valide vin in serii (W6IWI
        // 14086 kHz: 98.7% -> 98.0% aplicate).
        bool aplica = valida && afcFereastraAnterioaraValida;
        afcFereastraAnterioaraValida = valida;
        if (!aplica) return;

        freqErrorFiltrat = freqErrorFiltrat * (1.0 - AfcEmaAlpha) + eroareHz * AfcEmaAlpha;
        double limitat = Math.Max(-AfcMaxCorectieHz, Math.Min(AfcMaxCorectieHz, freqErrorFiltrat));
        double castig = modUrmarireFrecventa == 2 ? 0.25 : 0.05;
        frecventaTunedBaza += limitat * castig;
        // Eroarea masurata e relativa la acordul vechi: dupa corectie scade cu cat s-a corectat.
        freqErrorFiltrat -= limitat * castig;

        sampleRateFiltre = sampleRate;
        RecalculeazaCoeficientiFrecventa();
        if (folosesteMatchedFilter) RecalculeazaSabloaneMf();
    }

    /// <summary>Un ton pentru AFC: mixer complex, trece-jos de ordin 4 pe I/Q, linie de intarziere.</summary>
    private sealed class AfcTon
    {
        private readonly Biquad i1 = new(), i2 = new(), q1 = new(), q2 = new();
        private double osRe = 1, osIm, pasRe = 1, pasIm;
        private double[] intRe = new double[1], intIm = new double[1];
        private int poz;
        private double zRe, zIm, pRe, pIm;

        /// <summary>|z|^2 - puterea tonului in banda de baza acum.</summary>
        public double Putere => zRe * zRe + zIm * zIm;

        public void Configureaza(double lpfHz, double fs, int intarziere)
        {
            i1.ConfigureazaTreceJos(lpfHz, fs); i2.ConfigureazaTreceJos(lpfHz, fs);
            q1.ConfigureazaTreceJos(lpfHz, fs); q2.ConfigureazaTreceJos(lpfHz, fs);
            intRe = new double[intarziere];
            intIm = new double[intarziere];
            Reseteaza();
        }

        public void SetFrecventa(double hz, double fs)
        {
            double w = 2 * Math.PI * hz / fs;
            pasRe = Math.Cos(w);
            pasIm = -Math.Sin(w); // e^{-jw}: tonul la frecventa nominala ajunge la 0 Hz
        }

        public void Reseteaza()
        {
            i1.Reseteaza(); i2.Reseteaza(); q1.Reseteaza(); q2.Reseteaza();
            Array.Clear(intRe);
            Array.Clear(intIm);
            poz = 0;
            zRe = zIm = pRe = pIm = 0;
        }

        /// <summary>Un esantion: z[n] in banda de baza si produsul z[n]·conj(z[n-D]).</summary>
        public void Proceseaza(double x)
        {
            zRe = i2.Proceseaza(i1.Proceseaza(x * osRe));
            zIm = q2.Proceseaza(q1.Proceseaza(x * osIm));
            double r = osRe * pasRe - osIm * pasIm;
            osIm = osRe * pasIm + osIm * pasRe;
            osRe = r;

            double vRe = intRe[poz], vIm = intIm[poz];
            pRe = zRe * vRe + zIm * vIm;
            pIm = zIm * vRe - zRe * vIm;
            intRe[poz] = zRe;
            intIm[poz] = zIm;
            if (++poz == intRe.Length) poz = 0;
        }

        /// <summary>Adauga produsul curent la suma ferestrei.</summary>
        public void Acumuleaza(ref double sumRe, ref double sumIm, ref double sumAbs)
        {
            sumRe += pRe;
            sumIm += pIm;
            sumAbs += Math.Sqrt(pRe * pRe + pIm * pIm);
        }

        public void Renormalizeaza()
        {
            double n = Math.Sqrt(osRe * osRe + osIm * osIm);
            osRe /= n;
            osIm /= n;
        }
    }

    /// <summary>MF: fiecare ton raportat la amplitudinea lui invatata (vezi comentariul de la campuri).</summary>
    private double DecizieMf(double mark, double space)
    {
        if (ddAm <= 0 || ddAs <= 0) return mark - space; // pana se invata ambele tonuri
        double am = Math.Max(ddAm, MfDdPodea * ddAs);
        double asp = Math.Max(ddAs, MfDdPodea * ddAm);
        // inapoi pe scara unei amplitudini, ca pragul de Sensibilitate sa ramana comparabil
        return (mark / am - space / asp) * 0.5 * (am + asp);
    }

    /// <summary>La fiecare bit esantionat: amplitudinea tonului decis si coerenta pe jumatati.</summary>
    private void InvataMf(int bit, double markFiltrat, double spaceFiltrat)
    {
        if (bit == 1) ddAm = ddAm <= 0 ? markFiltrat : ddAm + MfDdAlfa * (markFiltrat - ddAm);
        else ddAs = ddAs <= 0 ? spaceFiltrat : ddAs + MfDdAlfa * (spaceFiltrat - ddAs);

        double coh = bit == 1 ? magCohMark : magCohSpace;
        double nc = bit == 1 ? magNcMark : magNcSpace;
        if (nc <= 0) return;
        coerentaMedie += MfAlfaCoerenta * (Math.Min(1.0, coh / nc) - coerentaMedie);
        mfNecoerent = mfNecoerent ? coerentaMedie < MfPragCoerent : coerentaMedie < MfPragNecoerent;
    }

    /// <summary>Corelator glisant recursiv pe o fereastra de lungime data (aceeasi formula ca MF).</summary>
    private sealed class CorelatorGlisant
    {
        private double[] buf = new double[1];
        private int poz;
        private double w, rotRe = 1, rotIm, powRe = 1, powIm, accRe, accIm;

        public void Configureaza(int lungime)
        {
            buf = new double[lungime];
            Reseteaza();
            SetFrecventa(w);
        }

        public void SetFrecventa(double omega)
        {
            w = omega;
            rotRe = Math.Cos(-w);
            rotIm = Math.Sin(-w);
            powRe = Math.Cos(w * buf.Length);
            powIm = Math.Sin(w * buf.Length);
            (accRe, accIm) = SumaExacta(buf, poz, w); // vezi SetareaConstantelorMf
        }

        /// <summary>Depanare/teste: abaterea relativa a sumei recursive fata de cea exacta.</summary>
        public double Eroare()
        {
            var (re, im) = SumaExacta(buf, poz, w);
            return Math.Sqrt(Math.Pow(re - accRe, 2) + Math.Pow(im - accIm, 2)) / Math.Max(Math.Sqrt(re * re + im * im), 1e-12);
        }

        public void Reseteaza()
        {
            Array.Clear(buf);
            poz = 0;
            accRe = accIm = 0;
        }

        public void Proceseaza(double x)
        {
            double iesit = buf[poz];
            buf[poz] = x;
            if (++poz == buf.Length) poz = 0;
            double inRe = accRe - iesit + x * powRe;
            double inIm = accIm + x * powIm;
            accRe = rotRe * inRe - rotIm * inIm;
            accIm = rotRe * inIm + rotIm * inRe;
        }

        public double Magnitudine => Math.Sqrt(accRe * accRe + accIm * accIm);
    }

    private void IncepeCadru(double start)
    {
        inFrame = true;
        startCadru = start;
        fazaCeasAcumulator = esantionAbs - start;
        bitCounter = 0;
        currentByte = 0;
        contorConfirmareStart = 0;
    }

    /// <summary>
    /// La fiecare bit de stop. Caractere valide consecutive la 7-8 biti unul de altul blocheaza
    /// volantul si ii invata perioada; cu volantul blocat, rateurile doar se numara.
    /// </summary>
    private void ActualizeazaVolant(bool stopValid)
    {
        if (fantomaActiva && !volantBlocat)
        {
            double k = JRound((startCadru - fantomaGrila) / fantomaPerioada);
            if (k < 0 || k > 4)
            {
                fantomaActiva = false;
            }
            else if (stopValid && Math.Abs(startCadru - (fantomaGrila + k * fantomaPerioada)) <= VolantTolerantaBiti * samplesPerBit)
            {
                fantomaActiva = false;
                volantBlocat = true;
                volantPotriviri = VolantPragBlocare;
                perioadaCaracter = fantomaPerioada;
                startAnterior = startCadru;
                areStartAnterior = true;
                ratariVolant = 0;
                startPrezis = startCadru + perioadaCaracter;
                return;
            }
        }
        if (stopValid)
        {
            if (areStartAnterior)
            {
                double perioada = startCadru - startAnterior;
                double biti = perioada / samplesPerBit;
                double celMaiAproape = JRound(biti * 2) / 2.0;
                bool plauzibila = celMaiAproape >= 7.0 && celMaiAproape <= 8.0
                                  && Math.Abs(biti - celMaiAproape) <= VolantTolerantaBiti;
                bool consecventa = volantPotriviri == 0
                                   || Math.Abs(perioada - perioadaCaracter) <= VolantTolerantaBiti * samplesPerBit;
                if (plauzibila && consecventa)
                {
                    perioadaCaracter = volantPotriviri == 0 ? perioada : 0.8 * perioadaCaracter + 0.2 * perioada;
                    volantPotriviri++;
                }
                else if (!volantBlocat)
                {
                    volantPotriviri = plauzibila ? 1 : 0;
                    if (plauzibila) perioadaCaracter = perioada;
                }
            }
            startAnterior = startCadru;
            areStartAnterior = true;
            ratariVolant = 0;
            if (volantPotriviri >= VolantPragBlocare) volantBlocat = true;
        }
        else if (volantBlocat)
        {
            ratariVolant++;
            if (ratariVolant >= VolantMaxRatari)
            {
                DeblocheazaVolant();
                return;
            }
        }
        else
        {
            volantPotriviri = 0;
            areStartAnterior = false;
        }
        if (volantBlocat) startPrezis = startCadru + perioadaCaracter;
    }

    private void DeblocheazaVolant()
    {
        fantomaActiva = false;
        volantBlocat = false;
        volantPotriviri = 0;
        ratariVolant = 0;
        areStartAnterior = false;
        perioadaCaracter = 7.5 * samplesPerBit;
    }

    private void ReseteazaAtc()
    {
        atcMarkEnv = atcSpaceEnv = 0;
        atcMarkZgomot = atcSpaceZgomot = 0;
    }

    private static double MedieDescrescatoare(double medie, double x, double fereastra) => medie + (x - medie) / fereastra;

    /// <summary>
    /// Anvelopa: atac 1/4 bit, decadere 8 biti. Zgomotul: coboara in 1/4 bit, urca in 48 de biti.
    /// </summary>
    private double DecizieAtc(double mark, double space)
    {
        double bit = samplesPerBit;
        atcMarkEnv = MedieDescrescatoare(atcMarkEnv, mark, mark > atcMarkEnv ? bit / 4 : bit * 8);
        atcSpaceEnv = MedieDescrescatoare(atcSpaceEnv, space, space > atcSpaceEnv ? bit / 4 : bit * 8);
        atcMarkZgomot = MedieDescrescatoare(atcMarkZgomot, mark, mark < atcMarkZgomot ? bit / 4 : bit * 48);
        atcSpaceZgomot = MedieDescrescatoare(atcSpaceZgomot, space, space < atcSpaceZgomot ? bit / 4 : bit * 48);

        double zgomot = Math.Min(atcMarkZgomot, atcSpaceZgomot);
        double m = Math.Max(Math.Min(mark, atcMarkEnv), zgomot) - zgomot;
        double s = Math.Max(Math.Min(space, atcSpaceEnv), zgomot) - zgomot;
        double em = Math.Max(atcMarkEnv - zgomot, 0);
        double es = Math.Max(atcSpaceEnv - zgomot, 0);
        double v = m * em - s * es - 0.25 * (em * em - es * es);
        // Normalizat la unitatea unei amplitudini, ca pragul de Sensibilitate sa ramana pe aceeasi scara.
        return v / Math.Max(em + es, 1e-12);
    }

    /// <summary>Biquad RBJ trece-jos, Q = 1/sqrt(2) (Butterworth ordin 2).</summary>
    private sealed class Biquad
    {
        private double b0 = 1, b1, b2, a1, a2;
        private double x1, x2, y1, y2;

        public void ConfigureazaTreceJos(double fc, double fs)
        {
            double w = 2 * Math.PI * fc / fs;
            double alpha = Math.Sin(w) / Math.Sqrt(2);
            double c = Math.Cos(w);
            double a0 = 1 + alpha;
            b0 = (1 - c) / 2 / a0;
            b1 = (1 - c) / a0;
            b2 = (1 - c) / 2 / a0;
            a1 = -2 * c / a0;
            a2 = (1 - alpha) / a0;
            Reseteaza();
        }

        public void Reseteaza() => x1 = x2 = y1 = y2 = 0;

        public double Proceseaza(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            return y;
        }
    }
}
