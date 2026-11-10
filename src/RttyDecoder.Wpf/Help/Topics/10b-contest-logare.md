# Contest: logare si jurnal

## Formularul

- **Indicativ**: click pe un indicativ in terminal sau in lista statiilor (cu fereastra Contest deschisa), sau scris de mana.
- **Frecventa RF** in kHz: cu **CAT** bifat se ia de la statie. Fara statie, debifeaza CAT si scrie frecventa. Nu se foloseste frecventa audio Mark/Space.
- **RST primit**, **Nr. primit**, **Schimb primit**, plus campurile suplimentare (nume, diverse, sectiune, locator, prefix WPX, comentariu), dupa cum le cere definitia.
- **Ora UTC**: goala = momentul logarii; altfel `yyyy-MM-dd HH:mm:ss`.
- Mesajul de sub formular (si **Verifica**) arata erorile, dublele si multiplicatorii noi.

## Preluarea din terminal

Click pe un cuvant din textul decodat (care nu e indicativ):

| Ce dai click | Unde ajunge |
|---|---|
| `599` sau `5NN` | RST primit |
| Un numar | Nr. primit (daca are seriale si campul e gol), altfel Schimb primit |
| Un cuvant (sectiune, nume, zona) | Schimb primit |

Cuvintele uzuale (DE, TU, K, CQ, TEST, NR, 73...) sunt ignorate, la fel ora si textul trimis de tine. Sub mouse, cuvintele care se pot prelua se evidentiaza si cursorul devine mana. Preluarea nu transmite nimic. Un serial chiar egal cu 599 se scrie de mana.

Pentru **RTTYops WW DX**: daca indicativul are acelasi an in toate QSO-urile anterioare din sesiune, formularul arata anul si **Preia anul** il copiaza in Schimb primit (doar daca e gol). Verifica anul receptionat inainte de logare.

## Logarea

- **Enter** cu **ESM** bifat: vezi subiectul despre macro-uri si emisie. Fara ESM, Enter doar logheaza.
- **Logheaza QSO** salveaza QSO-ul. Un **dublu** ramane in jurnal, cu zero puncte; nu e sters automat.
- Salvarea e sigura: intai se scrie o copie, iar daca scrierea esueaza nimic nu avanseaza. La o eroare de salvare mesajul ramane vizibil, iar inchiderea ferestrei si a aplicatiei e oprita, ca sa nu pierzi QSO-uri.

## Jurnalul

Click dreapta pe un QSO:

| Comanda | Ce face |
|---|---|
| Look-up QRZ.com | Pagina indicativului pe QRZ.com, in browser |
| Corecteaza selectat... | QSO-ul trece in formular; dupa modificari, **Salveaza corectia**. Serialul deja transmis nu se schimba. O corectie nesalvata nu inlocuieste QSO-ul original. |
| Sterge selectat... | Cere confirmare; scorul si dublele se recalculeaza. |

Dublele apar in jurnal cu galben. Sus: ceasurile **UTC** si **LOCAL**; dedesubt, totalul: QSO-uri, duble, puncte, multiplicatori, scor.
