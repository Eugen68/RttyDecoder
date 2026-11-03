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
    private double afcRotRe = 1.0, afcRotIm;

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
    private double fazaVectRe, fazaVectIm;
    private int bitCounter;
    private int currentByte;

    // Debounce la startul de cadru: "space" trebuie sa persiste 0.7 biti.
    private int contorConfirmareStart;
    private int pragConfirmareStart;

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

    // AFC
    private double iAcc, qAcc;
    private double lastPhase;
    private int samplesSinceLastAfc;
    private const int FerestreAfc = 2;
    private int fereastraAfcSamples;
    private double freqErrorFiltrat;
    private const double AfcEmaAlpha = 0.25;
    private const double AfcMaxCorectieHz = 5.0;
    private const double AfcPragOutlierHz = 20.0;

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
        mfMarkPowNRe = Math.Cos(wMarkN);
        mfMarkPowNIm = Math.Sin(wMarkN);
        mfSpacePowNRe = Math.Cos(wSpaceN);
        mfSpacePowNIm = Math.Sin(wSpaceN);
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
        double wAfc = 2.0 * Math.PI * frecventaTunedBaza / sampleRateFiltre;
        afcRotRe = Math.Cos(wAfc);
        afcRotIm = Math.Sin(wAfc);

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
        fazaVectRe = 1.0; fazaVectIm = 0.0;
        bitCounter = 0;
        currentByte = 0;
        contorConfirmareStart = 0;
        pragConfirmareStart = (int)JRound(samplesPerBit * 0.7);
        contorEroriFramingConsecutive = 0;
        nbNivelMediu = 0.05;
        nbUltimulEsantionValid = 0;
        nbContorBlankConsecutiv = 0;
        dnrX1 = 0; dnrX2 = 0; dnrY1 = 0; dnrY2 = 0;
        iAcc = 0; qAcc = 0;
        lastPhase = 0;
        samplesSinceLastAfc = 0;
        fereastraAfcSamples = Math.Max(1, (int)JRound(samplesPerBit)) * FerestreAfc;
        freqErrorFiltrat = 0;

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
            fazaVectRe = 1.0; fazaVectIm = 0.0;
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
            ReseteazaAtc();
            DeblocheazaVolant();
            if (folosesteMatchedFilter && mfBuffer != null)
            {
                Array.Clear(mfBuffer);
                mfBufferPos = 0;
                mfMarkAccRe = mfMarkAccIm = 0;
                mfSpaceAccRe = mfSpaceAccIm = 0;
            }
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
            diferentaBruta = folosesteAtc && !folosesteMatchedFilter
                ? DecizieAtc(markFiltrat, spaceFiltrat)
                : markFiltrat - spaceFiltrat;

            // AFC/PLL
            double newPhaseRe = fazaVectRe * afcRotRe - fazaVectIm * afcRotIm;
            double newPhaseIm = fazaVectRe * afcRotIm + fazaVectIm * afcRotRe;
            fazaVectRe = newPhaseRe;
            fazaVectIm = newPhaseIm;

            iAcc += sample * fazaVectRe;
            qAcc += sample * fazaVectIm;
            samplesSinceLastAfc++;

            if (samplesSinceLastAfc >= fereastraAfcSamples)
            {
                double currentPhase = Math.Atan2(qAcc, iAcc);
                double deltaPhase = currentPhase - lastPhase;

                while (deltaPhase > Math.PI) deltaPhase -= 2.0 * Math.PI;
                while (deltaPhase < -Math.PI) deltaPhase += 2.0 * Math.PI;

                double freqError = deltaPhase * sampleRate / (2.0 * Math.PI * samplesSinceLastAfc);

                if (magMark > magSpace * 1.2 && magMark > nivelZgomotMediu * 2.0)
                {
                    if (Math.Abs(freqError) <= AfcPragOutlierHz)
                    {
                        freqErrorFiltrat = freqErrorFiltrat * (1.0 - AfcEmaAlpha) + freqError * AfcEmaAlpha;
                        double freqErrorLimitat = Math.Max(-AfcMaxCorectieHz, Math.Min(AfcMaxCorectieHz, freqErrorFiltrat));

                        if (modUrmarireFrecventa == 1) frecventaTunedBaza += freqErrorLimitat * 0.05;
                        else if (modUrmarireFrecventa == 2) frecventaTunedBaza += freqErrorLimitat * 0.25;

                        if (modUrmarireFrecventa > 0)
                        {
                            sampleRateFiltre = sampleRate;
                            RecalculeazaCoeficientiFrecventa();
                            if (folosesteMatchedFilter && Math.Abs(freqError) > 0.5) RecalculeazaSabloaneMf();
                        }
                    }
                }
                iAcc = 0; qAcc = 0;
                lastPhase = currentPhase;
                samplesSinceLastAfc = 0;

                double normAfc = Math.Sqrt(fazaVectRe * fazaVectRe + fazaVectIm * fazaVectIm);
                if (normAfc > 1e-9)
                {
                    fazaVectRe /= normAfc;
                    fazaVectIm /= normAfc;
                }
            }

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
                            double start = startPrezis;
                            if (contorConfirmareStart >= samplesPerBit * 0.3)
                            {
                                double abatere = (esantionAbs - contorConfirmareStart) - startPrezis;
                                double limita = 0.25 * samplesPerBit;
                                start += VolantCorectie * Math.Max(-limita, Math.Min(limita, abatere));
                            }
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
                else if (bitCurent == 0 && contorConfirmareStart >= pragConfirmareStart)
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
                    if (bitCounter < 5)
                    {
                        if (bitCurent == 1) currentByte |= 1 << bitCounter;
                        bitCounter++;
                    }
                    else
                    {
                        bool volantEraBlocat = folosesteVolant && volantBlocat;
                        if (folosesteVolant) ActualizeazaVolant(bitCurent == 1);
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
