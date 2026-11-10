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
- **Dublu click**: deschide pagina lui pe QRZ.com.
- Indicativele cu prefix litera+cifra (`S50A`, `A61BK`, `E73A`) sunt recunoscute daca cty.dat confirma prefixul.

## Lista statiilor

Butonul **Statii**: tara, continent, zona CQ, de cate ori a fost auzita, prima si ultima data. Se sorteaza pe orice coloana; **Fisier > Salveaza lista statiilor (CSV)**. **Goleste** sterge lista.

## Verificare online

**Setari > Verificare online (QRZ.com / HamQTH.com)**: cu un cont, indicativele aurii se verifica si online (QRZ.com intai, HamQTH.com ca rezerva gratuita). Cele confirmate devin verzi, cu numele si orasul in tooltip; cele negasite raman aurii. O singura cautare pe indicativ si sesiune. Parolele sunt criptate (Windows DPAPI) in `settings.json`.
