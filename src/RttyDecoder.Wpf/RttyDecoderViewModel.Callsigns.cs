using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using HamCallsign;

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
    /// Bazele cty.dat / MASTER.SCP / LoTW (de obicei <see cref="ReferenceData.Load"/> facut in fundal la pornire).
    /// Pana atunci se recunoaste doar forma indicativelor. Lista statiilor se goleste.
    /// </summary>
    public void UseReferenceData(ReferenceData data)
    {
        var old = _callsigns;
        old.StationHeard -= OnStationHeard;
        _callsigns = CreateTracker(data.CreateRecognizer());
        ClearStations();
        CallsignSettingsChanged?.Invoke();
    }

    /// <summary>Recunoasterea unui cuvant cu bazele curente (pentru evidentierea din terminal).</summary>
    public CallsignMatch? RecognizeWord(string word) => _callsigns.Recognizer.Recognize(word);

    /// <summary>Click pe un indicativ: in clipboard si catre gazda.</summary>
    public void SelectCallsign(Callsign call)
    {
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
                                "\n" + KnownText(KnownIn);

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
