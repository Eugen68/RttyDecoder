using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HamCallsign;
using HamContest;

namespace RttyDecoder.Wpf.Contest;

/// <summary>Uses CTY data for DXCC and overlays CTY's starred WAE entries for WAE contests.</summary>
public sealed class ContestStationResolver
{
    private readonly CtyDatabase _dxcc, _wae;
    public ContestStationResolver(string source)
    {
        _dxcc = CtyDatabase.Load(new StringReader(source));
        var normal = new StringBuilder(); var starred = new StringBuilder(); StringBuilder current = normal;
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length != 0 && !char.IsWhiteSpace(line[0]))
            {
                var parts = line.Split(':');
                bool wae = parts.Length >= 8 && parts[7].Trim().StartsWith('*');
                current = wae ? starred : normal;
                if (wae) { parts[7] = " " + parts[7].Trim().TrimStart('*'); line = string.Join(':', parts); }
            }
            current.AppendLine(line);
        }
        // WAE aliases override their broader DXCC aliases, regardless of file order.
        _wae = CtyDatabase.Load(new StringReader(normal.ToString() + starred));
    }
    public static ContestStationResolver Load(string? cacheFolder = null)
    {
        if (cacheFolder != null)
        {
            string file = Path.Combine(cacheFolder, ReferenceData.CtyFileName);
            if (File.Exists(file))
            {
                var source = ContestFiles.ReadText(file);
                if (CtyDatabase.Load(new StringReader(source)).EntityCount >= ReferenceData.MinCtyEntities) return new(source);
            }
        }
        using var stream = typeof(CtyDatabase).Assembly.GetManifestResourceStream("HamCallsign.Data.cty.dat")!;
        using var reader = new StreamReader(stream);
        return new(reader.ReadToEnd());
    }
    public StationInfo Resolve(string text, CountryIdentityKind kind, string? wpx = null, string section = "", string grid = "")
    {
        var database = kind == CountryIdentityKind.Wae ? _wae : _dxcc;
        if (!Callsign.TryParse(text.Trim().ToUpperInvariant(), database, out var call)) throw new ArgumentException("Indicativ invalid: " + text);
        var entity = database.Lookup(call);
        return new(call.Full)
        {
            CountryPrefix = entity?.PrimaryPrefix ?? "", Continent = entity?.Continent ?? "", CqZone = entity?.CqZone,
            ItuZone = entity?.ItuZone, CountryIdentityKind = kind, WpxPrefix = (wpx ?? SimpleWpx(call.Full)).Trim().ToUpperInvariant(),
            Section = section.Trim().ToUpperInvariant(), Grid = grid.Trim().ToUpperInvariant()
        };
    }
    /// <summary>Only unambiguous ordinary calls are automatic; portable WPX prefixes are entered explicitly.</summary>
    public static string SimpleWpx(string call)
    {
        var match = Regex.Match(call.Trim().ToUpperInvariant(), "^([A-Z0-9]*[0-9])[A-Z]+$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "";
    }
}
