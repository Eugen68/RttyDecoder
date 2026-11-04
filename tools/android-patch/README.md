# Patch-uri pentru aplicatia Android (Yaesu-FTX1-CAT-Control)

Repo-ul Android il intretine Eugen; aici sunt doar propuneri, de aplicat cu `git apply` in radacina lui.

| Patch | Ce face | Stare |
|---|---|---|
| `RttyDemodulator.patch` | `RttyDemodulator.java` de la versiunea initiala pana la `65fd101` (AFC, Matched Filter, volant) | aplicat deja in Android (`refactorizare_claude_2`); pastrat ca referinta |
| `DeepRtty.patch` | DeepRTTY: textul gri fara gol si resetarea ca marcaj in coada | aplicat in Android (`9910082` pe `refactorizare_claude_2`) |
| `fsk-rtty-u.patch` | emisia FSK inversata in RTTY-U | **respins** - testul pe statii arata ca emisia era deja corecta (vezi mai jos) |

## DeepRtty.patch

Se aplica pe `refactorizare_claude_2` (`65fd101`) si pe `master` (`ea922b9`) - fisierele DeepRTTY nu
s-au mai schimbat de la `7c2c705`. Atinge doar Kotlin-ul DeepRTTY si testul lui; API-ul
`DeepRttyReceiver` (porneste/opreste/reseteaza/alimenteaza/listener) ramane acelasi, deci
`EcranRttyDroid.java` nu se schimba.

1. **`DeepRttyStream.preview()`** - coada neconfirmata (textul gri) era doar fereastra de final de
   12 s; tokenii ultimei ferestre complete dintre textul confirmat si inceputul ferestrei de final
   (pana la ~4 s) nu apareau nicaieri pana la urmatoarea confirmare. Acum coada = tokenii inca
   neconfirmati ai ultimei ferestre (`OverlapMerger.pendingTokens`, getter nou) pana la zona sigura a
   ferestrei de final, plus fereastra de final de acolo incolo.
2. **`DeepRttyReceiver`** - resetul era un flag partajat, iar firul de decodare golea apoi toata
   coada, inclusiv blocurile sosite *dupa* reset (la pornire si la fiecare reacordare). Acum resetul
   e un marcaj in coada, in ordine cu audio-ul; la pornire stream-ul se reseteaza direct.

Verificat (proiect JVM separat, Kotlin 1.9.24, onnxruntime 1.27.0, cu inlocuitori pentru
Context/Log/SystemClock/InferenceWrapper):
- test nou `DeepRttyTest.coadaNeconfirmataFaraGol` (semnalul taiat la inceput cu 0-1.5 s, ca sa mute
  grila ferestrelor): pe codul vechi pica deja la 0 s ("...BROWN FOQWERTYUIOP" - se pierde
  "X JUMPS OVER THE LAZY DOG" si shift-ul FIGS), cu patch-ul trece la toate;
