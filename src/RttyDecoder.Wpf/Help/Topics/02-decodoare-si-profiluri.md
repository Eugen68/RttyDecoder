# Decodoare, modelul DeepRTTY si profiluri

Programul are trei decodoare, alese din lista **Decodor**:

| Decodor | Cum lucreaza | Cand il folosesti |
|---|---|---|
| **Chebyshev** (implicit) | Filtre pe cele doua tonuri si decizie bit cu bit | Bun la flutter si fading rapid; rapid, fara intarziere |
| **Matched Filter** | Integreaza un bit intreg pe fiecare ton, cu normalizarea tonurilor | Cel mai bun clasic pe semnal slab si fading lent; slab la flutter rapid |
| **DeepRTTY** | Model neural (ONNX) care citeste ferestre de audio | Cele mai putine erori pe semnal slab si QRN; textul vine cu intarziere |

## Modelul DeepRTTY

- Modelul (`model_rtty_2026-10.onnx`) e **inclus in program**; nu se descarca nimic. Se incarca la prima alegere a decodorului DeepRTTY ("DeepRTTY: se incarca modelul...").
- Textul **gri** de sub terminal e provizoriu; se confirma (verde, in terminal) dupa aproximativ **8-12 secunde**. E normal: modelul are nevoie de context.
- Modelul primeste **audio-ul original**, prin propriul front-end. NB, DNR si latimea filtrului schimba doar masurarea SNR si urmarirea AFC/PLL, nu intrarea modelului.
- SQL, SNR-G, ATC, Volant si Sensibilitate nu modifica textul neural (DeepRTTY are filtrul lui de zgomot). NORM/REV si US-TTY se aplica.
- Sub terminal apare cat audio asteapta decodarea si cate recuperari dupa suprasarcina au fost (daca procesorul nu tine pasul pe live, decodorul renunta la audio-ul vechi si reia de la semnalul nou).
- Daca modelul nu se poate incarca, programul revine pe Chebyshev si spune de ce.

## Profiluri de receptie

Lista **Profil** pune dintr-o data reglajele potrivite unei situatii. Sunt puncte de pornire masurate pe inregistrari reale (W6IWI), nu o alegere automata:

| Profil | Decodor | Utilizare |
|---|---|---|
| Semnal bun · rapid | Matched Filter | Acord bun, fading lent, fara intarzierea modelului |
| Semnal slab · DeepRTTY | DeepRTTY | Cele mai putine erori pe QRN; confirmare dupa ~8-12 s |
| QRN / flutter · rapid | Chebyshev | Alternativa clasica rapida la flutter |

Toate pornesc cu SQL 35%, volant activ si pragurile implicite. Profilul **pastreaza** acordul (Mark), NORM/REV, AFC/PLL si ITA2/US-TTY. Daca modifici de mana un reglaj, profilul devine **Personalizat**.

NB si ATC nu sunt pornite de niciun profil: pe proba QRN masurata au iesit mai slab decat Chebyshev simplu. Le poti porni de mana pentru alt tip de interferenta.
