using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using HamCallsign;
using HamCallsign.Online;

namespace RttyDecoder.Wpf;

/// <summary>Indicativele din textul decodat (biblioteca HamCallsign): recunoastere, lista statiilor, click.</summary>
public sealed partial class RttyDecoderViewModel
{
    private CallsignTracker _callsigns;
    private readonly Dictionary<string, HeardStationRow> _rows = new(StringComparer.Ordinal);

    /// <summary>
    /// Click pe un indicativ (in terminal sau in lista): gazda il poate folosi (log, DX cluster, ...).
    /// Indicativul e deja copiat in clipboard. Pe firul UI.
    /// </summary>
    public event Action<Callsign>? CallsignSelected;

    /// <summary>Urmarirea indicativelor din textul decodat (evenimentele vin pe firul UI).</summary>
    public CallsignTracker Callsigns => _callsigns;

    /// <summary>Statiile auzite, pentru lista din interfata.</summary>
    public ObservableCollection<HeardStationRow> HeardStations { get; } = new();

    /// <summary>Lista statiilor, implicit cele auzite cel mai recent primele.</summary>
    public ICollectionView HeardStationsView { get; }

    public ICommand ClearStationsCommand { get; }

    /// <summary>
    /// Verificarea online (QRZ.com, apoi HamQTH.com) a indicativelor care nu sunt in listele locale.
    /// Fara date de cont nu face nimic. Gazda seteaza contul cu <see cref="SetOnlineCredentials"/>.
    /// </summary>
    public CallsignVerifier Verifier { get; private set; } = null!;

    /// <summary>Exista cel putin un cont (QRZ.com sau HamQTH.com) completat.</summary>
    public bool OnlineVerificationEnabled => Verifier.HasQrz || Verifier.HasHamQth;

    /// <summary>
    /// Datele de cont pentru verificarea online (goale = fara). Indicativele deja auzite si
    /// necunoscute local se verifica imediat.
    /// </summary>
    public void SetOnlineCredentials(string? qrzUsername, string? qrzPassword, string? hamQthUsername, string? hamQthPassword)
    {
        Verifier.QrzUsername = qrzUsername;
        Verifier.QrzPassword = qrzPassword;
        Verifier.HamQthUsername = hamQthUsername;
        Verifier.HamQthPassword = hamQthPassword;
        OnVerificationUpdated();
        if (!OnlineVerificationEnabled) return;
        foreach (var row in HeardStations)
        {
            if (!row.IsKnown) Verifier.RequestVerification(row.Callsign);
        }
    }

    private void CreateVerifier()
    {
        var v = new CallsignVerifier(c => _callsigns.Recognizer.KnownIn(c) != KnownSources.None,
            new QrzLookupClient(agent: "RttyDecoder"), new HamQthLookupClient(agent: "RttyDecoder"));
        v.VerificationUpdated += () => _dispatcher.BeginInvoke(OnVerificationUpdated);
        Verifier = v;
    }

    /// <summary>Un raspuns online a sosit: lista si terminalul se recoloreaza.</summary>
    private void OnVerificationUpdated()
    {
        foreach (var row in HeardStations) row.SetOnline(Verifier.GetState(row.Callsign), Verifier.GetResult(row.Callsign));
        CallsignSettingsChanged?.Invoke();
    }

    private string _myCall = "";
    /// <summary>Indicativul propriu: evidentiat altfel si neinclus in lista statiilor.</summary>
    public string MyCall
    {
        get => _myCall;
        set
        {
            string v = (value ?? "").Trim().ToUpperInvariant();
            if (!Set(ref _myCall, v)) return;
            _callsigns.Recognizer.MyCall = v;
            CallsignSettingsChanged?.Invoke();
            RefreshMacroPreviews();
        }
    }

    private bool _showStations = true;
    /// <summary>Lista statiilor auzite e vizibila.</summary>
    public bool ShowStations
    {
        get => _showStations;
        set => Set(ref _showStations, value);
    }

    private string _stationsText = "0 statii";
    public string StationsText
    {
        get => _stationsText;
        private set => Set(ref _stationsText, value);
    }

    /// <summary>Indicativul propriu s-a schimbat: textul deja afisat trebuie re-evidentiat (pe firul UI).</summary>
    internal event Action? CallsignSettingsChanged;

    /// <summary>
    /// Bazele cty.dat / MASTER.SCP / LoTW (de obicei <see cref="ReferenceData.Load"/> facut in fundal la
    /// pornire, apoi din nou dupa o actualizare). Pana atunci se recunoaste doar forma. Lista statiilor
    /// ramane; indicativele din terminal se recoloreaza.
    /// </summary>
    public void UseReferenceData(ReferenceData data)
    {
        var r = data.CreateRecognizer();
        r.MyCall = _myCall;
        _callsigns.Recognizer = r;
        Verifier.InvalidateUnconfirmed();
        CallsignSettingsChanged?.Invoke();
    }

    /// <summary>Recunoasterea unui cuvant cu bazele curente (pentru evidentierea din terminal).</summary>
    public CallsignMatch? RecognizeWord(string word) => _callsigns.Recognizer.Recognize(word);

    /// <summary>Click pe un indicativ: in clipboard si catre gazda.</summary>
    public void SelectCallsign(Callsign call)
    {
        LastCallsign = call.Full;   // pentru {CALL} in macro-uri
        try
        {
            Clipboard.SetText(call.Full);
            Message = $"{call.Full} copiat in clipboard";
        }
        catch
        {
            // clipboard ocupat de alt program: nu e grav
        }
        CallsignSelected?.Invoke(call);
    }

