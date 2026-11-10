using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HamContest;
using HamContest.Udc;

namespace RttyDecoder.Wpf.Contest;

public sealed record ContestDefinitionFile(string Path, string Name)
{
    public override string ToString() => Name;
}
public sealed record ContestDefinition(string FileName, string Text, ImmutableArray<string> Sections, UdcImportResult Import);
public sealed record PendingContestContact(string Callsign, string Report, string FixedExchange, DateTimeOffset Time);
public sealed record ContestDocument(int Version, string DefinitionFileName, string DefinitionText,
    ImmutableArray<string> SectionTexts, string SessionJson, string SentReport, string FixedExchange,
    PendingContestContact? PendingContact, ContestDraft? Draft = null, string CabrilloHeaders = "");

/// <summary>All mutable contest data lives in the user's profile, never beside the executable.</summary>
public sealed class ContestFiles
{
    public string Root { get; }
    public string DefinitionsFolder => System.IO.Path.Combine(Root, "UserDefinedContests");
    public string SupportFolder => System.IO.Path.Combine(Root, "SupportFiles");
    public string SessionsFolder => System.IO.Path.Combine(Root, "Contests", "Sessions");
    public string LastSessionFile => System.IO.Path.Combine(Root, "Contests", "last-session.txt");
    public static string DefaultRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YO9VA", "RttyDecoder");
    public static string DefaultExportFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RttyDecoder", "Logs");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly Action<string, string> _write;

    public ContestFiles(string? root = null, Action<string, string>? write = null)
    {
        Root = System.IO.Path.GetFullPath(root ?? DefaultRoot);
        _write = write ?? AtomicWrite;
        Directory.CreateDirectory(DefinitionsFolder); Directory.CreateDirectory(SupportFolder); Directory.CreateDirectory(SessionsFolder);
        Seed("DEMO-RTTY.udc", DefinitionsFolder); Seed("DEMO.sec", SupportFolder);
    }
    private static void Seed(string name, string folder)
    {
        string path = System.IO.Path.Combine(folder, name);
        if (File.Exists(path)) return;
        using var stream = typeof(ContestFiles).Assembly.GetManifestResourceStream("RttyDecoder.Contest." + name)!;
        using var reader = new StreamReader(stream);
        // Never replace a definition edited by the user.
        try { using var file = new FileStream(path, FileMode.CreateNew); using var writer = new StreamWriter(file, new UTF8Encoding(false)); writer.Write(reader.ReadToEnd()); }
        catch (IOException) when (File.Exists(path)) { }
    }
    public ImmutableArray<ContestDefinitionFile> ListDefinitions() => Directory.EnumerateFiles(DefinitionsFolder, "*", SearchOption.TopDirectoryOnly)
        .Where(p => System.IO.Path.GetExtension(p).Equals(".udc", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
        .Select(p => new ContestDefinitionFile(p, System.IO.Path.GetFileName(p))).ToImmutableArray();

    public ContestDefinition LoadDefinition(string path)
    {
        string text = ReadText(path);
        var sections = ImmutableArray.CreateBuilder<string>();
        foreach (string file in Directory.EnumerateFiles(SupportFolder).Where(p => System.IO.Path.GetExtension(p).Equals(".sec", StringComparison.OrdinalIgnoreCase)))
        {
            string source = ReadText(file);
            try { SectionList.Parse(source); sections.Add(source); }
            catch (FormatException) { /* Required invalid lists become explicit missing-SEC errors during import. */ }
        }
        return new(System.IO.Path.GetFileName(path), text, sections.ToImmutable(), UdcImporter.Import(text, sections.Select(SectionList.Parse)));
    }
    public string Import(string source)
    {
        string extension = System.IO.Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".udc" or ".sec")) throw new ArgumentException("Alegeti un fisier .udc sau .sec.");
        string text = ReadText(source);
        if (extension == ".sec") SectionList.Parse(text);
        else if (UdcDocument.Parse(text).Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) throw new FormatException("Fisierul UDC are sintaxa invalida.");
        string folder = extension == ".udc" ? DefinitionsFolder : SupportFolder;
        string name = System.IO.Path.GetFileName(source), target = System.IO.Path.Combine(folder, name);
        if (File.Exists(target) && ReadText(target) != text)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..10];
            target = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(name) + "-" + hash + extension);
        }
        if (!File.Exists(target)) _write(target, text);
        return target;
    }
    public string NewSessionPath(string name) => System.IO.Path.Combine(SessionsFolder, name + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".contest.json");
    public void Save(string path, ContestDocument document) => _write(path, JsonSerializer.Serialize(document, Json));
    public ContestDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<ContestDocument>(ReadText(path), Json) ?? throw new InvalidDataException("Sesiune goala.");
        if (document.Version != 1 || document.SectionTexts.IsDefault || string.IsNullOrEmpty(document.DefinitionText) || string.IsNullOrEmpty(document.SessionJson))
            throw new InvalidDataException("Format de sesiune necunoscut sau incomplet.");
        return document;
    }
    public void Remember(string path) { try { _write(LastSessionFile, System.IO.Path.GetFullPath(path)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public string? LastSession() { try { return File.Exists(LastSessionFile) ? ReadText(LastSessionFile).Trim() : null; } catch (IOException) { return null; } }
    public static string ReadText(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new StreamReader(file, new UTF8Encoding(false, true), true);
        return reader.ReadToEnd();
    }
    public string MacroPath(string definitionFileName)
    {
        // Restored sessions can come from another machine; only accept a bare UDC filename.
        if (definitionFileName != System.IO.Path.GetFileName(definitionFileName)
            || definitionFileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || !System.IO.Path.GetExtension(definitionFileName).Equals(".udc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Numele definitiei UDC nu este valid.");
        return System.IO.Path.Combine(DefinitionsFolder, System.IO.Path.GetFileNameWithoutExtension(definitionFileName) + ".macros.json");
    }

    public static void AtomicWrite(string path, string text) => AtomicWrite(path, text, overwrite: true);

    internal static void AtomicWrite(string path, string text, bool overwrite)
    {
        path = System.IO.Path.GetFullPath(path); string directory = System.IO.Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory); string temporary = System.IO.Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true); writer.Write(text); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
