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

    /// <summary>Tabelul FIGS US-TTY in loc de ITA2.</summary>
    public bool UsTty { get; set; }

    public const double Baud = 45.45;

    /// <summary>Bitul de stop: 1.5 biti (RTTY standard).</summary>
    public const double StopBits = 1.5;

    public RttyTxSettings Clone() => (RttyTxSettings)MemberwiseClone();
}
