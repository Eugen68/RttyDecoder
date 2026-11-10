# Contest: definitii si sesiuni

## Fisierele concursului

| Continut | Folder |
|---|---|
| Definitii `.udc` si macro-urile lor `.macros.json` | `%APPDATA%\YO9VA\RttyDecoder\UserDefinedContests` |
| Liste de sectiuni `.sec` | `%APPDATA%\YO9VA\RttyDecoder\SupportFiles` |
| Jurnalele `.contest.json` | `%APPDATA%\YO9VA\RttyDecoder\Contests\Sessions` |
| Exporturi (locatie sugerata) | `Documente\RttyDecoder\Logs` |

- Folderele se creeaza automat; se deschid din **Setari > Folder UDC / Folder SEC** (in fereastra Contest) sau din meniul **Contest** al ferestrei principale.
- **Fisiere > Importa UDC / SEC** copiaza fisierele alese. Un fisier diferit cu acelasi nume primeste un sufix - definitia existenta nu se suprascrie. Importul preia si fisierul de macro-uri de langa UDC, daca destinatia nu are deja unul.
- Nu tine doua liste SEC diferite cu acelasi **Type**: HamContest raporteaza conflictul.
- `DEMO-RTTY.udc` si `DEMO.sec` sunt exemple pentru exercitiu, nu un regulament real. Nu se descarca automat definitii de concurs.

## Pornirea unei sesiuni

1. Alege **UDC-ul**, **indicativul propriu** si **primul serial**. Completeaza sectiunea/locatorul propriu daca regulamentul le foloseste; prefixul WPX propriu se poate completa explicit.
2. **Incepe sesiunea**. Definitia trebuie sa fie suportata si sa permita RTTY (Mode=RTTY sau BOTH).
3. Completeaza **Schimb fix (fara serial)** cu schimbul real cerut de regulament: zona, an, nume/QTH etc. Biblioteca nu poate transforma automat un text ca "SeeRules" intr-un schimb real.

## Redeschiderea

- **Fisiere > Deschide sesiune...** sau **Reia ultima sesiune**: jurnalul salvat, cu rezervarea in curs.
- **Fisiere > Nou...** pastreaza jurnalul curent si revine la configurare. Mai multe sesiuni sunt fisiere independente.
- Sesiunea pastreaza textul UDC/SEC original: editarea ulterioara a fisierelor din folder nu schimba regulile unei sesiuni incepute.

## Diagnostice

Avertismentele si erorile din definitia UDC apar in fereastra (sectiunea de diagnostice). Compatibilitatea UDC e un subset documentat de HamContest, nu toata functionalitatea N1MM.
