# Contest: export ADIF si Cabrillo

## ADIF

**Unelte > Export ADIF...** salveaza un fisier `.adi` (ADIF 3.1.8), cu locatia sugerata in `Documente\RttyDecoder\Logs`.

- Contine: ora UTC, frecventa in MHz, banda, modul RTTY, RST, seriale, schimburile trimis/primit, nume, locator, comentariu si indicativul propriu.
- Include si QSO-urile duble.
- Campurile ADIF sunt ASCII: caracterele cu diacritice sunt refuzate explicit (nu transformate pe ascuns). Jurnalul `.contest.json` pastreaza textul original.

## Cabrillo

**Unelte > Export Cabrillo...** foloseste sablonul din definitia UDC (CabrilloFormat=99).

In **Antete Cabrillo** completeaza categoria ta reala, cate o linie `CHEIE: valoare`. Exemplu de sintaxa, de adaptat:

```
CATEGORY-OPERATOR: SINGLE-OP
CATEGORY-BAND: ALL
CATEGORY-POWER: LOW
CATEGORY-ASSISTED: NON-ASSISTED
CATEGORY-TRANSMITTER: ONE
OPERATORS: YO9VA
```

- CATEGORY-OPERATOR, -BAND, -POWER, -ASSISTED si -TRANSMITTER sunt obligatorii; CATEGORY-MODE se pune singur RTTY.
- Antetele cerute in plus de organizator (nume, adresa, club etc.) le adaugi tu.
- Nu se inventeaza o categorie si nu se trunchiaza un schimb mai lung decat sablonul - exportul se opreste cu un mesaj.
- Exportul nu inseamna verificarea tuturor regulilor organizatorului.
