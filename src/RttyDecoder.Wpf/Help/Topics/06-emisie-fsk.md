# Emisie FSK si macro-uri

Statia trebuie conectata (Setari > Conexiune statie) si in modul **RTTY-L sau RTTY-U**; PTT-ul si pinul FSK se aleg tot acolo.

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
| QSY automat inainte de raspuns | Nu | Vezi subiectul A-TUNE, polaritate si QSY |
| Inverseaza pinul FSK | Nu | Doar pentru interfete care inverseaza linia; altfel polaritatea TX din meniul statiei |
