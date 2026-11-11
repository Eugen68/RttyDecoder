# Emisie FSK / AFSK si macro-uri

Metoda se alege in **Setari > Setari transmisie**:

- **FSK** - statia conectata (Setari > Conexiune statie) si in modul **RTTY-L sau RTTY-U**; PTT-ul si pinul FSK se aleg tot acolo. Tonurile si shift-ul le da statia (2125 / 2295 Hz).
- **AFSK** - programul genereaza tonurile pe placa de sunet, statia in **DATA-U / USB** (sau DATA-L / LSB). PTT-ul merge prin CAT. Vezi mai jos.

## AFSK (de exemplu IC-7300)

- **Emiti unde asculti**: tonurile Mark/Space sunt chiar perechea pe care decodezi (A-TUNE sau click pe waterfall). Liniile verzi **TX** din waterfall stau pe pereche. Nu e nevoie de QSY - QSY-ul automat nu se foloseste la AFSK.
- **Polaritatea pe aer** e cea standard (Mark = frecventa RF mai mare), calculata din modul statiei: in DATA-U / USB Mark e tonul de sus, in DATA-L / LSB tonul de jos - indiferent de NORM/REV. Daca statia nu raporteaza modul, emisia urmeaza NORM/REV de la receptie. Bifa **Inverseaza polaritatea** schimba tonurile intre ele.
- Tonurile se genereaza pe ceasul placii de sunet, cu faza continua: viteza exacta 45.45 baud, fara clicuri.
- **Placa de iesire**: placa audio a statiei (la IC-7300: "USB Audio CODEC"). **Nivel**: incepe cu 50% si coboara pana cand ALC-ul nu mai misca. Prea mult = splatter.
- **Pe statie**:
  - **IC-7300 / IC-7300MK2**: modul **USB-D** (DATA), meniul SET > Connectors > MOD Input: **DATA MOD = USB**, USB MOD Level ~50%. Conexiunea CAT prin Hamlib (model 3073 / 3094).
  - **FTDX10 / FTX-1**: modul **DATA-U**, audio de date de pe USB (FTDX10: MODE PSK/DATA > DATA MOD SOURCE = REAR si REAR SELECT = USB).
  - Numele meniurilor pot diferi dupa firmware - verifica in manualul statiei.
- PTT-ul cere statia conectata (Setari > Conexiune statie); manipularea FSK nu mai trebuie.
- Daca placa de sunet e scoasa in timpul emisiei, emisia se opreste singura si PTT-ul cade.

## Macro-urile F1..F8

- Butoanele de sub terminal, sau tastele **F1..F8**.
- **Mouse-ul deasupra** arata textul exact care pleaca, cu variabilele inlocuite (si ce lipseste).
- **Click dreapta** = editare (nume si text).
- Variabile:

| Variabila | Se inlocuieste cu |
|---|---|
| `{MYCALL}` | Indicativul tau (campul "Indicativul meu" din lista statiilor) |
| `{CALL}` | Ultimul indicativ pe care ai dat click (terminal sau lista) |
| `{RST}` | 599 |

## Campul de emisie si LIVE

- **Enter** = trimite textul scris.
- **LIVE**: emisia sta pornita si fiecare caracter pleaca imediat ce il scrii (intre ele, diddle = LTRS).
- Mesajele trimise in timpul unei emisii se adauga la coada (un singur PTT).
- **STOP** sau **Esc** opreste imediat.
- Textul trimis apare in terminal cu galben. Cat timp emiti, decodarea sta.

## Ora in terminal

Fiecare mesaj incepe pe un rand nou cu `[HH:mm:ss]` UTC - verde la receptie, rosu la emisie. Mesaj nou = dupa o pauza de cel putin 5 s, la trecerea receptie/emisie si dupa orice schimbare de acord.

## Setari > Setari transmisie

| Setare | Implicit | Ce face |
|---|---|---|
| Preambul | 2 LTRS | Caractere trimise dupa PTT, inainte de text (sincronizarea receptoarelor) |
| Pauza la sfarsit | 300 ms | Pe Mark, inainte de oprirea PTT |
| Limita unei emisii | 180 s | Peste ea emisia se opreste singura |
| CR/LF automat | Da | La inceputul si sfarsitul emisiei |
| Diddle | Da | In LIVE, LTRS continuu cand nu scrii |
| QSY automat inainte de raspuns | Nu | Doar FSK. Vezi subiectul A-TUNE, polaritate si QSY |
| Inverseaza polaritatea | Nu | FSK: pin activ = Space, doar pentru interfete care inverseaza linia (altfel polaritatea TX din meniul statiei). AFSK: Mark pe celalalt ton |
| Metoda | FSK | FSK sau AFSK |
| Placa de iesire (AFSK) | implicita | Placa audio a statiei |
| Nivel (AFSK) | 50% | Pana cand ALC-ul nu mai misca |
