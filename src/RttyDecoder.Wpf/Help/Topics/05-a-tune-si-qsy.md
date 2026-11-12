# A-TUNE, polaritate si QSY

## A-TUNE (un click)

- Gaseste cea mai buna **pereche** de tonuri la 170 Hz, ambele peste zgomot (o purtatoare singura nu e luata drept semnal RTTY).
- Stabileste **care ton e Mark**, pe ultimele ~5 s de audio: intai dupa **durate** (bitul de stop are 1,5 biti si e mereu pe Mark, deci doar Mark-ul are rulari de x,5 biti), iar daca duratele nu decid, dupa **incadrarea caracterelor** (start pe Space, stop pe Mark).
- Pune decodorul pe Mark-ul real si **NORM/REV dupa semnal**. Daca nu e sigur (semnal scurt sau slab), lasa NORM/REV cum era si spune "polaritate nesigura".
- In subsidiar calculeaza **frecventa statiei pentru emisie** si o arata in mesaj si langa VFO (`TX: 3.505.200`).

## Dublu click pe A-TUNE = QSY

Muta statia pe frecventa calculata: perechea corespondentului ajunge pe **2125 / 2295 Hz**, deci emisia ta FSK pleaca exact pe frecventa lui. Decodorul trece pe Mark 2125 (NORM) sau 2295 (REV).

Daca un A-TUNE a calculat deja frecventa (o vezi la `TX:`), dublu click-ul duce statia **exact acolo**, chiar daca intre timp corespondentul a terminat emisia: primul click al dublu click-ului nu mai inlocuieste calculul afisat. Pentru alt semnal: click pe el in waterfall (sau A-TUNE simplu), apoi dublu click. Fara un calcul afisat, dublu click-ul face A-TUNE si QSY dintr-o data (cat semnalul e pe aer).

De ce se muta statia, nu decodorul: la FSK statia emite exact unde e acordata, cu tonurile fixe 2125 / 2295. La **AFSK** nu e nevoie de QSY: emisia pleaca pe perechea decodorului, deci A-TUNE singur e destul.

## RTTY-L si RTTY-U

- O statie de pe aceeasi frecventa se aude pe 2125 / 2295 in **ambele** moduri.
- In **RTTY-L**, Mark-ul e tonul de jos (2125) - NORM.
- In **RTTY-U**, Mark-ul e tonul de sus (2295) - de aceea receptia cere **REV**.
- QSY-ul aliniaza **perechea** (tonul de jos pe 2125), deci merge corect in ambele moduri. RTTY-L se comporta ca LSB, RTTY-U ca USB.

## Polaritatea emisiei

NORM/REV e **doar pentru receptie**. Emisia nu depinde de NORM/REV si nici de modul statiei - testat: FTDX10 si FTX-1 emit la fel in RTTY-L si RTTY-U, IC-7300MK2 la fel in RTTY si RTTY-R; modurile U (Yaesu) si RTTY-R (Icom) inverseaza doar receptia.

Polaritatea **pinului FSK** tine de statie (Setari > Conexiune statie > Inverseaza polaritatea FSK): Yaesu ia pinul activ ca Mark (nebifat), Icom pe setarile din fabrica ca Space (bifat). Cu bifa potrivita, ambele emit standard (Mark sus) - testat FTDX10 <-> IC-7300MK2.

## QSY automat inainte de raspuns

Optiune in **Setari > Setari transmisie** (implicit oprita). Cand e bifata, la un macro sau text care **nu incepe cu CQ**, daca ultimul A-TUNE a calculat alta frecventa decat a statiei (statia in RTTY-L/U), statia se muta intai acolo si emisia pleaca dupa ~250 ms.

Nu se muta statia:
- la **CQ** (chemi pe frecventa ta - si se uita QSY-ul pregatit);
- in **LIVE**;
- dupa un **click pe alt semnal** in waterfall (calculul vechi se uita);
- daca statia s-a mutat de la A-TUNE;
- in fereastra **Contest**, in modul **Run** (frecventa de Run nu trebuie sa alunece). In S&P se muta.

## Shift+click pe waterfall

QSY direct: statia se muta ca semnalul de sub mouse sa ajunga pe Mark-ul decodorului.
