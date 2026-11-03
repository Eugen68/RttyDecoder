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

## Probleme cunoscute (mostenite)

- **AFC / PLL nu urmaresc deriva** pe semnal stabil: conditia `magMark > 2 x nivelZgomotMediu` nu e
  indeplinita in regim stationar, deci Mark-ul nu se misca. Comportamentul e identic cu aplicatia
  Android (verificat cu codul Java) si e fixat de testul `AfcComportamentMostenit`; de reparat in
  ambele aplicatii.

## Licenta

MIT - vezi [LICENSE](LICENSE). Componentele terte: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
