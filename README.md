# RTTY Decoder (Windows)

Decodor RTTY (45,45 baud, shift 170 Hz) pentru Windows, portat in C# (.NET 10) din aplicatia
Android [Yaesu-FTX1-CAT-Control](https://github.com/Eugen68/Yaesu-FTX1-CAT-Control).
Gandit ca **modul**: decodarea, sursele audio si interfata sunt biblioteci separate, de inclus intr-o
aplicatie mai mare.

- trei decodoare: **Chebyshev** (implicit), **Matched Filter** si **DeepRTTY** (model neural ONNX)
- audio **live** de la placa de sunet / interfata radio / cablu virtual (WASAPI), orice rata
- decodare din **fisiere WAV** (PCM 8/16/24/32 bit sau float, mono/stereo, orice rata), in timp
  real sau cat de repede se poate
- **waterfall** calibrat in dBFS, FFT 512/1024, istoric 8/15/30 s, zoom si deplasare fara
  pierderea istoricului, palete, pauza imagine si captura PNG; acord prin click si **A-TUNE**
- **indicator de acord** cu elipse incrucisate (ca pe osciloscop), in dreapta waterfall-ului
- NORM/REV, AFC/PLL, NB, DNR, SNR-G, ATC, volant de caracter, ITA2 / US-TTY, salvarea textului
- **SQL** (squelch, pornit implicit): pe zgomot nu mai apar caractere aleatoare. Criteriul e ritmul
  caracterelor - pe un semnal RTTY real vin la ~7.5 biti unul dupa altul (chiar si cu pauze de
  tastare, in interiorul cuvintelor), pe zgomot la intervale intamplatoare (masurat: zgomot 6% in
  ritm, semnal 56-100%; bitul de stop si SNR-ul nu deosebesc zgomotul). La deschidere se scrie si
  inceputul mesajului, iar la pauzele de tastare nu se pierde nimic. Afecteaza doar textul afisat,
  nu decodarea; pragul (implicit 35%) in Reglaje. Decodoarele clasice; DeepRTTY are filtrul lui.
- **emisie RTTY prin FSK**: 8 macro-uri (F1..F8), text trimis la Enter si modul LIVE
- **MONITOR**: semnalul primit se aude in difuzoare (placa de iesire implicita), cu volum reglabil
- **indicative** evidentiate in terminal si **lista statiilor auzite** (tara, continent, CQ, de cate
  ori, prima/ultima data), cu biblioteca [HamCallsign](https://github.com/Eugen68/HamCallsign)

## Instalare

Din [Actions](../../actions) (artefactul `RttyDecoder-win-x64`) sau din [Releases](../../releases):
dezarhiveaza si porneste `RttyDecoder.exe`. Nu trebuie instalat .NET. Arhiva contine si
`rtty_sample_11025hz.wav` pentru o proba rapida (Mark 1275 Hz - apasa A-TUNE sau click pe semnal).

Setarile se salveaza in `%APPDATA%\YO9VA\RttyDecoder\settings.json`, inclusiv pozitia si
marimea ferestrei (daca monitorul pe care era nu mai exista, fereastra apare centrata).

## Utilizare

1. Alege intrarea (placa de sunet a statiei) si canalul (stanga/dreapta/mix), apoi **START**.
   Nivelul ideal e intre -30 si -6 dBFS (indicatorul de langa SNR).
2. Click pe semnal in waterfall (pe tonul Mark - linia rosie; linia portocalie e Space) sau **A-TUNE**.
   Rotita mouse-ului muta acordul cu 1 Hz (cu Ctrl, 10 Hz).
   **Indicatorul de acord** din dreapta waterfall-ului: Mark pe orizontala, Space pe verticala.
   Acordat corect = o cruce din doua elipse subtiri. Elipse inclinate sau groase = dezacordat;
   o singura elipsa = doar purtatoare sau REV gresit; o pata = zgomot sau semnal prea slab.
3. Daca iese text fara sens, incearca **REV**.
4. **MONITOR** reda in difuzoare exact ce primeste decodorul (intarziere ~0.1-0.4 s). Atentie la
   reactie: daca intrarea e chiar iesirea placii (Stereo Mix, cablu virtual), sunetul se reintoarce.
   La fisierele WAV se aude doar cu *Timp real* bifat.
5. **DeepRTTY** decodeaza mai bine semnalele slabe, dar cu intarziere: textul gri de sub terminal e
   provizoriu si se confirma (verde) dupa ~8-12 s.

6. **Indicativele** din textul decodat apar evidentiate: verde = cunoscut (in MASTER.SCP sau in lista LoTW), auriu =
   forma corecta dar necunoscut (poate fi si o eroare de decodare), portocaliu = indicativul propriu
   (campul *Indicativul meu* din lista statiilor). Mouse-ul deasupra arata tara si zonele.
   **Click** copiaza indicativul in clipboard, **dublu click** deschide pagina lui pe QRZ.com (la fel
   si in lista). Lista (butonul **Statii**) se poate sorta pe orice coloana si salva ca CSV.
   Cu un cont QRZ.com si/sau HamQTH.com (meniul **Setari**), indicativele aurii se verifica si online
   (QRZ.com intai, HamQTH.com ca rezerva gratuita): cele confirmate devin verzi, cu numele si orasul
   in tooltip; cele negasite raman aurii ("probabil o eroare de decodare"). O singura cautare pe
   indicativ si sesiune.
   cty.dat si MASTER.SCP sunt incluse; daca au peste 7 zile, aplicatia descarca in fundal versiunile
   noi in `%LOCALAPPDATA%\YO9VA\RttyDecoder`, plus zilnic lista utilizatorilor LoTW activi (~6 MB,
   doar daca s-a schimbat). Se folosesc de la pornirea urmatoare; fara internet nu se intampla nimic.
   Indicativele cu prefix litera+cifra (`S50A`, `A61BK`, `E73A`) sunt recunoscute daca cty.dat
   confirma prefixul.

### Waterfall: navigare si niveluri

| Comanda | Efect |
| --- | --- |
| Click | Acordul decodorului pe frecventa indicata; cu **Atragere pe semnal**, cauta o pereche apropiata, folosind NORM/REV curent |
| Click pe un triunghi de sus | Acord pe perechea RTTY marcata (vezi **Semnale** mai jos) |
| Rotita / Ctrl+rotita | Acord fin: 1 Hz / 10 Hz |
| Alt+rotita | Zoom in jurul cursorului |
| Alt+tragere / tragere cu butonul mijlociu | Deplasare in frecventa; dezactiveaza urmarirea automata a acordului |
| **0–4 kHz**, **Semnal**, **+ / −** | Vedere generala, centrare pe Mark/Space sau zoom; istoricul ramane disponibil |
| **Urmeaza** | Centrul imaginii urmareste acordul, cand vederea este marita |
| Shift+click | QSY explicit, numai daca statia este conectata si comanda este disponibila |
| **Pauza imagine**, **PNG...** | Ingheata afisajul in timp ce receptia continua; salveaza imaginea vizibila cu axe si markeri |

**FFT Rapid · 512** are pasul 15,625 Hz si fereastra de 64 ms; **Detaliat · 1024**,
7,8125 Hz si 128 ms. Cadrele vin la fiecare 32 ms in ambele moduri. Schimbarea FFT pastreaza
istoricul, iar zoom-ul reda din nou spectrele complete. Curba de deasupra arata ultimul spectru
vizibil. Sub cursor apar frecventa, vechimea audio si nivelul cadrului din acel moment; golurile
nu primesc un nivel inventat.

Scara este **amplitudine spectrala dBFS**, normalizata dupa fereastra Hamming: o sinusoida de
amplitudine 0,5, centrata pe un bin, indica aproximativ −6,02 dBFS. Nu este puterea RMS a
intregului audio si nici o densitate de zgomot in dB/Hz. Nivelurile depind de pozitia intre bin-uri,
de rezolutia FFT si de filtrul anti-aliasing (atenuare spre 4 kHz). Indicatorul audio de langa SNR
ramane separat. Setarile vechi de podea sunt convertite o singura data la noua scara.

**Pe imagine**, pe langa M (rosu) si S (portocaliu):
- **TX** - doua linii punctate verzi pe 2125 / 2295 Hz, cu statia in RTTY-L / RTTY-U: acolo pleaca
  emisia FSK. Cand perechea corespondentului nu e pe ele, raspunsul tau iese alaturi - A-TUNE + dublu
  click (sau QSY-ul automat) aduce statia pe frecventa lui.
- **Semnale** - triunghiuri albastre sus, deasupra fiecarei perechi RTTY (doua tonuri la 170 Hz, ambele
  peste zgomot) gasite in banda, din media ultimelor ~0,3 s; click pe triunghi = acord pe ea. Se pot
  ascunde din bifa **Semnale**.
- **Zona filtrului** - benzi slab colorate in jurul M si S, pe latimea filtrelor Chebyshev (numai cu
  decodorul Chebyshev): se vede daca semnalul intra in filtru.
- Cu statia conectata, cursorul arata si **frecventa RF** a tonului (in RTTY, pe un Mark: frecventa
  corespondentului).

In **Afisare waterfall: zoom, istoric, FFT, niveluri, paleta si inaltime**, **Auto** estimeaza zgomotul din banda
250–3000 Hz si il urmareste lent, independent de zoom. **Fixeaza nivel** tine podeaua fixa;
dezactivarea Auto permite reglajul manual. **Interval** stabileste domeniul deasupra podelei,
iar **Gama** modifica intensitatea: peste 1 intuneca semnalele slabe, sub 1 le evidentiaza.
Sunt disponibile paleta clasica si cea de contrast, spectrul si inaltimea de 160–400 px.
Preferintele se salveaza intre sesiuni; pauza imaginii este temporara.

Istoricul reprezinta **timp audio**, inclusiv la redarea WAV accelerata. START/STOP, schimbarea
sursei, acordul/QSY si TX/RX sunt marcate prin separatoare. Cadrele omise sunt afisate ca goluri,
cu durata lor; numarul cadrelor eliminate apare in stare. Cand mai multe cadre ajung pe acelasi
rand de pixeli se pastreaza maximul, pentru vizibilitatea semnalelor scurte. Pauzele de timp real
cu sursa oprita nu extind axa audio. Bufferul live pastreaza cel mult 30 s, independent de
imaginea inghetata.

**A-TUNE** cauta ambele tonuri, verifica distanta dintre ele si nivelul fata de zgomot, apoi
rafineaza frecventa sub pasul FFT. O purtatoare unica nu este suficienta. Scorul spectral afisat
de Snap este euristic, nu o probabilitate de decodare; NORM/REV la Snap ramane cel ales manual.
QSY-ul automat inainte de TX ramane o optiune separata, descrisa mai jos.

### Emisie

**Emisia (FSK)** - statia in modul RTTY (de obicei RTTY-L), PTT-ul si pinul FSK din Setari >
Conexiune statie:
- **Macro-urile F1..F8** sub terminal (click sau tasta F1..F8). Mouse-ul deasupra arata textul
  exact care pleaca, cu indicativele inlocuite (si ce lipseste). Click dreapta pe un buton =
  editare (nume si text). Variabile: `{MYCALL}` (campul "Indicativul meu"), `{CALL}` (ultimul
  indicativ pe care ai dat click in terminal sau in lista), `{RST}` (599).
- **Campul de emisie**: Enter = trimite. **LIVE**: emisia sta pornita si fiecare caracter pleaca
  imediat ce il scrii (intre ele, diddle = LTRS).
- **Ora UTC in terminal**: fiecare mesaj incepe pe un rand nou cu `[HH:mm:ss]: ` - verde la
  receptie, rosu la emisie. Mesaj nou = dupa o pauza de cel putin 5 s, la trecerea intre receptie
  si emisie si (la receptie) dupa orice schimbare de acord: click/rotita pe waterfall, A-TUNE,
  NORM/REV, QSY sau VFO-ul statiei (nu si deriva AFC). Sus, dupa volumul monitorizarii, ceasurile
  **UTC** si **LOCAL**.
- Mesajele trimise in timpul unei emisii se adauga la coada (un singur PTT). **STOP (Esc)** opreste
  imediat. Textul trimis apare in terminal cu galben; cat timp se emite, decodarea sta.
- **Setari > Setari transmisie**: preambul (LTRS dupa PTT, implicit 2), pauza la sfarsit (300 ms),
  limita unei emisii (180 s), CR/LF automat, diddle, inversarea pinului FSK (implicit oprita).
  Valorile implicite sunt cele din aplicatia Android. AFSK urmeaza.
- **QSY automat inainte de raspuns** (Setari transmisie, implicit oprit): la un macro sau text (Enter)
  care **nu incepe cu CQ**, dupa un **A-TUNE** care a calculat alta frecventa decat a statiei, statia se
  muta intai acolo (ca dublu click pe A-TUNE), apoi pleaca emisia (~250 ms mai tarziu). Doar in RTTY-L /
  RTTY-U. Nu muta: CQ-ul (chemi pe frecventa ta - si uita QSY-ul pregatit), LIVE, dupa un click pe alt
  semnal in waterfall sau daca statia s-a mutat de la A-TUNE.
- **Polaritatea emisiei**: pinul FSK pleaca mereu normal (activ = Mark) - **NORM/REV e doar pentru
  receptie**, iar emisia nu depinde nici de modul statiei. Testul pe statii (FTDX10 <-> FTX-1 +
  aplicatia Android, `tools/android-patch/README.md`): emisia iese la fel in RTTY-L si RTTY-U, doar
  receptia in RTTY-U cere REV (comportament Yaesu). Emisie inversata, daca e nevoie: polaritatea TX
  din meniul statiei; bifa "Inverseaza pinul FSK" doar pentru interfete care inverseaza linia.

**Meniul** de sus:
- **Fisier**: Deschide WAV (Ctrl+O), Salveaza textul (Ctrl+S), Salveaza lista statiilor (CSV), Iesire
- **Indicative**: Deschide folderul cu date (`%LOCALAPPDATA%\YO9VA\RttyDecoder`), Actualizeaza datele
  acum (verifica imediat cty.dat, MASTER.SCP si LoTW si le foloseste pe cele noi, fara repornire si
  fara sa goleasca lista statiilor), Starea datelor (cate indicative si de cand e fiecare fisier)
- **Setari**: Conexiune statie (vezi mai jos), Reconecteaza statia; Verificare online (QRZ.com / HamQTH.com) - conturile pentru confirmarea indicativelor
  necunoscute local (parolele criptate cu Windows DPAPI in `settings.json`, ca in CwDecoder);
  Deschide folderul setarilor (`%APPDATA%\YO9VA\RttyDecoder`, `settings.json`)
- **Ajutor**: Despre

**Statia** (biblioteca [HamRig](https://github.com/Eugen68/HamRig), submodul `external/HamRig`),
din **Setari > Conexiune statie**:
- **Direct pe portul serial** (CAT Yaesu: FTX-1, FTDX10, FT-710, FT-991A...): portul CAT (la FTX-1
  primul port USB, "Enhanced"), viteza (ca in meniul statiei, implicit 38400) si PTT pe un pin al
  portului CAT (Fara / RTS / DTR). Statia anunta singura schimbarile (AI1).
- **Prin OmniRig**: Rig 1 / Rig 2; statia poate fi folosita in acelasi timp si de alte programe.
  OmniRig doar citeste si schimba frecventa si modul, fara PTT.
- **Port manipulare si PTT**: port, viteza, PTT (Fara / RTS / DTR) si manipulare (Fara / RTS / DTR,
  FSK pentru RTTY, keying CW). La FTX-1 de obicei al doilea port ("Standard"): ex. PTT pe RTS, FSK
  pe DTR; portul se deschide o singura data.
- PTT-ul se alege **intr-un singur loc** (la portul CAT sau la portul de manipulare): cand alegi un
  pin intr-o parte, cealalta trece pe Fara. Fara niciun pin, la conexiunea directa emisia se
  comanda prin CAT (`TX1;`); la OmniRig nu exista PTT.
- **Testeaza** citeste frecventa si modul; **Test PTT 0.5 s** comuta emisia o jumatate de secunda;
  **Test manipulare 0.5 s** activeaza pinul de manipulare.

Langa nivel apar frecventa VFO si modul statiei, iar in USB/LSB/DATA si frecventa RF a Mark-ului
(ex. `14.083.500 DATA-U   Mark 14.085.625`); **TX** rosu cand statia emite. **Shift+click pe
waterfall** = QSY: statia se muta astfel incat semnalul de sub mouse sa ajunga pe Mark-ul
decodorului.

**A-TUNE si frecventa pentru emisie (FSK).** La FSK statia emite exact unde e acordata, deci ca sa
raspunzi pe frecventa corespondentului trebuie mutata **statia**, nu doar decodorul:
- **un click pe A-TUNE**: gaseste perechea de tonuri (170 Hz) si stabileste **care e Mark** pe
  ultimele ~5 s: intai dupa durate (stopul de 1.5 biti e pe Mark, deci doar Mark-ul are rulari de
  x.5 biti), apoi, daca duratele nu decid, dupa incadrarea caracterelor (start pe Space, stop pe
  Mark) - decodorul se acordeaza pe Mark-ul real, cu NORM/REV pus dupa semnal. Pe RYRY incadrarea
  singura se insela; duratele nu. In subsidiar calculeaza frecventa statiei pentru
  emisie (perechea semnalului pe 2125 / 2295; in RTTY-L ca in LSB, in RTTY-U ca in USB), afisata in mesaj
  si langa VFO (`TX: 3.501.300`);
- **dublu click pe A-TUNE**: muta statia pe frecventa calculata, decodorul pe perechea 2125 / 2295
  (Mark 2125 pe NORM, 2295 pe REV);
- click pe waterfall ramane acordul decodorului, Shift+click = QSY ca inainte;
- REV-ul pus de A-TUNE (de exemplu in RTTY-U) e doar pentru receptie: emisia FSK ramane normala.

Exemplu: ambele statii pe 3.501.000, corespondentul cu 300 Hz mai sus apare in RTTY-L cu Mark-ul
la ~1825 Hz; A-TUNE acordeaza decodorul pe 1825 si arata `TX: 3.501.300`; dublu click muta statia
acolo, iar semnalul vine pe 2125.

Se aliniaza **perechea** de tonuri, nu Mark-ul: o statie de pe aceeasi frecventa se aude pe 2125 / 2295
in ambele moduri, dar in RTTY-U Mark-ul e tonul de sus (2295 - de aceea receptia cere REV). Testul pe
statii (FTDX10 pe 3.505.000, FTX-1 emite pe 3.505.200): RTTY-L QSY la 3.505.201; in RTTY-U, cand se
alinia Mark-ul, QSY-ul iesea 3.505.370 (+170 Hz) - corectat.

Meniul tine de fereastra aplicatiei, nu de modul: o aplicatie mai mare isi pune propriul meniu si
apeleaza aceleasi comenzi (`RttyDecoderView.OpenWavDialog()`, `SaveTextDialog()`,
`SaveStationsDialog()`, `RttyDecoderViewModel.UseReferenceData(...)`).

Pentru o inregistrare: meniul **Fisier > Deschide WAV...** (Ctrl+O). Cu *Timp real* debifat, un fisier de o ora se decodeaza in
cateva zeci de secunde (DeepRTTY: cateva minute).

## Profiluri de receptie si reglaje

Lista **Profil** ofera trei puncte de pornire masurate pe corpusul W6IWI:

| Profil | Decodor | Utilizare |
|---|---|---|
| Semnal bun · rapid | Matched Filter | Acord bun si fading lent, fara confirmarea intarziata a modelului neural |
| Semnal slab · DeepRTTY | DeepRTTY | Cele mai putine erori pe inregistrarea QRN; confirmarea textului dupa aproximativ 8-12 s |
| QRN / flutter · rapid | Chebyshev | Alternativa clasica rapida la flutter; NB si ATC nu sunt activate automat, deoarece au iesit mai slab pe proba QRN masurata |

Toate pornesc cu SQL 35%, volant activ si pragurile implicite. Profilul reseteaza reglajele
procesarii, dar pastreaza Mark, NORM/REV, AFC/PLL si ITA2/US-TTY. Modificarea manuala a unui reglaj
al profilului afiseaza **Personalizat**. Setarile efective se salveaza ca pana acum; la incarcare
profilul este recunoscut daca valorile coincid. Sunt puncte de pornire pentru corpusul masurat,
nu o alegere automata sau o garantie pentru orice tip de interferenta.

In **Reglaje** apare explicatia decodorului selectat. Controalele fara efect sunt dezactivate:

| Reglaj | Chebyshev | Matched Filter | DeepRTTY |
|---|---|---|---|
| NB / DNR | Audio-ul decodorului | Audio-ul decodorului | Numai SNR si AFC/PLL, nu intrarea modelului |
| Latime filtru Chebyshev | Decodare si SNR/AFC | Nu se aplica | Numai SNR/AFC |
| ATC | Decizie la fading selectiv | Normalizare proprie | Nu se aplica |
| SNR-G, Sensibilitate, Volant | Decizie/sincronizare | Decizie/sincronizare | Nu se aplica textului neural |
| SQL | Numai textul afisat | Numai textul afisat | Filtru propriu al DeepRTTY |
| NORM/REV, US-TTY | Se aplica | Se aplica | Se aplica |

Modelul neural primeste audio-ul original, prin propriul front-end. Nu trebuie presupus ca
activarea DNR/NB curata si intrarea lui. Rezultatele si metoda de masurare:
[recordings/README.md](recordings/README.md) si [benchmark](tools/RttyDecoder.Benchmarks/README.md).

## Structura

| Proiect | Ce contine | Dependente |
|---|---|---|
| `src/RttyDecoder.Core` | Decodarea: `RttyReceiver` (API-ul modulului), demodulatorul clasic, DeepRTTY, Baudot, reesantionare, spectru, A-TUNE. Fara UI, merge si pe Linux/macOS. | ONNX Runtime |
| `src/RttyDecoder.Audio` | `WasapiInputSource` (placa de sunet, Windows), `WavFileSource` | NAudio |
| `src/RttyDecoder.Wpf` | `RttyDecoderView` (UserControl), `RttyDecoderViewModel`, `WaterfallControl` | WPF, HamCallsign |
| `external/HamCallsign` | Submodul git: recunoasterea indicativelor (biblioteca comuna aplicatiilor noastre) | |
| `external/HamRig` | Submodul git: controlul statiei (CAT Yaesu direct, OmniRig) | System.IO.Ports |
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

Indicativele: gazda da bazele (`vm.UseReferenceData(ReferenceData.Load(folder))`), indicativul propriu
(`vm.MyCall`) si primeste click-urile prin `vm.CallsignSelected` (ex. pentru log sau DX cluster);
`vm.Callsigns` e `CallsignTracker`-ul cu lista statiilor.

### Bufferul si oprirea DeepRTTY

DeepRTTY pastreaza cel mult 24 s de audio neprocesat, inclusiv blocul aflat in lucru.
Sub terminal apare cantitatea de audio in asteptare; aceasta nu include intarzierea normala
(~8-12 s) a textului provizoriu. La un WAV (inclusiv cu *Timp real*), citirea asteapta automat
loc liber, fara sa piarda audio. La live, daca bufferul se umple, decodorul abandoneaza audio-ul
vechi si textul lui neconfirmat si reincepe de la semnalul nou; contorul de recuperari apare
langa starea bufferului. Textul deja confirmat ramane in terminal.

Pentru gazdele modulului care decodeaza surse finite, folositi
`rx.Process(samples, sampleRate, waitForDecoder: true)` (sau aceeasi optiune la `Process48k`).
STOP elibereaza o citire care asteapta bufferul. `TryFlush(timeout)` raporteaza daca finalul
fisierului a fost confirmat; timeout-ul include si asteptarea unui buffer plin.

STOP/START, CLEAR si reacordarea folosesc sesiuni distincte, procesate de un singur fir de
inferenta. STOP nu asteapta o inferenta nativa lenta; rezultatul sesiunii anulate este ignorat.
La inchidere modelul este eliberat abia dupa terminarea ultimei inferente, pe fundal.
Gazda poate folosi `await rx.DisposeAsync()` pentru a astepta si eliberarea resurselor native.
Gazdele care folosesc direct `DeepRttyReceiver` trebuie sa astepte `Completion` dupa `Dispose`
inainte de a elibera modelul pe care l-au furnizat.

## Compilare si teste

HamCallsign e submodul, deci la clonare:

```bash
git clone --recurse-submodules https://github.com/Eugen68/RttyDecoder.git
```

(intr-o clona existenta: `git submodule update --init`)

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
  nu doar inceputul fiecarui bloc. Afisaj calibrat in dBFS, cu FFT selectabil, istoric complet
  pentru zoom si podea automata stabilizata.
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
