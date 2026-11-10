# Fisiere si foldere

Programul nu scrie nimic langa executabil. Datele stau in doua foldere din profilul tau Windows.

## Date de indicative - `%LOCALAPPDATA%\YO9VA\RttyDecoder`

De obicei `C:\Users\<nume>\AppData\Local\YO9VA\RttyDecoder`. Se deschide din **Indicative > Deschide folderul cu date**.

| Fisier | Ce contine | Actualizare automata |
|---|---|---|
| `cty.dat` | Tari, continente, zone CQ/ITU dupa prefix (AD1C, country-files.com) | Daca are peste 7 zile |
| `MASTER.SCP` | Indicative active (Super Check Partial) | Daca are peste 7 zile |
| `lotw-user-activity.csv` | Utilizatorii LoTW activi (~6 MB) | Zilnic, doar daca s-a schimbat |

- **La pornire** se citesc fisierele din acest folder.
- **Daca lipsesc sau sunt stricate** (de exemplu cty.dat cu prea putine tari), se folosesc copiile incluse in program (biblioteca HamCallsign).
- **Actualizarea** se face in fundal, dupa pornire; un fisier se inlocuieste doar daca descarcarea e valida. Fara internet nu se intampla nimic.
- **Indicative > Actualizeaza datele acum** verifica imediat si foloseste fisierele noi fara repornire; **Indicative > Starea datelor** arata cate indicative si de cand e fiecare fisier.
- Fereastra **Contest** foloseste acelasi `cty.dat` (si intrarile marcate cu `*` pentru concursurile WAE).
- O versiune anume de cty.dat (de exemplu pentru un concurs) se copiaza peste cea din folder. Atentie: daca e mai veche de 7 zile, actualizarea automata o inlocuieste la urmatoarea pornire.

## Setari si concurs - `%APPDATA%\YO9VA\RttyDecoder`

De obicei `C:\Users\<nume>\AppData\Roaming\YO9VA\RttyDecoder`. Se deschide din **Setari > Deschide folderul setarilor**.

| Fisier / folder | Ce contine |
|---|---|
| `settings.json` | Toate setarile: audio, decodor, waterfall, statie, transmisie, macro-uri, pozitia ferestrei principale si a ferestrei Contest; parolele QRZ/HamQTH criptate |
| `UserDefinedContests\` | Definitiile de concurs `.udc` si macro-urile lor `.macros.json` |
| `SupportFiles\` | Listele de sectiuni `.sec` |
| `Contests\Sessions\` | Jurnalele de concurs `.contest.json` |

Folderele de concurs se deschid si din meniul **Contest**. Exporturile ADIF/Cabrillo se salveaza unde alegi (implicit sugerat `Documente\RttyDecoder\Logs`).

## Mutarea pe alt calculator

Copiaza folderul `%APPDATA%\YO9VA\RttyDecoder` (setari, concursuri, jurnale). Parolele QRZ/HamQTH nu se pot citi pe alt calculator (sunt criptate pentru contul Windows) - se reintroduc. Folderul din `%LOCALAPPDATA%` nu trebuie copiat: se reface singur.
