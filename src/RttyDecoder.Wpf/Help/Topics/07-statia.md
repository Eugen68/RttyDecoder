# Statia: CAT, OmniRig, Hamlib, PTT si manipulare

Din **Setari > Conexiune statie** (biblioteca HamRig).

## Direct pe portul serial (CAT Yaesu)

FTX-1, FTDX10, FT-710, FT-991A...
- **Portul CAT**: la FTX-1, primul port USB ("Enhanced").
- **Viteza**: ca in meniul statiei (implicit 38400).
- **PTT**: pe un pin al portului CAT (Fara / RTS / DTR).
- Statia anunta singura schimbarile de frecventa si mod.

## Prin OmniRig

Rig 1 sau Rig 2; statia poate fi folosita in acelasi timp si de alte programe. OmniRig doar citeste si schimba frecventa si modul - **fara PTT**.

## Prin Hamlib

Hamlib cunoaste sute de modele: Icom, Kenwood, Elecraft, Yaesu (inclusiv **FTX-1**, din Hamlib 4.7). Se instaleaza separat: [Hamlib releases](https://github.com/Hamlib/Hamlib/releases) (pe Windows, installer-ul sau arhiva `hamlib-w64`).

**Foloseste Hamlib 4.7.2 sau mai nou**: are doua corecturi de securitate in rigctld (CVE-2026-54634, GHSA-f72v-7gmh-m9mj) si corecturi pentru FTX-1. Cand lista de modele se incarca, fereastra de setari arata versiunea si te avertizeaza daca e mai veche. rigctld pornit de program asculta doar pe calculatorul tau (127.0.0.1), nu in retea.

- Programul vorbeste cu **rigctld** (programul de retea din Hamlib), implicit pe `127.0.0.1:4532`.
- Cu bifa **Porneste rigctld din program**: alegi **modelul** (lista vine din `rigctl -l`; se poate scrie si numarul, de ex. 1051 = FTX-1, 1042 = FTDX-10, 1049 = FT-710, 3073 = IC-7300, 3094 = IC-7300MK2), **portul** si **viteza** statiei. Programul porneste rigctld si il opreste la iesire. Calea catre `rigctld.exe` se gaseste singura (PATH sau `Program Files\hamlib*\bin`); altfel o alegi cu **...**.
- Fara bifa: programul se leaga la un rigctld deja pornit (de exemplu cel folosit si de WSJT-X) - completezi doar adresa.
- Modurile: RTTY = RTTY-L, RTTYR = RTTY-U, PKTUSB / PKTLSB = DATA-U / DATA-L.
- **PTT** prin Hamlib, sau pe un pin al portului de manipulare. **Manipularea FSK** doar pe portul de manipulare.
- Unele statii nu raporteaza prin Hamlib emisia sau modul; atunci se vede doar frecventa - restul merge.

### Icom IC-7300 / IC-7300MK2 prin Hamlib

"Direct pe portul serial" e doar pentru Yaesu (comenzi CAT text). Icom vorbeste CI-V, deci se leaga **prin Hamlib**: model **3073** (IC-7300) sau **3094** (IC-7300MK2).

Pe statie (MENU > SET > Connectors > CI-V; numele pot diferi dupa firmware):

| Setare pe statie | Valoare | De ce |
|---|---|---|
| CI-V USB Baud Rate | 19200 sau 115200 (nu Auto) | Aceeasi viteza o pui la **Viteza** in sectiunea Hamlib |
| CI-V Address | cea implicita a statiei (IC-7300: 94h; la MK2 verifica in meniu) | Hamlib foloseste adresa implicita a modelului; daca ai schimbat-o, pune la **Argumente** `--civaddr=0x94` (cu adresa ta) |
| CI-V Transceive | ON (merge si OFF) | Daca vezi erori sau frecventa care sare, incearca OFF |
| CI-V USB Port | Unlink from [REMOTE] | Portul USB separat de mufa CI-V din spate |
| CI-V USB Echo Back | OFF | Hamlib nu are nevoie de ecou |

Porturile USB:
- **IC-7300MK2** (USB-C) are doua porturi COM: primul la **Port statie** (CAT prin Hamlib), al doilea la **Port manipulare** pentru FSK (pin DTR sau RTS, la fel ca setarea de manipulare RTTY prin USB din meniul statiei). PTT-ul poate merge prin Hamlib.
- **IC-7300** (primul model) are un singur port COM: il tine rigctld, deci FSK pe acelasi port nu merge - foloseste **AFSK** (Setari transmisie), statia in USB-D, DATA MOD = USB.

## Port manipulare si PTT

Un al doilea port serial: port, viteza, **PTT** (Fara / RTS / DTR) si **manipulare** (Fara / RTS / DTR) - FSK pentru RTTY. La FTX-1 de obicei al doilea port ("Standard"), de exemplu PTT pe RTS si FSK pe DTR.

**Inverseaza polaritatea FSK** (pin activ = Space): **Yaesu nebifat, Icom bifat** - testat pe FTDX10, FTX-1 si IC-7300MK2 cu setarile din fabrica. Se bifeaza singur cand alegi un model Icom din lista Hamlib si se debifeaza la conexiunea directa (Yaesu). La IC-7300MK2: in meniul statiei USB SEND = USB (B) DTR, USB Keying (RTTY) = USB (B) RTS (portul (A) e cel CI-V, tinut de rigctld), iar in program portul de manipulare = al doilea port COM al statiei, PTT pe DTR, manipulare pe RTS.

PTT-ul se alege **intr-un singur loc** (la portul CAT sau la portul de manipulare): cand alegi un pin intr-o parte, cealalta trece pe Fara. Fara niciun pin, la conexiunea directa emisia se comanda prin CAT (`TX1;`), la Hamlib prin rigctld; la OmniRig nu exista PTT.

## Teste

- **Testeaza**: citeste frecventa si modul.
- **Test PTT 0.5 s**: comuta emisia o jumatate de secunda.
- **Test manipulare 0.5 s**: activeaza pinul de manipulare.

## Pe ecran

Langa nivel apar frecventa VFO si modul (in USB/LSB/DATA si frecventa RF a Mark-ului). **TX** rosu cand statia emite. **Setari > Reconecteaza statia** reface legatura dupa ce ai deconectat cablul sau ai repornit statia.
