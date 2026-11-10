# Primii pasi

1. Alege **intrarea** (placa de sunet a statiei sau interfata radio) si **canalul** (stanga, dreapta sau mix), apoi apasa **START**.
2. Verifica **nivelul** (langa SNR): ideal intre -30 si -6 dBFS. Prea mic = semnalul se pierde in zgomot; prea mare = distorsiuni.
3. Acordeaza decodorul pe semnal: **click pe waterfall** pe tonul Mark (linia rosie; linia portocalie e Space), sau **A-TUNE**, care gaseste singur cel mai puternic semnal RTTY si pune si NORM/REV dupa semnal.
4. Daca iese text fara sens, incearca **REV** (sau A-TUNE, care il detecteaza singur).
5. **MONITOR** reda in difuzoare ce primeste decodorul. Atentie la reactie: daca intrarea e chiar iesirea placii (Stereo Mix, cablu virtual), sunetul se intoarce.

## Indicatorul de acord

In dreapta waterfall-ului: Mark pe orizontala, Space pe verticala.

| Ce vezi | Ce inseamna |
|---|---|
| O cruce din doua elipse subtiri | Acordat corect |
| Elipse inclinate sau groase | Dezacordat |
| O singura elipsa | Doar purtatoare, sau REV gresit |
| O pata | Zgomot sau semnal prea slab |

## Fisiere WAV

**Fisier > Deschide WAV...** (Ctrl+O) decodeaza o inregistrare. Cu **Timp real** debifat, un fisier de o ora se decodeaza in cateva zeci de secunde (DeepRTTY: cateva minute). Monitorul se aude doar cu Timp real bifat.

Arhiva programului contine `rtty_sample_11025hz.wav` pentru o proba rapida (Mark 1275 Hz: apasa A-TUNE sau click pe semnal).