- `DeepRttyTest` existent (NORM -3/-6/-9 dB, REV -6 dB) si `RttyDemodulatorTest` neschimbate;
- receptorul complet (doar in scratchpad, nu se poate testa in testele unitare Android): pornire cu
  tot audio-ul imediat, in ritm, si reset la mijloc - pe codul vechi textul confirmat + gri pierdea
  "JUMPS OVER THE LAZY DOG" in 7 din 7 rulari, cu patch-ul 7 din 7 complete. Cursa de la pornire nu
  s-a putut provoca pe Linux (in C# aparea doar pe Windows); reparatia ei e prin constructie.

## fsk-rtty-u.patch - RESPINS (test pe statii, 2026-11-04)

### Ipoteza patch-ului

Emisia FSK din aplicatie ar fi buna in RTTY-L, dar in RTTY-U statia ar inversa tonurile (Mark pe
frecventa RF mai mica), deci receptorul ar trebui sa dea REV. Patch-ul inversa pinul FSK cand statia e
in RTTY-U: `polaritate = NORM/REV XOR RTTY-U` (modul din raspunsul CAT `MD0x`, x = `9`), in
`trimiteCodBaudotMetronom` si la idle-ul pe Mark fara diddle (`EcranRttyDroid.java`). Patch-ul ramane
aici doar ca referinta; **nu se aplica**.

### Testul (Eugen, YO9VA)

- FTDX10: emisie si receptie direct cu statia (tastatura/decodorul intern);
- FTX-1: emisie (pin FSK) si receptie cu aplicatia YaesuCATControlADV (FTX-1 nu are decodor intern);
- ambele statii cu setarile default; **toate emisiile pe NORM**; REV s-a pus doar la receptie, unde
  era nevoie.

![Tabelul testului](test-polaritate-fsk-2026-11-04.jpg)

| Emite (NORM) | Receptioneaza | FTDX10 -> FTX-1 + aplicatia | FTX-1 + aplicatia -> FTDX10 |
|---|---|---|---|
| RTTY-L | RTTY-L | OK pe NORM | OK pe NORM |
| RTTY-L | RTTY-U | OK doar cu REV | OK doar cu REV |
| RTTY-U | RTTY-U | OK doar cu REV | OK doar cu REV |
| RTTY-U | RTTY-L | OK pe NORM | OK pe NORM |

### Concluzii

1. **Cele doua tabele sunt identice**: FTX-1 + aplicatia se comporta exact ca FTDX10 singur.
2. **Emisia e corecta in ambele moduri**: la ambele statii semnalul din aer e acelasi in RTTY-L si in
   RTTY-U (receptorul in RTTY-L il citeste pe NORM indiferent de modul emitatorului). Aplicatia nu
   are nevoie de nicio inversare suplimentara la emisie in RTTY-U.
3. **Receptia in RTTY-U cere REV** - la decodorul intern al FTDX10 la fel ca la aplicatie. E
   comportamentul statiilor Yaesu cu setarile default, nu un bug al aplicatiei.
4. `fsk-rtty-u.patch` ar fi **stricat** emisia FTX-1 in RTTY-U pe NORM (ar fi inversat-o). Pe
   combinatia RTTY-U + REV ar fi dat din intamplare semnal corect, dar doar pentru ca anula cuplajul
   de la punctul 5.
5. **Problema reala din aplicatie**: butonul NORM/REV schimba si receptia, si emisia
   (`demod.regimReverseSoftware` e folosit si de `trimiteCodBaudotMetronom`). Cazul tipic: in RTTY-U
   pui REV ca sa citesti statia, raspunzi fara sa treci inapoi pe NORM, iar emisia iese inversata si
   corespondentul nu te mai decodeaza.

**Meniurile statiilor:**
- **FTDX10** are in meniu setari NORM/REV **separate** pentru receptie si pentru emisie (plus decodor
  RTTY intern si butoane de macrouri RTTY);
- **FTX-1** are meniul mai sarac: fara decodor RTTY, fara macrouri RTTY, iar NORM/REV **doar pentru
  emisie**. Receptia RTTY o face numai aplicatia, deci butonul NORM/REV din aplicatie tine locul
  setarii de RX pe care FTX-1 nu o are.

Yaesu trateaza deci polaritatea de RX si de TX separat; aplicatia le leaga de acelasi buton.

**Propunere (inca neaplicata in Android, de discutat):** emisia FSK sa nu mai urmeze butonul
NORM/REV - pinul pleaca mereu cu polaritatea normala (Mark = pin activ); REV ramane doar pentru
receptie. Optional, mai tarziu, o setare separata „TX REV” pentru cazurile rare cand chiar vrei sa
emiti inversat.

### Observatii

- Un test anterior, mai putin sistematic, parea sa arate ca FTDX10 in RTTY-U decodeaza fara REV o
  emisie FTX-1 din RTTY-L; tabelul de mai sus (randul RTTY-L -> RTTY-U cere REV) il contrazice si e
  cel de referinta.
- **RttyDecoder (programul de PC)**: commit-ul `a870049` („Emisia FSK corecta si in RTTY-U",
  `RttyFskTransmitter.RigInvertsTones`) se bazeaza pe aceeasi ipoteza. Testul de mai sus nu a acoperit
  emisia FTDX10 prin pinul FSK al programului de PC, doar emisia directa a statiei; dar daca statia se
  comporta la fel cand e comandata prin pin (cum se vede la FTX-1), si `a870049` inverseaza gresit in
  RTTY-U. **De reverificat pe statie** inainte de a-l pastra (inainte de el, in modul RTTY al statiei
  pinul deja nu urma REV-ul receptiei - adica exact propunerea de mai sus).
