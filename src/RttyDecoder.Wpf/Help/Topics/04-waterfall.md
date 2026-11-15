# Waterfall

## Mouse si taste

| Comanda | Efect |
|---|---|
| Click | Acordul decodorului pe frecventa indicata (cu **Atragere pe semnal**: cauta perechea RTTY apropiata) |
| Click pe un triunghi de sus | Acord pe perechea RTTY marcata |
| Rotita / Ctrl+rotita | Acord fin: 1 Hz / 10 Hz |
| Alt+rotita | Zoom in jurul cursorului |
| Alt+tragere sau butonul din mijloc | Deplasare in frecventa |
| Shift+click | QSY: statia se muta ca semnalul de sub mouse sa ajunga pe Mark (numai cu statia conectata) |

## Ce vezi pe imagine

- **M** (rosu) si **S** (portocaliu): tonurile Mark si Space ale decodorului.
- **TX** - doua linii punctate verzi pe 2125 / 2295 Hz, cu statia in RTTY-L / RTTY-U: acolo pleaca emisia ta FSK. Daca perechea corespondentului nu e pe ele, raspunsul iese alaturi - vezi subiectul **A-TUNE, polaritate si QSY**. La **AFSK** (statia in DATA-U / USB) liniile stau pe perechea decodorului: emiti unde asculti.
- **Triunghiuri albastre** sus: perechi RTTY gasite in banda (doua tonuri la 170 Hz, ambele peste zgomot). Mouse-ul deasupra arata tonurile si nivelul; click = acord. Bifa **Semnale** le ascunde.
- **Zona filtrului**: benzi slab colorate in jurul M si S, pe latimea filtrelor (doar cu Chebyshev).
- Sub cursor: frecventa audio, cu statia conectata si **frecventa RF** (in RTTY, pe un Mark = frecventa corespondentului), vechimea in secunde si nivelul in dBFS.
- Separatoare orizontale: START/STOP, sursa noua, acord/QSY, TX/RX. Golurile (cadre pierdute, emisie) raman negre, cu durata lor.

## Controalele de sub sageata

Sageata **Afisare waterfall** de sub imagine deschide reglajele:

- **0–4 kHz**, **Semnal**, **+ / −**: vedere generala, zoom pe Mark/Space sau zoom; istoricul ramane.
- **Istoric** 8/15/30 s, **Urmeaza** (in zoom, centrul urmareste acordul), **Pauza imagine** (receptia continua), **PNG...** (salveaza imaginea).
- **FFT**: Rapid · 512 (15,6 Hz, 64 ms) sau Detaliat · 1024 (7,8 Hz, 128 ms). Detaliat separa mai bine tonurile, dar raspunde mai incet.
- **Podea** (Auto urmareste zgomotul), **Fixeaza nivel**, **Interval** (cat de larga e scala de culori), **Gama** (peste 1 intuneca semnalele slabe, sub 1 le evidentiaza), **Paleta**, **Spectru**, **Semnale**, **Atragere pe semnal**, **Inaltime**.

Scara e amplitudine spectrala **dBFS** (o sinusoida de amplitudine 0,5 = aproximativ -6 dBFS), nu nivelul RMS al intregului audio.
