using System.Text.RegularExpressions;

namespace RttyDecoder.Benchmarks;

public static class Scoring
{
    // Pastreaza punctuatia si erorile de text; numai majuscule si whitespace uniform.
    public static string Normalize(string text) => Regex.Replace(text.ToUpperInvariant(), @"\s+", " ").Trim();

    public static int Distance(string expected, string actual)
    {
        // Doua randuri, nu o matrice de marimea intregii inregistrari.
        var previous = Enumerable.Range(0, actual.Length + 1).ToArray();
        var next = new int[actual.Length + 1];
        for (int i = 1; i <= expected.Length; i++)
        {
            next[0] = i;
            for (int j = 1; j <= actual.Length; j++)
                next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (expected[i - 1] == actual[j - 1] ? 0 : 1));
            (previous, next) = (next, previous);
        }
        return previous[actual.Length];
    }

    public static int? CompleteMessages(string? expected, string actual)
    {
        if (string.IsNullOrEmpty(expected)) return null;
        string text = Normalize(actual);
        int found = 0;
        foreach (var group in expected.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(x => x.Length > 0).GroupBy(x => x))
        {
            int count = 0, position = 0;
            while (count < group.Count() && (position = text.IndexOf(group.Key, position, StringComparison.Ordinal)) >= 0)
            {
                count++;
                position += group.Key.Length;
            }
            found += count;
        }
        return found;
    }

    public static int CountCall(string text, string call) => Regex.Matches(text.ToUpperInvariant(),
        @"(?<![A-Z0-9/])" + Regex.Escape(call.ToUpperInvariant()) + @"(?![A-Z0-9/])").Count;

    public static double Percentile(IEnumerable<double> values, double quantile)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("Nu exista masuratori.", nameof(values));
        return sorted[(int)Math.Ceiling(Math.Clamp(quantile, 0, 1) * (sorted.Length - 1))];
    }
}
