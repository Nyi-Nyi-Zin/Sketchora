using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sketchora.App.Controls;

/// <summary>Small vector glyphs stay crisp at any Windows display scale.</summary>
public sealed class GlyphIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(GlyphIcon),
        new FrameworkPropertyMetadata("Pen", FrameworkPropertyMetadataOptions.AffectsRender));
    public GlyphIcon() { IsHitTestVisible = false; Focusable = false; }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly IReadOnlyDictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["Pen"] = "M4,20 L5,15 16,4 Q18,2 20,4 Q22,6 20,8 L9,19 Z M14,6 L18,10 M5,15 L9,19",
        ["Highlighter"] = "M4,16 L12,8 18,14 10,22 Z M11,9 L16,4 22,10 17,15 M3,21 L9,21",
        ["Eraser"] = "M3,14 L12,4 Q13,3 14,4 L21,11 Q22,12 21,13 L13,21 9,21 Z M7,10 L17,18 M13,21 L22,21",
        ["Line"] = "M4,20 L20,4 M3,20 L5,20 M19,4 L21,4",
        ["Arrow"] = "M4,20 L20,4 M9,4 L20,4 20,15",
        ["Rectangle"] = "M4,5 L20,5 20,19 4,19 Z",
        ["Ellipse"] = "M21,12 A9,7 0 1 1 3,12 A9,7 0 1 1 21,12 Z",
        ["Text"] = "M4,5 L20,5 M12,5 L12,21 M8,21 L16,21 M4,5 L4,8 M20,5 L20,8",
        ["Fading"] = "M12,2 L13,8 19,9 13,11 12,17 10,11 4,9 10,8 Z M19,15 L20,18 23,19 20,20 19,23 18,20 15,19 18,18 Z",
        ["Eye"] = "M2,12 Q12,-1 22,12 Q12,25 2,12 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 Z",
        ["Cursor"] = "M5,3 L5,20 10,15 14,22 17,20 13,13 20,12 Z",
        ["Undo"] = "M8,5 L3,10 8,15 M3,10 L14,10 Q21,10 21,16 Q21,21 15,21",
        ["Redo"] = "M16,5 L21,10 16,15 M21,10 L10,10 Q3,10 3,16 Q3,21 9,21",
        ["Clear"] = "M4,6 L20,6 M9,6 L9,3 15,3 15,6 M6,6 L7,21 17,21 18,6 M10,10 L10,17 M14,10 L14,17",
        ["Camera"] = "M3,7 L7,7 9,4 15,4 17,7 21,7 21,20 3,20 Z M16,13 A4,4 0 1 1 8,13 A4,4 0 1 1 16,13 Z",
        ["Copy"] = "M8,7 L21,7 21,21 8,21 Z M16,7 L16,3 3,3 3,16 8,16",
        ["Board"] = "M3,4 L21,4 21,17 3,17 Z M12,17 L12,22 M7,22 L17,22 M6,8 L15,8 M6,12 L18,12",
        ["Expand"] = "M9,5 L16,12 9,19",
        ["Collapse"] = "M15,5 L8,12 15,19",
        ["Save"] = "M4,3 L18,3 21,6 21,21 3,21 3,3 Z M7,3 L7,10 17,10 17,3 M7,21 L7,14 17,14 17,21",
        ["Open"] = "M3,18 L3,5 10,5 13,8 20,8 M3,20 L7,11 23,11 19,20 Z",
        ["New"] = "M14,2 L14,8 20,8 M4,2 L14,2 20,8 20,22 4,22 Z M12,11 L12,19 M8,15 L16,15",
        ["Settings"] = "M5,4 L5,20 M12,4 L12,20 M19,4 L19,20 M2,9 L8,9 M9,15 L15,15 M16,7 L22,7",
        ["Ghost"] = "M4,21 L4,11 A8,8 0 1 1 20,11 L20,21 16,18 12,21 8,18 Z M9,10 L9,12 M15,10 L15,12",
        ["Hide"] = "M4,4 L20,20 M3,12 Q7,6 12,6 Q17,6 21,12 M3,12 Q7,18 12,18 Q17,18 21,12",
        ["Quit"] = "M12,3 L12,12 M6,6 A9,9 0 1 0 18,6",
        ["Previous"] = "M15,5 L8,12 15,19",
        ["Next"] = "M9,5 L16,12 9,19",
        ["Region"] = "M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16 M8,8 L16,8 16,16 8,16 Z"
    }.ToDictionary(pair => pair.Key, pair => { var shape = Geometry.Parse(pair.Value); shape.Freeze(); return shape; });

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var pen = new Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        drawingContext.DrawGeometry(null, pen, Shapes.GetValueOrDefault(Kind) ?? Shapes["Pen"]);
        drawingContext.Pop();
    }
}
