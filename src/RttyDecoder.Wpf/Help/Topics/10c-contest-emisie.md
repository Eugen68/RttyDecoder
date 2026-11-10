# Contest: macro-uri, seriale si emisie

## Tastele F1-F4

**S&P** debifat = **Run**; bifat = **Search & Pounce**. Fiecare mod are setul lui de macro-uri:

| Tasta | Run | S&P |
|---|---|---|
| F1 | CQ TEST | Indicativul tau (se poate repeta oricand) |
| F2 | Raportul si schimbul, cu serialul fixat | Schimbul tau |
| F3 | AGN AGN PSE | AGN AGN PSE |
| F4 | TU | TU |
| Esc | STOP emisie | STOP emisie |

- **Mouse-ul deasupra** unui buton F arata textul care pleaca (serialul e estimat pana la fixare).
- **Click dreapta** pe un buton F = editare, separat pentru Run si S&P.
- Macro-urile se salveaza langa definitie (`nume.macros.json`); daca lipsesc, se creeaza cu seturile implicite.
- Variabile: `{MYCALL}` indicativul sesiunii, `{CALL}` indicativul corespondentului, `{RST}` raportul trimis, `{EXCHANGE}` serialul plus schimbul fix.

## Enter (ESM)

Cu bifa **ESM** (implicit), Enter in formular trimite mesajul potrivit starii:

| Mod | Starea | Enter |
|---|---|---|
| Run | fara indicativ | CQ (F1) |
| Run | indicativ, fara schimb primit | Raportul (F2); repetat = acelasi serial |
| Run | schimbul primit completat | Logheaza, apoi TU (F4) |
| S&P | fara schimb primit | Indicativul tau (F1) |
| S&P | schimbul primit completat | Schimbul tau (F2) |
| S&P | schimbul trimis | Logheaza (fara emisie) |

Cursorul trece singur la campul urmator. Cu ESM debifat, Enter doar logheaza. TU apasat separat nu logheaza.

## Serialele

- **F2** (sau **Fixeaza QSO / serial**) rezerva numarul si il **salveaza pe disc inainte de emisie**. Repetarea foloseste acelasi numar, chiar si dupa repornirea aplicatiei.
- Indicativul si schimbul transmis raman blocate pana la logare sau **Anuleaza**.
- Un serial anulat sau sters nu se refoloseste (poate fi fost deja transmis).

## Emisia

- Foloseste configuratia FSK/CAT a aplicatiei, numai cu statia conectata in **RTTY-L sau RTTY-U**.
- **QSY automat** (daca e bifat in Setari transmisie) se face **doar in S&P**: statia merge pe frecventa calculata de A-TUNE. In Run statia ramane pe frecventa ta.
- Mesajele Contest nu se pun in coada peste o emisie activa: asteapta sfarsitul sau apasa STOP.
- Macro-urile F1-F8 ale ferestrei principale nu sunt atinse.
