# Corpus si rezultate de decodare

Inregistrarile WebSDR (8 kHz mono) sunt in release-ul public
[recordings](https://github.com/Eugen68/RttyDecoder/releases/tag/recordings).
Benchmark-ul automat verifica SHA256 inainte de fiecare decodare.

| Caz | Fisier | Mark fix | Referinta |
|---|---|---:|---|
| 14086 | `websdr_recording_2022-06-14T23_08_37Z_14086.0kHz.wav` | 2123 Hz | Mesajele W6IWI 00..99 |
| 7086-qrn | `QRN.websdr_recording_2022-03-15T05_45_16Z_7086.0kHz.wav` | 2048 Hz | Mesajele W6IWI 00..99; a doua jumatate foarte slaba |
| 14089 | `websdr_recording_2024-04-15T23_12_30Z_14089.1kHz.wav` | 2120 Hz | RY / TEST DE W6IWI; transcriere completa neverificata, CER n/a |
| sample | `samples/rtty_sample_11025hz.wav` | 1275 Hz | Text cunoscut, repetat de doua ori |
| noise | Generat la rulare, seed 12345, 120 s | 2125 Hz | Sir gol; caractere/minut in loc de CER |

## Rezultate actuale

[Masuratori 2026-11-05](results-2026-11-05.md), cu commit, setari, metoda si legatura spre
artefactele JSON/TXT/CSV ale rularii. [Datele JSON](results-2026-11-05.json) pastreaza
setarile si hash-urile chiar daca artefactul CI expira.

Trei profiluri din interfata folosesc configuratiile masurate: Matched Filter pe semnal bun,
DeepRTTY pe semnal slab, Chebyshev ca alternativa rapida pentru QRN/flutter. NB si ATC nu sunt
activate automat in ultimul profil: combinatiile masurate au crescut CER pe proba QRN.
Rezultatul unei singure inregistrari nu garanteaza comportamentul pe orice canal.

SQL este deja implementat si pornit implicit. Proba determinista de zgomot compara explicit
SQL activ cu SQL oprit; nu trebuie interpretata drept garantie pentru orice zgomot sau interferenta.
Pe 14089 raportam indicative si text integral, fara a inventa un numitor pentru CER.

## Reproducere

Comenzile, referintele exacte, limitele masuratorilor de latenta/memorie si procedura FSK fizica:
[tools/RttyDecoder.Benchmarks/README.md](../tools/RttyDecoder.Benchmarks/README.md).
Workflow-ul `benchmark` publica automat raportul in rezumat si artefactul `RttyDecoder-benchmark`.
Nu deschide porturi seriale si nu emite radio.

## Istoric

[Rezultatele din 2026-11-03](results-2026-11-03.md) sunt pastrate ca arhiva, inclusiv comparatiile
AFC/MF vechi. Nu se compara direct cu CER-ul automat actual: acordul, SQL, alfabetul si
normalizarea nu sunt identice in toate tabelele istorice.
