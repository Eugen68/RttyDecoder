# Statia: CAT, OmniRig, PTT si manipulare

Din **Setari > Conexiune statie** (biblioteca HamRig).

## Direct pe portul serial (CAT Yaesu)

FTX-1, FTDX10, FT-710, FT-991A...
- **Portul CAT**: la FTX-1, primul port USB ("Enhanced").
- **Viteza**: ca in meniul statiei (implicit 38400).
- **PTT**: pe un pin al portului CAT (Fara / RTS / DTR).
- Statia anunta singura schimbarile de frecventa si mod.

## Prin OmniRig

Rig 1 sau Rig 2; statia poate fi folosita in acelasi timp si de alte programe. OmniRig doar citeste si schimba frecventa si modul - **fara PTT**.

## Port manipulare si PTT

Un al doilea port serial: port, viteza, **PTT** (Fara / RTS / DTR) si **manipulare** (Fara / RTS / DTR) - FSK pentru RTTY. La FTX-1 de obicei al doilea port ("Standard"), de exemplu PTT pe RTS si FSK pe DTR.

PTT-ul se alege **intr-un singur loc** (la portul CAT sau la portul de manipulare): cand alegi un pin intr-o parte, cealalta trece pe Fara. Fara niciun pin, la conexiunea directa emisia se comanda prin CAT (`TX1;`).

## Teste

- **Testeaza**: citeste frecventa si modul.
- **Test PTT 0.5 s**: comuta emisia o jumatate de secunda.
- **Test manipulare 0.5 s**: activeaza pinul de manipulare.

## Pe ecran

Langa nivel apar frecventa VFO si modul (in USB/LSB/DATA si frecventa RF a Mark-ului). **TX** rosu cand statia emite. **Setari > Reconecteaza statia** reface legatura dupa ce ai deconectat cablul sau ai repornit statia.
