using System.Collections.Immutable;
using HamContest;

namespace RttyDecoder.Wpf.Contest;

public sealed record ContestDraft(string Callsign = "", string FrequencyKhz = "", string Report = "599", string Serial = "",
    string Exchange = "", string Name = "", string Misc = "", string Section = "", string Grid = "", string Comment = "",
    string Wpx = "", string TimeUtc = "", bool UseCat = true);

public sealed class ContestEntry : ObservableObject
{
    private string _call = "", _frequency = "", _report = "599", _serial = "", _exchange = "", _name = "", _misc = "", _section = "", _grid = "", _comment = "", _wpx = "", _time = "";
    private bool _useCat = true;
    public string Callsign { get => _call; set => Set(ref _call, value ?? ""); }
    public string FrequencyKhz { get => _frequency; set => Set(ref _frequency, value ?? ""); }
    public string Report { get => _report; set => Set(ref _report, value ?? ""); }
    public string Serial { get => _serial; set => Set(ref _serial, value ?? ""); }
    public string Exchange { get => _exchange; set => Set(ref _exchange, value ?? ""); }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Misc { get => _misc; set => Set(ref _misc, value ?? ""); }
    public string Section { get => _section; set => Set(ref _section, value ?? ""); }
    public string Grid { get => _grid; set => Set(ref _grid, value ?? ""); }
    public string Comment { get => _comment; set => Set(ref _comment, value ?? ""); }
    public string Wpx { get => _wpx; set => Set(ref _wpx, value ?? ""); }
    public string TimeUtc { get => _time; set => Set(ref _time, value ?? ""); }
    public bool UseCat { get => _useCat; set => Set(ref _useCat, value); }
    public ContestDraft Capture() => new(Callsign, FrequencyKhz, Report, Serial, Exchange, Name, Misc, Section, Grid, Comment, Wpx, TimeUtc, UseCat);
    public void Apply(ContestDraft value)
    {
        Callsign = value.Callsign; FrequencyKhz = value.FrequencyKhz; Report = value.Report; Serial = value.Serial;
        Exchange = value.Exchange; Name = value.Name; Misc = value.Misc; Section = value.Section; Grid = value.Grid;
        Comment = value.Comment; Wpx = value.Wpx; TimeUtc = value.TimeUtc; UseCat = value.UseCat;
    }
}

public sealed record ContestQsoRow(Qso Qso, bool IsDupe, int Points, ImmutableArray<MultiplierCredit> Credits)
{
    public string Utc => Qso.Time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string Call => Qso.Station.Callsign;
    public string Band => Qso.Band;
    public string FrequencyKhz => (Qso.FrequencyHz / 1000m).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    public string Sent => ExchangeText(Qso.Sent);
    public string Received => ExchangeText(Qso.Received);
    public string Result => IsDupe ? "DUBLU" : string.Join(", ", Credits.Select(c => $"M{c.Group}: {c.Key}"));
    private static string ExchangeText(Exchange e) => string.Join(" ", new[] { e.Report, e.Serial?.ToString("D3"), e.Value, e.Name, e.Misc, e.Section, e.Grid, e.Comment }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>The decoder supplies radio state and sends text; tests use a host without hardware.</summary>
public interface IContestHost
{
    long? FrequencyHz { get; }
    bool IsBusy { get; }
    string MyCall { get; }
    bool TryTransmit(string text);
    void Stop();
}
