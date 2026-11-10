using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RttyDecoder.Wpf;

/// <summary>
/// Evidentierea unui cuvant din terminal (fundal si subliniere), desenata deasupra textului: documentul
/// nu se modifica, deci textul in curs de decodare si legaturile lui raman neatinse.
/// </summary>
internal sealed class WordHighlightAdorner : Adorner
{
    private static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xE5, 0xFF)));
    private static readonly Pen Line = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), 1.5));
    private Rect? _rect;

    public WordHighlightAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

    /// <summary>Dreptunghiul cuvantului (coordonate in elementul adornat), sau null = nimic.</summary>
    public Rect? Rect
    {
        get => _rect;
        set { if (_rect != value) { _rect = value; InvalidateVisual(); } }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_rect is not { } r || r.IsEmpty) return;
        dc.DrawRoundedRectangle(Fill, null, r, 2, 2);
        dc.DrawLine(Line, new Point(r.Left, r.Bottom), new Point(r.Right, r.Bottom));
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
