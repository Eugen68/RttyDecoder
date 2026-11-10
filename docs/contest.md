# Modul Contest

**Contest > Deschide fereastra de concurs**, sau **Ctrl+Shift+C**, deschide o fereastra separata,
nemodala. Decodorul ramane disponibil. O singura fereastra Contest este deschisa la un moment
dat; repetarea comenzii o aduce in fata. Inchiderea ei salveaza formularul si nu opreste receptia.

Opțiunea **Deasupra decodorului** este bifată implicit. Debifați-o pentru a aduce decodorul
în față și pentru a permite ferestrei Contest să rămână în spate. **Ctrl+Shift+C** readuce
Contest în față. Alegerea se păstrează dacă închideți și redeschideți Contest cât timp
aplicația rămâne pornită. **Poziția și mărimea** ferestrei Contest se salvează în `settings.json`
și se refac la următoarea deschidere. Deasupra jurnalului sunt ceasurile **UTC** și **LOCAL**.

Biblioteca [HamContest](https://github.com/Eugen68/HamContest) este un submodul fixat la commit-ul
`86b76b4` (ramura `station-preview`: preview.2 din PR #1 al bibliotecii, plus `UdcContestRules.Preview`
pentru culorile din decodor). Clonati cu submodule:

```sh
git clone --recurse-submodules https://github.com/Eugen68/RttyDecoder.git
```

Meniul **Ajutor** al ferestrei Contest: **Ajutor** deschide fereastra de ajutor a aplicatiei la
subiectele de concurs (pe scurt, definitii si sesiuni, logare si jurnal, macro-uri/seriale/emisie,
export); **Despre** arata versiunea aplicatiei, build-ul si biblioteca HamContest.

## Fisiere

| Continut | Folder implicit |
| --- | --- |
| Definitii .udc | `%APPDATA%\YO9VA\RttyDecoder\UserDefinedContests` |
| Liste .sec | `%APPDATA%\YO9VA\RttyDecoder\SupportFiles` |
| Jurnale .contest.json | `%APPDATA%\YO9VA\RttyDecoder\Contests\Sessions` |
| Exporturi, locatie sugerata | `Documente\RttyDecoder\Logs` |

Folderele sunt create automat. **Fișiere → Importă UDC / SEC** copiaza fisierele alese; un fisier diferit
cu acelasi nume primeste un sufix, fara a suprascrie definitia existenta. Nu pastrati doua liste
SEC diferite cu acelasi Type: HamContest raporteaza conflictul; folositi **Setări → Folder SEC**
pentru a administra listele. SEC trebuie sa aiba layout-ul simplu suportat de biblioteca.

DEMO-RTTY.udc si DEMO.sec sunt exemple originale pentru exercitiu, nu un regulament de concurs.
Copiile deja existente nu sunt inlocuite la pornire. Nu sunt descarcate automat definitii de concurs.

## Pornirea si redeschiderea

1. Alegeti UDC-ul, indicativul propriu si primul serial. Completati sectiunea/locatorul propriu
   daca regulamentul le foloseste; prefixul WPX propriu poate fi completat explicit.
2. Apasati **Incepe sesiunea**. Definitia trebuie sa fie suportata si sa permita RTTY sau BOTH.
3. Completati **Schimb fix (fara serial)** cu schimbul real cerut de regulament: zona, an,
   nume/QTH etc. Biblioteca nu poate transforma automat un text precum SeeRules intr-un schimb real.
4. Folositi **Deschide sesiune** sau **Reia ultima sesiune** din **Fișiere** pentru un jurnal salvat. **Nou** pastreaza
   jurnalul curent si revine la configurare. Mai multe sesiuni sunt fisiere independente.

Sesiunea contine textul UDC/SEC original, datele QSO-urilor, regulile identificate prin amprenta,
formularul si rezervarea curenta. Editarea ulterioara a fisierelor din catalog nu schimba regulile
unei sesiuni incepute. Schimbarile incompatibile ale versiunii HamContest necesita migrare explicita.

## Introducere si logare

- Click pe un indicativ in terminal sau lista statiilor il trimite si formularului Contest,
  daca fereastra este vizibila si nu exista un QSO fixat/corectie in lucru.
- **Click pe un numar sau un cuvant** din terminal (care nu e indicativ) il preia in formular:
  `599`/`5NN` = RST primit; un numar = **Nr. primit** (daca concursul are seriale si campul e gol),
  altfel **Schimb primit**; un cuvant (sectiune, nume, zona) = **Schimb primit**. Cuvintele uzuale
  (DE, TU, K, CQ, TEST, 73...) sunt ignorate, la fel ora si textul trimis. Sub mouse, cuvintele care se
  pot prelua se evidentiaza (fundal si subliniere) si cursorul devine mana, ca la indicative. Preluarea
  nu transmite nimic. Limita: un serial chiar egal cu 599 se introduce de mana.
- Cat e deschisa fereastra Contest, **indicativele din terminal si din lista statiilor sunt colorate**
  dupa starea lor in sesiune, pe banda curenta: **gri taiat = dublu**, **roz = multiplicator nou**,
  culorile obisnuite = QSO valid. Tooltip-ul o spune si in cuvinte. Calculul e facut de HamContest
  (`UdcContestRules.Preview`), fara schimbul primit: **dublu** = indicativ lucrat deja pe banda/modul
  cerut de regulament; multiplicatorii care vin din indicativ (tara, prefix WPX, zona CQ/ITU din cty.dat)
  se vad inainte de QSO, cei care vin din schimb (sectiune, numar) abia la logare. Daca regulamentul are
  si un criteriu de dublu pe schimb, gri inseamna "lucrat deja" (QSO-ul poate fi totusi valid). Culorile
  se refac la fiecare QSO si la schimbarea benzii (prin CAT).
- **CAT** preia frecventa RF afisata de statie, in kHz. La pierderea conexiunii, logarea cere
  debifarea CAT si completarea manuala. Nu se foloseste frecventa audio Mark/Space.
- Completati RST, numarul si schimbul primit. Campurile suplimentare se mapeaza explicit pe
  Name, Misc, Section, Grid si Comment ale definitiei. Pentru portabile, WPX se introduce explicit;
  prefixul simplu poate fi calculat pentru indicative fara bara.
- Pentru RTTYops WW DX, dacă indicativul are același an în toate contactele anterioare cu
  an valid din sesiunea curentă, formularul arată anul și contactul sursă. **Preia anul**
  îl copiază în **Schimb primit** numai dacă acel câmp este gol. Verificați anul recepționat
  înainte de logare. Dacă anii diferă, nu se propune o valoare. Numărul primit se introduce manual.
- **Verifica** si mesajul de sub formular indica erorile, dublele si multiplicatorii noi.
- **Enter** in formular, cu **ESM** bifat (implicit), trimite mesajul potrivit starii:

  | Mod | Starea | Enter |
  | --- | --- | --- |
  | Run | fara indicativ | CQ (F1) |
  | Run | indicativ, fara schimb primit | raportul (F2, fixeaza serialul; repetat = acelasi serial) |
  | Run | schimbul primit completat | logheaza QSO-ul, apoi TU (F4) |
  | S&P | fara schimb primit | indicativul tau (F1) |
  | S&P | schimbul primit completat | schimbul tau (F2) |
  | S&P | schimbul trimis | logheaza QSO-ul (fara emisie) |

  Dupa raport cursorul trece la numarul/schimbul primit, dupa logare la indicativ. Cu ESM debifat,
  Enter doar logheaza.
- **Logheaza QSO** salveaza QSO-ul. Un dupe ramane
  in jurnal, cu zero puncte; nu este sters automat. Ora UTC poate ramane goala pentru momentul
  curent sau poate fi introdusa ca `yyyy-MM-dd HH:mm:ss`.
- **Click dreapta pe un QSO** din jurnal: **Look-up QRZ.com** (pagina indicativului in browser),
  **Corecteaza selectat...** (QSO-ul trece in formular, apoi **Salveaza corectia**; serialul deja
  transmis nu poate fi schimbat) si **Sterge selectat...** (cere confirmare; scorul si dublele se
  recalculeaza).

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
indicativul propriu, F2 trimite schimbul si F4 confirma cu TU. In S&P, F1 (indicativul propriu)
se poate repeta si dupa ce QSO-ul a fost fixat cu F2; in Run, F1 (CQ) cere logarea sau anularea
QSO-ului curent. Fiecare set poate fi editat
separat prin click dreapta pe butoanele F1–F4. Tineti cursorul peste buton pentru previzualizare.
Serialul din previzualizare este estimat pana la rezervare.

La incarcarea unei definitii sau redeschiderea unei sesiuni, se incarca fisierul de macro-uri
din folderul UDC. De exemplu, `RTTYops-WW-DX.udc` foloseste `RTTYops-WW-DX.macros.json`.
Daca fisierul lipseste, se creeaza automat cu seturile generice Run si S&P. Pentru a muta
configuratia pe alt calculator, pastrati fisierele impreuna si importati UDC-ul. Importul
preia si fisierul de macro-uri alaturat daca destinatia nu are deja unul.
Un fisier invalid este pastrat si eroarea este afisata.

Emisia foloseste configuratia FSK/CAT a aplicatiei, numai cu statia conectata in RTTY-L/U.
Optiunea QSY automat inainte de raspuns se aplica **numai in S&P** (statia merge pe frecventa
corespondentului calculata de A-TUNE). In Run statia ramane pe frecventa ta: altfel, la fiecare
A-TUNE pe un corespondent care raspunde alaturi, frecventa de Run ar aluneca. Logarea, schimbarea sesiunii si
anularea sunt blocate inclusiv in scurta asteptare pentru QSY. Mesajele Contest nu sunt puse
in coada peste o emisie activa; asteptati sfarsitul sau apasati STOP. Macro-urile normale ale
ferestrei principale nu sunt rescrise. TU nu logheaza automat un QSO.

## Export

**Unelte → Export ADIF** deschide salvarea unui `.adi`, cu o locatie sugerata in Documente; nu scrie
langa executabil. ADI 3.1.8 contine UTC, frecventa MHz, banda, RTTY, RST, seriale, schimburile
transmis/primit, nume, locator, comentariu si indicativul propriu. Include si QSO-urile duble.
Campurile ADI sunt ASCII imprimabil: caracterele internationale sunt refuzate explicit,
fara transliterare/pierdere silentioasa. Jurnalul JSON pastreaza integral textul original UTF-8.

**Unelte → Export Cabrillo** foloseste sablonul format 99 al UDC-ului. In **Antete Cabrillo** completati
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