    /// <summary>Dublu click: pagina indicativului pe QRZ.com (indicativul de baza, fara /P, DL/ ...).</summary>
    public void OpenQrz(Callsign call)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.qrz.com/db/" + Uri.EscapeDataString(call.HomeCall)) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Message = "Nu pot deschide QRZ.com: " + e.Message;
        }
    }

    private CallsignTracker CreateTracker(CallsignRecognizer recognizer)
    {
        recognizer.MyCall = _myCall;
        var t = new CallsignTracker(recognizer);
        t.StationHeard += OnStationHeard;
        t.CallsignRecognized += m =>
        {
            if (m.Status == CallsignStatus.FormatOnly && Verifier != null && OnlineVerificationEnabled) Verifier.RequestVerification(m.Callsign);
        };
        return t;
    }

    private ICollectionView CreateStationsView()
    {
        var v = new ListCollectionView(HeardStations);
        v.SortDescriptions.Add(new SortDescription(nameof(HeardStationRow.LastHeardUtc), ListSortDirection.Descending));
        v.IsLiveSorting = true;
        v.LiveSortingProperties.Add(nameof(HeardStationRow.LastHeardUtc));
        v.LiveSortingProperties.Add(nameof(HeardStationRow.Count));
        return v;
    }

    private void OnStationHeard(HeardStation s, bool isNew)
    {
        if (_rows.TryGetValue(s.Callsign.Full, out var row))
        {
            row.Update(s);
            return;
        }
        row = new HeardStationRow(s);
        row.SetOnline(Verifier.GetState(row.Callsign), Verifier.GetResult(row.Callsign));
        _rows[s.Callsign.Full] = row;
        HeardStations.Add(row);
        StationsText = HeardStations.Count == 1 ? "1 statie" : $"{HeardStations.Count} statii";
    }

    private void ClearStations()
    {
        _callsigns.Clear();
        _rows.Clear();
        HeardStations.Clear();
        StationsText = "0 statii";
    }
}

/// <summary>Un rand din lista statiilor auzite.</summary>
public sealed class HeardStationRow : ObservableObject
{
    internal HeardStationRow(HeardStation s)
    {
        Callsign = s.Callsign;
        Entity = s.Entity;
        KnownIn = s.KnownIn;
        FirstHeardUtc = s.FirstHeard.UtcDateTime;
        Update(s);
    }

    public Callsign Callsign { get; }

    public DxccEntity? Entity { get; }

    public string Call => Callsign.Full;

    public string Country => Entity?.Name ?? "";

    public string Continent => Entity?.Continent ?? "";

    public string CqZone => Entity == null ? "" : Entity.CqZone.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gasit in MASTER.SCP sau LoTW.</summary>
    public bool IsKnown => KnownIn != KnownSources.None;

    /// <summary>Starea verificarii online (QRZ.com / HamQTH.com).</summary>
    public CallsignVerificationState OnlineState { get; private set; } = CallsignVerificationState.Pending;

    /// <summary>Datele gasite online (nume, oras), daca exista.</summary>
    public CallsignLookupResult? OnlineResult { get; private set; }

    /// <summary>Cunoscut local sau confirmat online: verde in lista.</summary>
    public bool IsConfirmed => IsKnown || OnlineState == CallsignVerificationState.Confirmed;

    internal void SetOnline(CallsignVerificationState state, CallsignLookupResult? result)
    {
        if (state == OnlineState && result == OnlineResult) return;
        OnlineState = state;
        OnlineResult = result;
        OnPropertyChanged(nameof(OnlineState));
        OnPropertyChanged(nameof(OnlineResult));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(ToolTip));
    }

    public KnownSources KnownIn { get; }

    public DateTime FirstHeardUtc { get; }

    private DateTime _lastHeardUtc;
    public DateTime LastHeardUtc
    {
        get => _lastHeardUtc;
        private set => Set(ref _lastHeardUtc, value);
    }

    private int _count;
    public int Count
    {
        get => _count;
        private set => Set(ref _count, value);
    }

    public string ToolTip =>
        Entity == null ? Call : $"{Call} - {Entity.Name} ({Entity.Continent}), CQ {Entity.CqZone}, ITU {Entity.ItuZone}" +
                                "\n" + KnownText(KnownIn) + OnlineText(OnlineState, OnlineResult);

    /// <summary>Randul din tooltip pentru verificarea online ("" daca nu s-a verificat).</summary>
    internal static string OnlineText(CallsignVerificationState state, CallsignLookupResult? r) => state switch
    {
        CallsignVerificationState.Confirmed when r != null =>
            $"\nconfirmat de {r.Source}: " + string.Join(", ", new[] { r.Name, r.City, r.Country, r.Grid }.Where(x => x.Length > 0)),
        CallsignVerificationState.NotFound => "\nQRZ.com / HamQTH.com: negasit - probabil o eroare de decodare",
        _ => "",
    };

    /// <summary>"in MASTER.SCP", "in LoTW", "in MASTER.SCP si LoTW" sau avertismentul de necunoscut.</summary>
    internal static string KnownText(KnownSources k) => k switch
    {
        KnownSources.MasterScp => "in MASTER.SCP",
        KnownSources.Lotw => "in lista LoTW",
        KnownSources.MasterScp | KnownSources.Lotw => "in MASTER.SCP si LoTW",
        _ => "nu e in MASTER.SCP / LoTW (verifica: poate fi o eroare de decodare)",
    };

    internal void Update(HeardStation s)
    {
        LastHeardUtc = s.LastHeard.UtcDateTime;
        Count = s.Count;
    }
}
