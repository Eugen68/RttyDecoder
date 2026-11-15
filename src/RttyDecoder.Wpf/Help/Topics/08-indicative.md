# Indicative si lista statiilor

Indicativele din textul decodat sunt evidentiate:

| Culoare | Inseamna |
|---|---|
| Verde | Cunoscut: in MASTER.SCP sau in lista LoTW (sau confirmat online) |
| Auriu | Forma corecta, dar necunoscut - poate fi si o eroare de decodare |
| Portocaliu | Indicativul tau (campul "Indicativul meu" din lista statiilor) |
| Gri, taiat | Contest: dublu (doar cu fereastra Contest deschisa) |
| Roz | Contest: multiplicator nou (doar cu fereastra Contest deschisa) |

- **Mouse-ul deasupra**: tara si zonele (din cty.dat).
- **Click**: copiaza indicativul in clipboard si il foloseste pentru `{CALL}` in macro-uri (si il trimite in fereastra Contest, daca e deschisa).
- **Dublu click** (in terminal): deschide pagina lui pe QRZ.com.
- Indicativele cu prefix litera+cifra (`S50A`, `A61BK`, `E73A`) sunt recunoscute daca cty.dat confirma prefixul.

## Lista statiilor

Butonul **Statii**: tara, continent, zona CQ, de cate ori a fost auzita, **Frecventa** (Mark-ul pe RF, in kHz, la ultima decodare - doar cu statia conectata) si **UTC** (ultima decodare). Se sorteaza pe orice coloana; **Fisier > Salveaza lista statiilor (CSV)**. **Goleste** sterge lista.

- **Click**: ca in terminal (clipboard, `{CALL}`, fereastra Contest).
- **Dublu click = QSY** pe frecventa unde a fost auzit, cu NORM/REV-ul lui:
  - **FSK** (statia in RTTY): ca dublu click pe A-TUNE - perechea lui ajunge pe 2125 / 2295, deci raspunzi exact pe frecventa lui;
  - **AFSK** / SSB / DATA: perechea lui ajunge pe perechea decodorului;
  - statia trebuie sa fie in **acelasi mod** ca atunci cand a fost auzit;
  - fara statie conectata se muta doar decodorul pe tonul lui (corect doar daca VFO-ul n-a fost mutat).
- **Click dreapta**: Look-up QRZ.com, QSY pe frecventa lui, Copiaza indicativul.

## Verificare online

**Setari > Verificare online (QRZ.com / HamQTH.com)**: cu un cont, indicativele aurii se verifica si online (QRZ.com intai, HamQTH.com ca rezerva gratuita). Cele confirmate devin verzi, cu numele si orasul in tooltip; cele negasite raman aurii. O singura cautare pe indicativ si sesiune. Parolele sunt criptate (Windows DPAPI) in `settings.json`.
