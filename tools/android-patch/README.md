# Patch-uri pentru aplicatia Android (Yaesu-FTX1-CAT-Control)

Repo-ul Android il intretine Eugen; aici sunt doar propuneri, de aplicat cu `git apply` in radacina lui.

| Patch | Ce face | Stare |
|---|---|---|
| `RttyDemodulator.patch` | `RttyDemodulator.java` de la versiunea initiala pana la `65fd101` (AFC, Matched Filter, volant) | aplicat deja in Android (`refactorizare_claude_2`); pastrat ca referinta |
| `DeepRtty.patch` | DeepRTTY: textul gri fara gol si resetarea ca marcaj in coada | propus, de aplicat |

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
