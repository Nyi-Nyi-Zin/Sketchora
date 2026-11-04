namespace Sketchora.Core;

public static class ShortcutCatalog
{
    public static readonly string[] Tools = ["Pen", "Highlighter", "Eraser", "Line", "Arrow", "Rectangle", "Ellipse", "Text", "Fading"];
    public static readonly string[] Actions = ["Draw", "Hide", "Ghost", "Undo", "Redo", "Clear", "Export", "CopyScreenshot", "RegionScreenshot", "Exit", "NewLesson", "OpenLesson", "SaveLesson", "SaveLessonAs", "CycleBoard", "ToggleBackground", "PreviousPage", "NextPage", "AddPage", "DuplicatePage", "RenamePage", "DeletePage", "RestorePage", "ClearTemporary", "Settings", "CustomColor", "ToggleInk", "ToggleHalo", "ToggleTouch", "TogglePressure", "TogglePalm", "Collapse", "WidthUp", "WidthDown", "NextColor"];
    public static IEnumerable<string> Names => Tools.Select(tool => "Tool." + tool).Concat(Actions);
    public static Dictionary<string, uint> Defaults(char draw = 'D', char hide = 'H')
    {
        var keys = Names.ToDictionary(name => name, _ => 0u);
        keys["Draw"] = draw; keys["Hide"] = hide;
        // Keep migrated recovery keys even when they use one of the new defaults.
        var defaults = new Dictionary<string, uint>
        {
            ["Ghost"] = 'G', ["Tool.Pen"] = 'P', ["Tool.Highlighter"] = 'M', ["Tool.Eraser"] = 'E',
            ["Tool.Line"] = 'L', ["Tool.Arrow"] = 'A', ["Tool.Rectangle"] = 'R', ["Tool.Ellipse"] = 'O',
            ["Tool.Text"] = 'T', ["Tool.Fading"] = 'F', ["Undo"] = 'Z', ["Redo"] = 'Y', ["Clear"] = 'C',
            ["Export"] = 'S', ["CopyScreenshot"] = 'B', ["RegionScreenshot"] = 'X', ["CycleBoard"] = 'W'
        };
        foreach (var (name, key) in defaults)
            keys[name] = keys.Values.Contains(key) ? Enumerable.Range(0x70, 24).Select(i => (uint)i).First(k => !keys.Values.Contains(k)) : key;
        return keys;
    }
    public static bool IsKey(uint key) => key == 0 || key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;
    public static bool IsValid(IReadOnlyDictionary<string, uint>? keys) => keys is not null
        && Names.All(keys.ContainsKey) && keys.Count == Names.Count()
        && keys.Values.All(IsKey) && keys["Draw"] != 0 && keys["Hide"] != 0 && keys["Ghost"] != 0
        && keys.Values.Where(k => k != 0).Distinct().Count() == keys.Values.Count(k => k != 0);
    public static string KeyName(uint key) => key == 0 ? "Off" : key >= 0x70 ? $"F{key - 0x6F}" : ((char)key).ToString();
}
