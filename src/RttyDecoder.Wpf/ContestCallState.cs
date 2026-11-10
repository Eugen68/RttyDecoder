namespace RttyDecoder.Wpf;

/// <summary>Starea unui indicativ in concursul activ (fereastra Contest), pentru colorarea din decodor.</summary>
public enum ContestCallState
{
    /// <summary>Niciun concurs activ, sau nu se poate calcula (frecventa necunoscuta, indicativ nerezolvat).</summary>
    None,
    /// <summary>QSO valid, fara multiplicator nou.</summary>
    Needed,
    /// <summary>QSO valid care aduce cel putin un multiplicator nou.</summary>
    NewMultiplier,
    /// <summary>Dublu pe banda/regula curenta: zero puncte.</summary>
    Dupe,
}
