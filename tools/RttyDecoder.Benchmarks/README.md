# Benchmark reproductibil

Necesita .NET 10. Nu deschide porturi seriale, nu comuta PTT si nu emite radio.
CI-ul `benchmark` ruleaza separat de build, descarca release-ul public `recordings`, verifica
SHA256 si publica artefactul `RttyDecoder-benchmark`. Nu exista praguri de viteza/jitter pentru
runnerul partajat. Testele unitare verifica scoring-ul, ciclul de viata si secventele FSK.

## Rulare locala

Din radacina repo-ului, dupa `git submodule update --init`:

```powershell
gh release download recordings --repo Eugen68/RttyDecoder --pattern '*.wav' --dir recordings-data
dotnet build tools/RttyDecoder.Benchmarks -c Release
$revision = git rev-parse HEAD
dotnet tools/RttyDecoder.Benchmarks/bin/Release/net10.0/RttyDecoder.Benchmarks.dll run --revision $revision --output benchmark-results
dotnet tools/RttyDecoder.Benchmarks/bin/Release/net10.0/RttyDecoder.Benchmarks.dll fsk-timing --revision $revision --output benchmark-results
```

Pentru o verificare rapida, fara descarcarea corpusului:

```powershell
dotnet run --project tools/RttyDecoder.Benchmarks -c Release -- run --only sample --revision $revision --output benchmark-results
```

`--input` schimba folderul inregistrarilor, `--manifest` schimba manifestul. Sursele finite sunt
alimentate cu `waitForDecoder: true`; orice overrun sau Flush incomplet invalideaza rularea.
Fiecare combinatie fisier/configuratie porneste un proces nou, ca memoria modelului precedent
sa nu contamineze varful de memorie. Comparati versiunile pe aceeasi masina si repetati rularea
pentru comparatii de performanta. Un rezultat izolat de CI nu masoara variatia intre calculatoare.

## Corpus si referinte

- `sample`: WAV-ul inclus, cu text cunoscut, de doua ori (6 aparitii YO9VA).
- `14086` si `7086-qrn`: referinta din documentatia inregistrarilor W6IWI: mesajele numerotate
  00..99, `THE QUICK BROWN FOX JUMPED OVER THE LAZY DOG'S BACK. TEST DE W6IWI`. Se foloseste US-TTY,
  inclusiv apostroful. Numitorul este textul de referinta complet, nu numai mesajele recuperate.
- `14089`: alternante RY / TEST DE W6IWI. Fara o transcriere completa cu inceputul/finalul si
  numarul de repetitii verificate, **CER si numarul de mesaje integrale sunt n/a**. Se raporteaza
  aparitiile exacte ale indicativului, textul si costul procesarii. Textul decodat nu devine propria referinta.
- `noise`: 120 s la 8 kHz, mono PCM16, seed xorshift32 12345, suma a 12 uniforme centrata si
  scalata cu 0.03. Este o aproximare gaussiana determinista, nu o colectie exhaustiva de QRN.
  Referinta este sirul gol; CER nu are numitor, deci se raporteaza caractere/minut. Se adauga
  Chebyshev/MF cu SQL oprit pentru a face vizibil efectul SQL.

Acord fix pe tonul masurat: 2123 / 2048 / 2120 Hz; 1275 Hz la sample. Tracking Off, NORM,
US-TTY si restul setarilor exacte apar in fiecare JSON. Profilurile UI pastreaza AFC/PLL si
alfabetul ales de utilizator; pentru a reproduce benchmark-ul trebuie setate ca mai sus.

## Interpretarea rezultatelor

| Camp | Ce masoara |
|---|---|
| CER | Levenshtein / lungimea referintei, dupa majuscule si uniformizarea whitespace; punctuatia se pastreaza. Include insertiile dintre mesaje. |
| Mesaje integrale gasite | Cate mesaje de referinta apar intacte in textul normalizat; nu pretinde ca randurile din UI au aceleasi delimitari. |
| Indicativ corect | Aparitii exacte delimitate ale indicativului asteptat. Nu este recall per transmisie: repetitiile pot afecta numarul. |
| Alte candidate | Forme recunoscute de HamCallsign diferite de indicativul asteptat; nu confirmari ale unor statii reale. |
| Viteza | Durata audio / timpul de decodare si Flush; exclude incarcarea modelului. |
| Memorie | PeakWorkingSet per proces, inclusiv model, recunoasterea indicativelor si scoring. |
| Primul text | Timp de ceas si cantitate de audio deja alimentata la primul eveniment TextReceived. La alimentare accelerata nu reprezinta latenta pe caracter. |
| Coada maxima | Audio inca neprocesat, esantionat dupa callback-urile audio; nu include coada textului provizoriu. |

JSON-urile includ si hash-urile audio/referinta, commit, runtime/OS, timpi CPU/incarcare,
setari si overruns. TXT-urile pastreaza iesirea integrala. Rapoartele Markdown sunt rezumate;
nu se compara direct cu tabele istorice cu alta normalizare, acord sau SQL.

## FSK: software si verificare fizica

`fsk-timing` foloseste **ceasul real** si un IFskOutput de masurare, fara hardware. Trimite 48
caractere RY, in repaus si cu pana la 4 fire CPU ocupate. CSV-ul pastreaza cele 336 intervale
per proba; stopul este comparat cu 1.5 biti. Bit nominal: 22.0022 ms; stop: 33.0033 ms.
Se publica mediana, P95 si maximul erorii absolute. Sarcina extrema poate produce jitter mare;
masuratoarea software nu garanteaza temporizarea USB/serial sau a statiei.

Pentru validare pe echipament (neexecutata in CI):

1. Folositi un analizor/osciloscop cu interfata adecvata nivelurilor electrice ale iesirii FSK.
2. Masurati o secventa RY la pinul fizic, cu receptia radio verificata separat; decodorul logic
   trebuie configurat la 45.45 baud, 5 biti LSB intai, 1.5 stop, polaritatea interfetei.
3. Repetati in repaus si cu procesare audio/CPU activa. Comparati distributia duratelor cu CSV-ul
   software si verificati textul recuperat. Un front fizic poate acoperi mai multi biti egali;
   nu trebuie comparat automat cu un singur apel SetLine.
4. Verificati STOP in mijlocul unui caracter, deconectarea interfetei si reluarea emisiei.
   Fara o cale fizica functionala, o incercare software de dezactivare PTT nu confirma starea pinului.

Testele cu ceas virtual acopera bitii si polaritatea, anularea fara ecou fals, exceptii la
inceputul/sfarsitul ceasului, deconectare simulata, coada in pauza finala si STOP/repornire.
