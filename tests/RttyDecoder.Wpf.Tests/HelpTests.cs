using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using RttyDecoder.Wpf.Help;

namespace RttyDecoder.Wpf.Tests;

/// <summary>Subiectele de ajutor: incarcare, titluri, cautare si afisare.</summary>
public class HelpTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error != null) throw new Exception("STA", error);
    }

    [Fact]
    public void SubiecteleSuntIncluseInOrdineCuTitluri()
    {
        Assert.True(HelpTopics.All.Count >= 11);
        Assert.Equal(HelpTopics.All.Select(t => t.Id).Order(StringComparer.Ordinal), HelpTopics.All.Select(t => t.Id));
        Assert.All(HelpTopics.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.NotEqual(t.Id, t.Title);   // fiecare fisier incepe cu "# Titlu"
        });
        Assert.Equal(HelpTopics.All.Count, HelpTopics.All.Select(t => t.Title).Distinct().Count());
        Assert.Equal("Primii pasi", HelpTopics.All[0].Title);
    }

    [Fact]
    public void CautareaIgnoraDiacriticeleSiLitereleMari()
    {
        var cty = HelpTopics.Search("cty.dat localappdata");
        Assert.Contains(cty, t => t.Id == "09-fisiere-si-foldere");
        Assert.Equal(HelpTopics.Search("FIȘIERE foldere").Select(t => t.Id), HelpTopics.Search("fisiere foldere").Select(t => t.Id));
        Assert.Contains(HelpTopics.Search("model onnx"), t => t.Id == "02-decodoare-si-profiluri");
        Assert.Empty(HelpTopics.Search("cuvantcarenuexista"));
        Assert.Equal(HelpTopics.All.Count, HelpTopics.Search("  ").Count);
    }

    [Fact]
    public void FiecareSubiectSeAfiseaza() => Sta(() =>
    {
        foreach (var topic in HelpTopics.All)
        {
            var doc = HelpDocument.Build(topic.Markdown, Brushes.White, Brushes.Cyan, Brushes.Gray, Brushes.Black);
            Assert.True(doc.Blocks.Count > 2, topic.Id);
            Assert.IsType<Paragraph>(doc.Blocks.FirstBlock);   // titlul
        }
    });

    [Fact]
    public void MarkdownulSimpluDevineBlocuri() => Sta(() =>
    {
        const string md = "# Titlu\n\nUn **ingrosat** si `cod`\ncontinuat.\n\n- unu\n- doi\n  continuare\n\n1. primul\n2. al doilea\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```\nlinie de cod\n```\n";
        var doc = HelpDocument.Build(md, Brushes.White, Brushes.Cyan, Brushes.Gray, Brushes.Black);
        var blocks = doc.Blocks.ToList();
        Assert.Equal(6, blocks.Count);   // titlu, paragraf, lista, lista numerotata, tabel, cod
        var paragraph = (Paragraph)blocks[1];
        Assert.Contains(paragraph.Inlines, i => i is Bold);
        Assert.Equal("Un ingrosat si cod continuat.", new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text);
        var bullets = (List)blocks[2];
        Assert.Equal(2, bullets.ListItems.Count);
        var lastItem = (Paragraph)bullets.ListItems.LastListItem!.Blocks.FirstBlock!;   // fara bulina
        Assert.Equal("doi continuare", new TextRange(lastItem.ContentStart, lastItem.ContentEnd).Text);
        Assert.Equal(TextMarkerStyle.Decimal, ((List)blocks[3]).MarkerStyle);
        var table = (Table)blocks[4];
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal(2, table.RowGroups[0].Rows.Count);   // antet + un rand (separatorul nu e rand)
        Assert.Equal("linie de cod", new TextRange(blocks[5].ContentStart, blocks[5].ContentEnd).Text);
    });
}
