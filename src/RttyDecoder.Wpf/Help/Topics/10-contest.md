# Contest

**Contest > Deschide fereastra de concurs** sau **Ctrl+Shift+C**. Fereastra e separata si nemodala: decodorul ramane folosibil. Detaliile complete sunt in meniul **Ajutor** al ferestrei Contest.

## Pe scurt

1. Alege **UDC-ul** (definitia concursului), indicativul propriu si primul serial; completeaza sectiunea/locatorul daca regulamentul le cere.
2. **Incepe sesiunea**, apoi completeaza **Schimbul fix** cerut de regulament.
3. Click pe un indicativ in terminal sau in lista statiilor il trimite in formular. **Click pe un numar sau un cuvant** din terminal il preia in Nr. primit / Schimb primit (`599`/`5NN` = RST; cuvintele uzuale DE, TU, K, CQ... sunt ignorate).
4. **F2** fixeaza serialul (salvat pe disc **inainte** de emisie) si trimite raportul; repetarea foloseste acelasi serial, chiar si dupa repornire.
5. Completeaza schimbul primit si logheaza (vezi Enter / ESM mai jos).

## Enter (ESM)

Cu bifa **ESM** (implicit), Enter in formular trimite mesajul potrivit:

| Mod | Starea | Enter |
|---|---|---|
| Run | fara indicativ | CQ (F1) |
| Run | indicativ, fara schimb primit | Raportul (F2); repetat = acelasi serial |
| Run | schimbul primit completat | Logheaza, apoi TU (F4) |
| S&P | fara schimb primit | Indicativul tau (F1) |
| S&P | schimbul primit completat | Schimbul tau (F2) |
| S&P | schimbul trimis | Logheaza (fara emisie) |

Cursorul trece singur la campul urmator. Cu ESM debifat, Enter doar logheaza.

## Culori in decodor

Cat e deschisa fereastra Contest, indicativele din terminal si din lista statiilor arata starea lor in sesiune, pe banda curenta:

| Culoare | Inseamna |
|---|---|
| Gri, taiat | Dublu - zero puncte |
| Roz | Multiplicator nou |
| Culorile obisnuite | QSO valid |

Multiplicatorii care vin din indicativ (tara, prefix, continent) se vad inainte de QSO; cei din schimb (sectiune, zona primita) abia la logare.

## Tastele in fereastra Contest

| Tasta | Run (S&P debifat) | S&P bifat |
|---|---|---|
| F1 | CQ TEST | Indicativul propriu (se poate repeta oricand) |
| F2 | Raport + schimb, cu serialul fixat | Schimbul |
| F3 | AGN AGN PSE | AGN AGN PSE |
| F4 | TU | TU |
| Esc | STOP TX | STOP TX |

Macro-urile Run si S&P se editeaza separat (click dreapta pe F1-F4) si se salveaza langa UDC (`nume.macros.json`).

## De retinut

- **QSY automat** se face doar in **S&P**; in Run frecventa ta ramane pe loc.
- Un dublu ramane in jurnal cu zero puncte; nu e sters automat.
- Serialele anulate sau sterse nu se refolosesc.
- Daca salvarea jurnalului esueaza, inchiderea aplicatiei e oprita, ca sa nu pierzi QSO-uri.
- **Unelte > Export ADIF / Cabrillo**. La Cabrillo completeaza antetele reale ale categoriei tale (CATEGORY-...).
- Fisierele concursului: vezi subiectul **Fisiere si foldere**.
