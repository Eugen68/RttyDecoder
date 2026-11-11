namespace RttyDecoder.Core.Tx;

/// <summary>Setarile emisiei RTTY (valorile implicite: ca in aplicatia Android).</summary>
public sealed class RttyTxSettings
{
    /// <summary>Caractere LTRS trimise dupa PTT, inainte de text (sincronizarea receptoarelor).</summary>
    public int PreambleLtrs { get; set; } = 2;

    /// <summary>Pauza pe Mark la sfarsitul emisiei, inainte de oprirea PTT-ului (ms).</summary>
    public int TailMs { get; set; } = 300;

    /// <summary>CR LF automat la inceputul si la sfarsitul fiecarei emisii.</summary>
    public bool AutoCrLf { get; set; } = true;

    /// <summary>In modul LIVE, cand nu scrii: LTRS continuu (receptoarele raman sincronizate).</summary>
    public bool Diddle { get; set; } = true;

    /// <summary>Limita unei emisii (secunde); peste ea emisia se opreste singura.</summary>
    public int MaxTxSeconds { get; set; } = 180;

    /// <summary>
    /// Inverseaza pinul FSK (pin activ = Space in loc de Mark). Implicit oprit: emisia nu urmeaza
    /// NORM/REV-ul receptiei si nici modul statiei; polaritatea se schimba de obicei din meniul statiei
    /// (TX polarity). Doar pentru interfete / statii care inverseaza linia.
    /// </summary>
    public bool InvertFsk { get; set; }

    /// <summary>
    /// QSY automat inainte de emisie: la un macro sau text care nu incepe cu CQ, dupa un A-TUNE care a
    /// calculat alta frecventa decat a statiei (statia in RTTY-L / RTTY-U), statia se muta intai pe
    /// frecventa corespondentului - ca dublu click pe A-TUNE. CQ-ul si LIVE nu muta statia. Implicit oprit.
    /// </summary>
    public bool AutoQsy { get; set; }

    /// <summary>FSK (pinul de manipulare, statia in RTTY) sau AFSK (tonuri pe placa de sunet, statia in DATA/SSB).</summary>
    public TxMethod Method { get; set; } = TxMethod.Fsk;

    /// <summary>AFSK: placa de iesire (id-ul WASAPI); gol = placa implicita a sistemului.</summary>
    public string AfskDeviceId { get; set; } = "";

    /// <summary>AFSK: nivelul tonurilor, 0..1 din plin. Se regleaza pana dispare ALC-ul statiei.</summary>
    public double AfskLevel { get; set; } = 0.5;

    /// <summary>Tabelul FIGS US-TTY in loc de ITA2.</summary>
    public bool UsTty { get; set; }

    public const double Baud = 45.45;

    /// <summary>Bitul de stop: 1.5 biti (RTTY standard).</summary>
    public const double StopBits = 1.5;

    public RttyTxSettings Clone() => (RttyTxSettings)MemberwiseClone();
}

/// <summary>Cum pleaca emisia RTTY.</summary>
public enum TxMethod
{
    /// <summary>Statia in RTTY, programul comuta pinul FSK (DTR/RTS).</summary>
    Fsk,
    /// <summary>Statia in DATA-U/USB (sau LSB), programul genereaza tonurile pe placa de sunet.</summary>
    Afsk,
}
