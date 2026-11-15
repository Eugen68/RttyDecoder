# Emisie FSK / AFSK si macro-uri

Metoda se alege in **Setari > Setari transmisie**:

- **FSK** - statia conectata (Setari > Conexiune statie) si in modul **RTTY-L sau RTTY-U**; PTT-ul si pinul FSK se aleg tot acolo. Tonurile si shift-ul le da statia (2125 / 2295 Hz).
- **AFSK** - programul genereaza tonurile pe placa de sunet, statia in **DATA-U / USB** (sau DATA-L / LSB). PTT-ul merge prin CAT. Vezi mai jos.

## AFSK (de exemplu IC-7300)

- **Emiti unde asculti**: tonurile Mark/Space sunt chiar perechea pe care decodezi (A-TUNE sau click pe waterfall). Liniile verzi **TX** din waterfall stau pe pereche. Nu e nevoie de QSY - QSY-ul automat nu se foloseste la AFSK.
- **Polaritatea pe aer** e cea standard (Mark = frecventa RF mai mare), calculata din modul statiei: in DATA-U / USB Mark e tonul de sus, in DATA-L / LSB tonul de jos - indiferent de NORM/REV. Daca statia nu raporteaza modul, emisia urmeaza NORM/REV de la receptie. Bifa **AFSK: inverseaza tonurile** (Setari transmisie) le schimba intre ele.
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

Ora UTC sta pe un rand al ei, textul incepe pe randul de dedesubt (**albastra** la receptie, rosie la emisie; textul decodat ramane verde, cel trimis galben):

```
[12:34:56] [3505.20]:
CQ CQ DE YO9VA YO9VA K
```

- **Schimbare de acord / frecventa** (click sau rotita pe waterfall, A-TUNE, NORM/REV, orice QSY, VFO-ul statiei; nu deriva AFC): ora si **frecventa Mark-ului pe RF** in kHz, inaintea primului text decodat dupa schimbare - doar daca Mark-ul s-a mutat cu **cel putin 10 Hz** fata de ultima ora cu frecventa (un click mic sau A-TUNE pe acelasi semnal nu pun ora). Fara statie conectata doar ora: `[12:34:56]:` (pragul de 10 Hz se aplica tonului audio).
- **Periodic, la :00 si :30** din fiecare minut, fara sa rupa traficul: daca la :00 / :30 se decodeaza, asteapta o **pauza de 3 s**; daca in **10 s** nu vine nicio pauza, trece oricum pe rand nou. Daca nu se decodeaza nimic, nu se pune nimic (ora vine in fata primului text). O ora pusa cu mai putin de 15 s inainte (frecventa noua, emisie) o inlocuieste pe cea periodica.
- **Trecerea receptie / emisie**: primul text dupa schimbare primeste ora.
- **Emisie**: fiecare emisie noua (dupa o pauza de cel putin 5 s) incepe cu ora.
- Dupa un mesaj al aplicatiei, urmatorul text primeste ora.

## Derularea terminalului

Poti derula in urma (rotita, bara) ca sa citesti ce s-a decodat; textul nou nu te muta de acolo. Dupa **15 s fara derulare**, terminalul revine singur la capat, la textul nou (nu cat tii butonul mouse-ului apasat, de exemplu la o selectie).

## Setari > Setari transmisie

| Setare | Implicit | Ce face |
|---|---|---|
| Preambul | 2 LTRS | Caractere trimise dupa PTT, inainte de text (sincronizarea receptoarelor) |
| Pauza la sfarsit | 300 ms | Pe Mark, inainte de oprirea PTT |
| Limita unei emisii | 180 s | Peste ea emisia se opreste singura |
| CR/LF automat | Da | La inceputul si sfarsitul emisiei |
| Diddle | Da | In LIVE, LTRS continuu cand nu scrii |
| QSY automat inainte de raspuns | Nu | Doar FSK. Vezi subiectul A-TUNE, polaritate si QSY |
| AFSK: inverseaza tonurile | Nu | Mark pe celalalt ton. Polaritatea pinului FSK e in Conexiune statie (FTDX10 si IC-7300MK2 bifat, FTX-1 nebifat) |
| Metoda | FSK | FSK sau AFSK |
| Placa de iesire (AFSK) | implicita | Placa audio a statiei |
| Nivel (AFSK) | 50% | Pana cand ALC-ul nu mai misca |
