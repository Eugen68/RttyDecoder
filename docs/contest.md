# Modul Contest

**Contest > Deschide fereastra de concurs**, sau **Ctrl+Shift+C**, deschide o fereastra separata,
nemodala. Decodorul ramane disponibil. O singura fereastra Contest este deschisa la un moment
dat; repetarea comenzii o aduce in fata. Inchiderea ei salveaza formularul si nu opreste receptia.

Biblioteca [HamContest](https://github.com/Eugen68/HamContest) este un submodul fixat la commit-ul
`4132ff9` (preview.2, din PR #1 al bibliotecii). Clonati cu submodule:

```sh
git clone --recurse-submodules https://github.com/Eugen68/RttyDecoder.git
```

## Fisiere

| Continut | Folder implicit |
| --- | --- |
| Definitii .udc | `%APPDATA%\YO9VA\RttyDecoder\UserDefinedContests` |
| Liste .sec | `%APPDATA%\YO9VA\RttyDecoder\SupportFiles` |
| Jurnale .contest.json | `%APPDATA%\YO9VA\RttyDecoder\Contests\Sessions` |
| Exporturi, locatie sugerata | `Documente\RttyDecoder\Logs` |

Folderele sunt create automat. **Importa UDC / SEC** copiaza fisierele alese; un fisier diferit
cu acelasi nume primeste un sufix, fara a suprascrie definitia existenta. Nu pastrati doua liste
SEC diferite cu acelasi Type: HamContest raporteaza conflictul; folositi butonul Folder SEC
pentru a administra listele. SEC trebuie sa aiba layout-ul simplu suportat de biblioteca.

DEMO-RTTY.udc si DEMO.sec sunt exemple originale pentru exercitiu, nu un regulament de concurs.
Copiile deja existente nu sunt inlocuite la pornire. Nu sunt descarcate automat definitii de concurs.

## Pornirea si redeschiderea

1. Alegeti UDC-ul, indicativul propriu si primul serial. Completati sectiunea/locatorul propriu
   daca regulamentul le foloseste; prefixul WPX propriu poate fi completat explicit.
2. Apasati **Incepe sesiunea**. Definitia trebuie sa fie suportata si sa permita RTTY sau BOTH.
3. Completati **Schimb fix (fara serial)** cu schimbul real cerut de regulament: zona, an,
   nume/QTH etc. Biblioteca nu poate transforma automat un text precum SeeRules intr-un schimb real.
4. Folositi **Deschide sesiune** sau **Reia ultima** pentru un jurnal salvat. **Nou** pastreaza
   jurnalul curent si revine la configurare. Mai multe sesiuni sunt fisiere independente.

Sesiunea contine textul UDC/SEC original, datele QSO-urilor, regulile identificate prin amprenta,
formularul si rezervarea curenta. Editarea ulterioara a fisierelor din catalog nu schimba regulile
unei sesiuni incepute. Schimbarile incompatibile ale versiunii HamContest necesita migrare explicita.

## Introducere si logare

- Click pe un indicativ in terminal sau lista statiilor il trimite si formularului Contest,
  daca fereastra este vizibila si nu exista un QSO fixat/corectie in lucru.
- **CAT** preia frecventa RF afisata de statie, in kHz. La pierderea conexiunii, logarea cere
  debifarea CAT si completarea manuala. Nu se foloseste frecventa audio Mark/Space.
- Completati RST, numarul si schimbul primit. Campurile suplimentare se mapeaza explicit pe
  Name, Misc, Section, Grid si Comment ale definitiei. Pentru portabile, WPX se introduce explicit;
  prefixul simplu poate fi calculat pentru indicative fara bara.
- **Verifica** si mesajul de sub formular indica erorile, dublele si multiplicatorii noi.
- **Logheaza QSO**, sau Enter cand cursorul este in formular, salveaza QSO-ul. Un dupe ramane
  in jurnal, cu zero puncte; nu este sters automat. Ora UTC poate ramane goala pentru momentul
  curent sau poate fi introdusa ca `yyyy-MM-dd HH:mm:ss`.
- Corectarea se face pe randul selectat, apoi **Salveaza corectia**. Serialul deja transmis
  nu poate fi schimbat. Stergerea cere confirmare; scorul si dublele se recalculeaza.

O corectie nesalvata nu inlocuieste QSO-ul original. Salvarea jurnalului foloseste mai intai o
copie a sesiunii: daca scrierea esueaza, randurile si contorul activ nu sunt avansate. La o
eroare de salvare, mesajul ramane vizibil; inchiderea ferestrei/aplicatiei este oprita pentru
a permite remedierea erorii.

## Seriale si emisie

**Fixeaza QSO / serial** rezerva numarul si salveaza rezervarea asociata indicativului.
F2 face acelasi lucru inainte de a trimite raportul. Repetarea foloseste acelasi numar,
inclusiv dupa redeschiderea sesiunii. Indicativul si schimbul transmis sunt blocate pana la
logare sau anulare. **Anuleaza** nu recicleaza un serial care ar fi putut fi transmis.

In fereastra Contest:

| Tasta | Actiune |
| --- | --- |
| F1 | CQ TEST cu indicativul sesiunii |
| F2 | Raport si schimb repetat, cu serialul fixat |
| F3 | AGN AGN PSE |
| F4 | TU cu indicativul sesiunii |
| Esc | STOP TX prin emitatorul existent |

Debifati **S&P** pentru Run. Bifati **S&P** pentru Search & Pounce, unde F1 trimite
indicativul propriu, F2 trimite schimbul si F4 confirma cu TU. Fiecare set poate fi editat
separat prin click dreapta pe butoanele F1–F4. Tineti cursorul peste buton pentru previzualizare.
Serialul din previzualizare este estimat pana la rezervare.

La incarcarea unei definitii sau redeschiderea unei sesiuni, se incarca fisierul de macro-uri
din folderul UDC. De exemplu, `RTTYops-WW-DX.udc` foloseste `RTTYops-WW-DX.macros.json`.
Daca fisierul lipseste, se creeaza automat cu seturile generice Run si S&P. Pentru a muta
configuratia pe alt calculator, pastrati fisierele impreuna si importati UDC-ul. Importul
preia si fisierul de macro-uri alaturat daca destinatia nu are deja unul.
Un fisier invalid este pastrat si eroarea este afisata.

Emisia foloseste configuratia FSK/CAT a aplicatiei, numai cu statia conectata in RTTY-L/U.
Optiunea existenta QSY automat inainte de TX ramane valabila. Logarea, schimbarea sesiunii si
anularea sunt blocate inclusiv in scurta asteptare pentru QSY. Mesajele Contest nu sunt puse
in coada peste o emisie activa; asteptati sfarsitul sau apasati STOP. Macro-urile normale ale
ferestrei principale nu sunt rescrise. TU nu logheaza automat un QSO.

## Export

**Export ADIF** deschide salvarea unui `.adi`, cu o locatie sugerata in Documente; nu scrie
langa executabil. ADI 3.1.8 contine UTC, frecventa MHz, banda, RTTY, RST, seriale, schimburile
transmis/primit, nume, locator, comentariu si indicativul propriu. Include si QSO-urile duble.
Campurile ADI sunt ASCII imprimabil: caracterele internationale sunt refuzate explicit,
fara transliterare/pierdere silentioasa. Jurnalul JSON pastreaza integral textul original UTF-8.

**Export Cabrillo** foloseste sablonul format 99 al UDC-ului. In **Antete Cabrillo** completati
categoria reala, cate o linie `CHEIE: valoare`. Exemplu de sintaxa, de adaptat categoriei proprii:

```text
CATEGORY-OPERATOR: SINGLE-OP
CATEGORY-BAND: ALL
CATEGORY-POWER: LOW
CATEGORY-ASSISTED: NON-ASSISTED
CATEGORY-TRANSMITTER: ONE
OPERATORS: YO9VA
```

Modul este RTTY; antetele cerute suplimentar de organizator (nume, adresa, club etc.) trebuie
adaugate de operator. Nu se inventeaza o categorie si nu se trunchiaza un schimb care depaseste
latimea sablonului. Exportul nu inseamna verificarea tuturor regulilor organizatorului.

## Date de tara si compatibilitate

HamCallsign rezolva tara si zonele din cty.dat. Pentru reguli WAE, integrarea incarca si
intrarile CTY marcate cu `*`, peste identitatile DXCC de baza. QSO-urile retin identitatile
rezolvate la logare. Necunoscutele necesare punctajului sunt raportate, nu ghicite.

Compatibilitatea UDC este cea documentata de HamContest preview.2, nu intreaga functionalitate
N1MM. Diagnosticele complete sunt disponibile in fereastra. Nu exista sincronizare multioperator,
suport pentru toate layout-urile SEC, import de macro-uri .mc sau comparare certificata cu N1MM.

CI verifica serialul salvat inainte de TX, repetare/restart, erori de scriere fara TX, editare,
duble, import fara suprascriere, ADIF/Cabrillo si ferestre reale WPF. Un test al executabilului
publicat deschide fereastra din aplicatia parinte, o inchide/redeschide si produce capturi PNG,
folosind un profil temporar si o gazda care nu poate actiona CAT/PTT.
