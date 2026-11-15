using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RttyDecoder.Wpf.Contest;

internal sealed record ContestMacro(string Name, string Text);

internal sealed record ContestMacros(int Version, ContestMacro[] Run, ContestMacro[] SearchAndPounce)
{
    /// <summary>F1..F7. Fisierele vechi (F1..F4) se completeaza cu F5..F7 implicite.</summary>
    public const int Count = 7;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex Variable = new(@"\{([^{}]+)\}");

    public static ContestMacros Defaults() => new(2,
        [
            new("CQ", "CQ TEST DE {MYCALL} {MYCALL} K"),
            new("Raport", "{CALL} {RST} {EXCHANGE} {EXCHANGE} DE {MYCALL} K"),
            new("AGN", "AGN AGN PSE"),
            new("TU", "{CALL} TU DE {MYCALL} K"),
            new("NR?", "NR? NR?"),
            new("Indicativ?", "CALL? CALL?"),
            new("QRZ", "QRZ? DE {MYCALL} K")
        ],
        [
            new("Indicativ", "{MYCALL} {MYCALL}"),
            new("Raport", "{RST} {EXCHANGE}"),
            new("AGN", "AGN AGN PSE"),
            new("TU", "TU"),
            new("NR?", "NR? NR?"),
            new("Indicativ?", "CALL? CALL?"),
            new("73", "TU 73 GL")
        ]);

    public ContestMacro Get(bool searchAndPounce, int slot) => (searchAndPounce ? SearchAndPounce : Run)[slot];

    public ContestMacros With(bool searchAndPounce, int slot, ContestMacro value)
    {
        Validate(value, slot);
        var messages = (ContestMacro[])(searchAndPounce ? SearchAndPounce : Run).Clone();
        messages[slot] = value;
        return searchAndPounce ? this with { SearchAndPounce = messages } : this with { Run = messages };
    }

    public static ContestMacros LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = Defaults();
            try { ContestFiles.AtomicWrite(path, defaults.Serialize(), overwrite: false); return defaults; }
            catch (IOException) when (File.Exists(path)) { }
        }

        var macros = JsonSerializer.Deserialize<ContestMacros>(ContestFiles.ReadText(path), Json);
        if (macros is not ({ Version: 1, Run.Length: 4, SearchAndPounce.Length: 4 } or { Version: 2, Run.Length: Count, SearchAndPounce.Length: Count }))
            throw new InvalidDataException($"Fisierul de macro-uri trebuie sa contina {Count} mesaje Run si {Count} mesaje S&P.");
        if (macros.Version == 1)
        {
            // fisier vechi (F1..F4): F5..F7 implicite; se scrie la prima salvare a unui macro
            var d = Defaults();
            macros = new(2, [.. macros.Run, .. d.Run[4..]], [.. macros.SearchAndPounce, .. d.SearchAndPounce[4..]]);
        }
        for (int slot = 0; slot < Count; slot++)
        {
            Validate(macros.Run[slot], slot);
            Validate(macros.SearchAndPounce[slot], slot);
        }
        return macros;
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static string Render(ContestMacro macro, Func<string, string> resolve) =>
        Variable.Replace(macro.Text, match => resolve(match.Groups[1].Value)).Trim();

    private static void Validate(ContestMacro? macro, int slot)
    {
        if (macro == null || string.IsNullOrWhiteSpace(macro.Name) || string.IsNullOrWhiteSpace(macro.Text))
            throw new InvalidDataException("Fiecare macro trebuie sa aiba nume si text.");
        string remainder = Render(macro, key => key switch
        {
            "MYCALL" or "CALL" or "RST" => "",
            "EXCHANGE" when slot == 1 => "",
            _ => throw new InvalidDataException("Variabila necunoscuta sau indisponibila pentru acest buton: {" + key + "}.")
        });
        if (remainder.Contains('{') || remainder.Contains('}'))
            throw new InvalidDataException("Verificati acoladele variabilelor din macro.");
    }
}
