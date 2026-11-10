using System.Globalization;
using System.IO;
using System.Text;

namespace RttyDecoder.Wpf.Help;

/// <summary>Un subiect de ajutor: titlul (primul rand "# ...") si textul (Markdown simplu).</summary>
public sealed record HelpTopic(string Id, string Title, string Markdown);

/// <summary>
/// Subiectele de ajutor, incluse in program ca fisiere Help/Topics/NN-nume.md (ordinea = NN). Un
/// subiect nou = un fisier nou in folder, fara alte modificari de cod.
/// </summary>
public static class HelpTopics
{
    public const string ResourcePrefix = "RttyDecoder.Wpf.Help.";

    public static IReadOnlyList<HelpTopic> All { get; } = Load();

    private static List<HelpTopic> Load()
    {
        var assembly = typeof(HelpTopics).Assembly;
        var topics = new List<HelpTopic>();
        foreach (string name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string text = reader.ReadToEnd().Replace("\r\n", "\n");
            string id = name[ResourcePrefix.Length..^".md".Length];
            string title = text.Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? id;
            topics.Add(new HelpTopic(id, title, text));
        }
        return topics;
    }

    /// <summary>Subiectul cu Id-ul dat, sau null.</summary>
    public static HelpTopic? Find(string id) => All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Subiectele care contin toate cuvintele cautate (in titlu sau text), fara sa conteze literele
    /// mari si diacriticele ("fisiere" gaseste si "fișiere"). Cautare goala = toate.
    /// </summary>
    public static IReadOnlyList<HelpTopic> Search(string? query)
    {
        var words = Normalize(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All;
        return All.Where(t =>
        {
            string text = Normalize(t.Title + " " + t.Markdown);
            return words.All(text.Contains);
        }).ToList();
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
