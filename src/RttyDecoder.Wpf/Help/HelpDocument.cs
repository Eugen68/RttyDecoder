using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RttyDecoder.Wpf.Help;

/// <summary>
/// Markdown simplu -&gt; FlowDocument, pentru subiectele de ajutor: titluri (#, ##, ###), paragrafe
/// (randurile consecutive se unesc), liste (- sau 1.), tabele (| a | b |), blocuri de cod (```) si,
/// in text, **ingrosat** si `cod`. Atat cat e nevoie aici - nu un parser Markdown complet.
/// </summary>
public static class HelpDocument
{
    public static FlowDocument Build(string markdown, Brush foreground, Brush accent, Brush muted, Brush codeBack)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = foreground,
            Background = Brushes.Transparent, PagePadding = new Thickness(16, 10, 16, 16), TextAlignment = TextAlignment.Left,
        };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        Paragraph? paragraph = null;
        List? list = null;
        Table? table = null;
        int i = 0;

        void EndBlocks() { paragraph = null; list = null; table = null; }

        while (i < lines.Length)
        {
            string raw = lines[i];
            string line = raw.TrimEnd();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                EndBlocks();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimEnd().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i].TrimEnd());
                i++;
                doc.Blocks.Add(new Paragraph(new Run(string.Join("\n", code)))
                {
                    FontFamily = new FontFamily("Consolas"), FontSize = 12, Background = codeBack,
                    Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 4, 0, 8),
                });
                continue;
            }

            if (line.Length == 0) { EndBlocks(); i++; continue; }

            int level = line.StartsWith("### ") ? 3 : line.StartsWith("## ") ? 2 : line.StartsWith("# ") ? 1 : 0;
            if (level > 0)
            {
                EndBlocks();
                var heading = new Paragraph { FontWeight = FontWeights.SemiBold, Foreground = level == 1 ? accent : foreground,
                    FontSize = level switch { 1 => 20, 2 => 16, _ => 14 }, Margin = new Thickness(0, level == 1 ? 0 : 12, 0, 6) };
                AddInlines(heading.Inlines, line[(level + 1)..].Trim(), codeBack);
                doc.Blocks.Add(heading);
                i++;
                continue;
            }

            if (line.StartsWith('|'))
            {
                var cells = SplitRow(line);
                bool separator = cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' '));
                if (separator) { i++; continue; }
                if (table == null)
                {
                    paragraph = null; list = null;
                    table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 8), BorderBrush = muted, BorderThickness = new Thickness(0, 0, 0, 1) };
                    for (int c = 0; c < cells.Length; c++) table.Columns.Add(new TableColumn());
                    table.RowGroups.Add(new TableRowGroup());
                    doc.Blocks.Add(table);
                }
                bool header = table.RowGroups[0].Rows.Count == 0;
                var row = new TableRow();
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var p = new Paragraph { Margin = new Thickness(0) };
                    if (c < cells.Length) AddInlines(p.Inlines, cells[c], codeBack);
                    if (header) p.FontWeight = FontWeights.SemiBold;
                    row.Cells.Add(new TableCell(p) { Padding = new Thickness(4, 3, 8, 3), BorderBrush = muted,
                        BorderThickness = new Thickness(0, header ? 0 : 1, 0, 0) });
                }
                table.RowGroups[0].Rows.Add(row);
                i++;
                continue;
            }

            string trimmed = line.TrimStart();
            bool bullet = trimmed.StartsWith("- ", StringComparison.Ordinal);
            int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
            bool numbered = dot > 0 && dot <= 3 && trimmed[..dot].All(char.IsDigit);
            if ((bullet || numbered) && raw.Length - raw.TrimStart().Length < 2)
            {
                paragraph = null; table = null;
                var marker = bullet ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal;
                if (list == null || list.MarkerStyle != marker)
                {
                    list = new List { MarkerStyle = marker, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(22, 0, 0, 0) };
                    doc.Blocks.Add(list);
                }
                var item = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                AddInlines(item.Inlines, bullet ? trimmed[2..] : trimmed[(dot + 2)..], codeBack);
                list.ListItems.Add(new ListItem(item));
                i++;
                continue;
            }

            if (list != null && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                // continuarea elementului de lista
                var last = (Paragraph)list.ListItems.LastListItem!.Blocks.LastBlock!;
                last.Inlines.Add(new Run(" "));
                AddInlines(last.Inlines, trimmed, codeBack);
                i++;
                continue;
            }

            list = null; table = null;
            if (paragraph == null)
            {
                paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                doc.Blocks.Add(paragraph);
            }
            else paragraph.Inlines.Add(new Run(" "));
            AddInlines(paragraph.Inlines, trimmed, codeBack);
            i++;
        }
        return doc;
    }

    private static string[] SplitRow(string line)
    {
        string inner = line.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|')) inner = inner[..^1];
        return inner.Split('|').Select(c => c.Trim()).ToArray();
    }

    /// <summary>Text cu **ingrosat** si `cod`.</summary>
    internal static void AddInlines(InlineCollection target, string text, Brush codeBack)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            int bold = text.IndexOf("**", pos, StringComparison.Ordinal);
            int code = text.IndexOf('`', pos);
            int next = bold < 0 ? code : code < 0 ? bold : Math.Min(bold, code);
            if (next < 0) { target.Add(new Run(text[pos..])); break; }
            if (next > pos) target.Add(new Run(text[pos..next]));
            if (next == code)
            {
                int end = text.IndexOf('`', code + 1);
                if (end < 0) { target.Add(new Run(text[code..])); break; }
                target.Add(new Run(text[(code + 1)..end]) { FontFamily = new FontFamily("Consolas"), Background = codeBack });
                pos = end + 1;
            }
            else
            {
                int end = text.IndexOf("**", bold + 2, StringComparison.Ordinal);
                if (end < 0) { target.Add(new Run(text[bold..])); break; }
                var span = new Bold();
                AddInlines(span.Inlines, text[(bold + 2)..end], codeBack);
                target.Add(span);
                pos = end + 2;
            }
        }
    }
}
