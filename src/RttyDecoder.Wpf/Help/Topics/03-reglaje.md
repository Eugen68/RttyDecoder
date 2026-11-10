# Reglaje de receptie

Butoanele de sub waterfall si panoul **Reglaje** (butonul cu acelasi nume). In panou apare si explicatia decodorului ales; controalele fara efect sunt dezactivate.

| Reglaj | Ce face |
|---|---|
| **NORM / REV** | Care ton e Mark. REV = Space sub Mark. In RTTY-U receptia cere de obicei REV (comportament Yaesu). Afecteaza **doar receptia**. |
| **Urmarire: AFC / PLL** | Urmareste deriva semnalului. AFC = lent si stabil, PLL = rapid. Pe zgomot acordul nu se misca. |
| **NB** (Noise Blanker) | Reduce impulsurile (QRN, scantei). Prag NB in Reglaje. |
| **DNR** | Filtru trece-banda intre Mark si Space. Latime DNR in Reglaje. |
| **SNR-G** | Sub pragul de semnal, decizia ramane pe Mark (idle) - mai putine caractere false. |
| **SQL** (squelch, pornit implicit) | Textul apare doar cand caracterele vin in ritmul RTTY (~7,5 biti unul dupa altul). Pe zgomot nu mai apar caractere aleatoare. Nu schimba decodarea, iar inceputul mesajului nu se pierde. Prag SQL in Reglaje (implicit 35%: mai mic = deschide mai usor, mai mare = mai putin gunoi). |
| **ATC** (W7AY) | Compenseaza fadingul selectiv (un ton se stinge, celalalt nu). Doar Chebyshev. |
| **Volant** | Sincronizare pe caracter la RTTY continuu: tine grila caracterelor si corecteaza startul. |
| **US-TTY** | Tabelul de cifre US-TTY (apostrof, ghilimele, $) in loc de ITA2. Se foloseste si la emisie. |
| **Sensibilitate** | Pragul de decizie al decodoarelor clasice. |
| **Latime filtru** | Latimea filtrelor Chebyshev pe fiecare ton (zona colorata din waterfall). |
| **CLEAR** | Goleste terminalul si uita textul neconfirmat al DeepRTTY. |

## Ce se aplica la fiecare decodor

| Reglaj | Chebyshev | Matched Filter | DeepRTTY |
|---|---|---|---|
| NB / DNR | Audio-ul decodorului | Audio-ul decodorului | Numai SNR si AFC/PLL |
| Latime filtru | Decodare si SNR/AFC | Nu se aplica | Numai SNR/AFC |
| ATC | Da | Normalizare proprie | Nu |
| SNR-G, Sensibilitate, Volant | Da | Da | Nu |
| SQL | Textul afisat | Textul afisat | Filtrul propriu DeepRTTY |
| NORM/REV, US-TTY | Da | Da | Da |
