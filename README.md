# RTTY Decoder (Windows)

Decodor RTTY (45,45 baud, shift 170 Hz) pentru Windows, portat in C# (.NET 10) din aplicatia
Android [Yaesu-FTX1-CAT-Control](https://github.com/Eugen68/Yaesu-FTX1-CAT-Control).
Gandit ca **modul**: decodarea, sursele audio si interfata sunt biblioteci separate, de inclus intr-o
aplicatie mai mare.

- trei decodoare: **Chebyshev** (implicit), **Matched Filter** si **DeepRTTY** (model neural ONNX)
- audio **live** de la placa de sunet / interfata radio / cablu virtual (WASAPI), orice rata
- decodare din **fisiere WAV** (PCM 8/16/24/32 bit sau float, mono/stereo, orice rata), in timp
  real sau cat de repede se poate
- **waterfall** cu acord prin click, rotita pentru reglaj fin si **A-TUNE**
- NORM/REV, AFC/PLL, NB, DNR, SNR-G, ATC, volant de caracter, ITA2 / US-TTY, salvarea textului

## Instalare

Din [Actions](../../actions) (artefactul `RttyDecoder-win-x64`) sau din [Releases](../../releases):
dezarhiveaza si porneste `RttyDecoder.exe`. Nu trebuie instalat .NET. Arhiva contine si
`rtty_sample_11025hz.wav` pentru o proba rapida (Mark 1275 Hz - apasa A-TUNE sau click pe semnal).

Setarile se salveaza in `%APPDATA%\YO9VA\RttyDecoder\settings.json`.

## Utilizare

1. Alege intrarea (placa de sunet a statiei) si canalul (stanga/dreapta/mix), apoi **START**.
   Nivelul ideal e intre -30 si -6 dBFS (indicatorul de langa SNR).
2. Click pe semnal in waterfall (pe tonul Mark - linia rosie; linia portocalie e Space) sau **A-TUNE**.
   Rotita mouse-ului muta acordul cu 1 Hz (cu Ctrl, 10 Hz).
3. Daca iese text fara sens, incearca **REV**.
4. **DeepRTTY** decodeaza mai bine semnalele slabe, dar cu intarziere: textul gri de sub terminal e
   provizoriu si se confirma (verde) dupa ~8-12 s.

Pentru o inregistrare: **Deschide WAV...**. Cu *Timp real* debifat, un fisier de o ora se decodeaza in
cateva zeci de secunde (DeepRTTY: cateva minute).

## Structura

| Proiect | Ce contine | Dependente |
|---|---|---|
| `src/RttyDecoder.Core` | Decodarea: `RttyReceiver` (API-ul modulului), demodulatorul clasic, DeepRTTY, Baudot, reesantionare, spectru, A-TUNE. Fara UI, merge si pe Linux/macOS. | ONNX Runtime |
| `src/RttyDecoder.Audio` | `WasapiInputSource` (placa de sunet, Windows), `WavFileSource` | NAudio |
| `src/RttyDecoder.Wpf` | `RttyDecoderView` (UserControl), `RttyDecoderViewModel`, `WaterfallControl` | WPF |
| `src/RttyDecoder.App` | Fereastra aplicatiei si salvarea setarilor | |
| `tests/RttyDecoder.Core.Tests` | Teste xUnit | |

### Folosirea ca modul

Doar decodarea (orice tip de aplicatie):

```csharp
using var rx = new RttyReceiver();
rx.TextReceived += text => Console.Write(text);          // pe firul audio
rx.Update(s => { s.MarkFrequencyHz = 2125; s.Decoder = RttyDecoderType.DeepRtty; });
rx.Start();
// din callback-ul sursei audio, mono +-1, la orice rata:
rx.Process(samples, sampleRate);
```

Cu interfata, intr-o fereastra WPF:

```xml
<rtty:RttyDecoderView DataContext="{Binding Rtty}" />   <!-- Rtty = new RttyDecoderViewModel() -->
```

Setarile se citesc/scriu cu `RttyDecoderViewModel.ExportSettings()` / `ImportSettings()`
(`RttySettings` se serializeaza direct cu System.Text.Json).

## Compilare si teste

```bash
dotnet build
```

```bash
dotnet test tests/RttyDecoder.Core.Tests
```

Testele (inclusiv DeepRTTY) ruleaza si pe Linux. Interfata WPF se compileaza peste tot, dar ruleaza
doar pe Windows; CI-ul (`.github/workflows/build.yml`, runner Windows) ruleaza testele, publica
exe-ul si face un test de fum: porneste interfata reala si decodeaza `samples/rtty_sample_11025hz.wav`
cu Chebyshev si cu DeepRTTY. Un tag `v*` publica si un Release.

### Paritatea cu Android

Demodulatorul e portat 1:1 (aceleasi nume interne, ca modificarile sa se poata urmari intre cele
doua). `AndroidParityTests` compara textul decodat de C# cu iesirea codului Java original
(`tests/RttyDecoder.Core.Tests/Golden/android_parity.txt`, generat cu `tools/android-parity/Parity.java`)
pe 27 de semnale sintetice - inclusiv erorile de pe semnalele foarte slabe - si trebuie sa fie identic.
Generatorul de semnal foloseste un `java.util.Random` reprodus bit cu bit, deci semnalele sunt aceleasi.

Daca algoritmul se schimba intentionat (in ambele aplicatii), fisierul golden se regenereaza: se
compileaza `RttyDemodulator.java`, `BaudotCodec.java`, `RttyDemodulatorTest.java` (fara JUnit) si
`Parity.java` si se ruleaza `Parity`.

## Diferente fata de Android

- **Waterfall-ul** foloseste un filtru anti-aliasing inainte de decimarea 48 kHz -> 8 kHz (pe Android
  se lua un esantion din 6, deci zgomotul de peste 4 kHz se plia in imagine) si toate esantioanele,
  nu doar inceputul fiecarui bloc. Afisaj in dB, cu podea automata si contrast reglabil.
- **A-TUNE** rafineaza pozitia sub-bin (eroare de cativa Hz in loc de +-8 Hz).
- Audio la orice rata (reesantionare interna la 48 kHz, pe care sunt reglate filtrele).
- Setarile trec printr-un lacat comun cu procesarea (pe Android erau campuri scrise din UI si citite
  din firul audio).

## AFC / PLL (reparat fata de Android)

Bucla AFC din aplicatia Android avea **semnul erorii inversat**: corela audio-ul cu un fazor
e^{+j w_mark t}, a carui faza scade cand semnalul e deasupra Mark-ului, si aduna eroarea ca atare -
reactie pozitiva, acordul fugea de semnal (+9..+13 Hz pe inregistrarile W6IWI). Pe semnal stabil o
alta conditie (`magMark > 2 x nivelZgomotMediu`) bloca de altfel bucla complet.

Varianta noua e un discriminator de frecventa pe ambele tonuri: fiecare ton e coborat in banda de
baza si filtrat (~50 Hz), iar rotatia fazei pe 4 ms da abaterea, cu semnul corect. Se masoara doar
tonul care domina clar, iar o fereastra (~2 biti) conteaza doar daca masuratorile sunt coerente si
un ton a dominat majoritatea timpului - zgomotul dintre transmisii nu muta acordul. Castigurile
(AFC 5%, PLL 25%) si limitele au ramas cele vechi. Testele: `AfcTests` (convergenta de la +-12/+-25 Hz,
REV, deriva de 120 Hz, semnal slab, zgomot pur). Pe inregistrarile reale converge la acelasi Mark din
orice punct de plecare (+-15 Hz), pe tonul masurat din spectru.

Doua corectii ulterioare (Eugen, YO9VA), pentru zgomot: o fereastra AFC conteaza doar daca un ton
domina si pe anvelopele decodorului, iar corectia se aplica doar dupa doua ferestre valide la rand.
Fara ele, 3 minute de zgomot pur cu PLL mutau acordul cu pana la 60 Hz; acum ramane in +-1 Hz
(`AfcTests.ZgomotulNuMutaAcordul`).

## Volant: start corectat si cand space-ul incepe chiar inainte de mijlocul startului prezis

Cu volantul blocat, daca space-ul incepea cu mai putin de 0.3 biti inainte de mijlocul startului
prezis, cadrul pornea pe grila, necorectat - desi frontul real era cunoscut si mereu intarziat
(mediana +0.3 biti): bitii erau cititi prea devreme, iar grila ramanea decalata si pentru caracterul
urmator. Ramura aparea in 1-12% din cadre la Chebyshev si 8-20% la MF. Acum startul se corecteaza
mereu, cu regula existenta (castig 0.5, plafon +-0.25 biti). Gasit de Eugen, YO9VA, verificat pe
inregistrari: 14089 kHz Chebyshev 169 -> 60 erori, MF 97 -> 48; indicativul W6IWI corect 22 -> 28 / 31
la MF.

## Matched Filter (imbunatatit fata de Android)

Pe inregistrarile reale MF pierdea litere. Doua cauze, gasite cu ajutorul inregistrarilor W6IWI:

- **Fading selectiv.** Corelatorul integreaza un bit intreg, deci la o tranzitie |Mark| si |Space| se
  schimba liniar pe toata durata bitului. Cand un ton e mai slab, trecerea prin zero a diferentei se
  muta spre el cu pana la 1/2 bit: fronturile de start si momentele de esantionare aluneca, iar
  caracterele pica la bitul de stop. Acum fiecare ton e raportat la amplitudinea lui, invatata la
  momentele de esantionare, iar startul se confirma dupa 0.35 biti (in loc de 0.7). Sintetic, la
  fading 0.2-1 Hz: 24-32% -> 0.2-9% CER, fara cost pe semnalele slabe fara fading.
- **Faza instabila in interiorul bitului** (flutter, Doppler): jumatatile ferestrei se anuleaza
  partial. MF masoara coerenta semnalului si, daca e mica, trece singur pe doua jumatati de bit
  adunate necoerent; pe semnal stabil ramane pe suma coerenta.

Corelatorul glisant isi recalculeaza sumele din buffer la fiecare schimbare de frecventa (gasit de
Eugen, YO9VA): recursia lui e corecta doar la frecventa fixa, iar cu AFC-ul care corecteaza la ~2 biti
eroarea se aduna (MF + PLL pe 14086 kHz: 1548 -> 110 erori). Tot de aici: AFC-ul judeca dominanta
tonurilor pe filtrele lui proprii, nu pe anvelopele MF (care intarzie ~1/2 bit si deplasau acordul cu
+9..+10 Hz).

Limita: la flutter rapid (fading complex de 5-10 Hz) MF ramane slab (~25% CER, fata de ~1% la
Chebyshev) - integreaza un bit intreg, iar canalul se schimba mai repede. Acolo: Chebyshev sau DeepRTTY.
Teste: `MatchedFilterTests`.

## Patch pentru aplicatia Android

`tools/android-patch/RttyDemodulator.patch` aduce in `RttyDemodulator.java` ambele reparatii (AFC si
Matched Filter); se aplica cu `git apply` in radacina Yaesu-FTX1-CAT-Control. Java-ul reparat da exact
acelasi text ca C# - fisierul golden de paritate e generat cu el. Chebyshev fara AFC e neschimbat.

## Rezultate pe inregistrari reale

Vezi [recordings/README.md](recordings/README.md).

## Licenta

MIT - vezi [LICENSE](LICENSE). Componentele terte: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
