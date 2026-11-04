using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sketchora.Core;

namespace Sketchora.App.ViewModels;

public enum DrawingTool { Pen, Highlighter, Eraser, Line, Arrow, Rectangle, Ellipse, Text, Fading, Select, PartialEraser, Pan }
public enum CanvasAction
{
    Undo, Redo, Clear, Export, Hide, Exit, NewLesson, OpenLesson, SaveLesson, SaveLessonAs,
    CycleBoard, ToggleBackground, PreviousPage, NextPage, AddPage, DuplicatePage, RenamePage, DeletePage, RestorePage,
    ClearTemporary, Settings, Ghost, CustomColor, CopyScreenshot, RegionScreenshot,
    DuplicateSelection, DeleteSelection, Freeze, ExportPdf, Import, ZoomIn, ZoomOut, ResetView,
    ToggleSpotlight, PatternNone, PatternGrid, PatternRuled, RemoveImage
}

public sealed class ToolbarViewModel : ObservableObject
{
    private readonly List<IRelayCommand> _commands = [];
    private bool _isDrawing, _isBusy;
    private bool _showInk = true, _haloEnabled, _touchEnabled, _isExpanded;
    private DrawingTool _tool, _previousTool, _primaryTool;
    private Dictionary<string, uint> _shortcuts = ShortcutCatalog.Defaults();
    private int _shortcutRevision;
    public int ShortcutRevision => _shortcutRevision;
    public DrawingTool PrimaryTool => _primaryTool;
    public string PrimaryToolTip => GetToolTip("Tool." + _primaryTool, _primaryTool + " · Choose drawing tool");
    public string PrimaryToolGlyph => _primaryTool.ToString();
    public string WidthTip => GetToolTip("WidthUp", "Increase width") + "\n" + GetToolTip("WidthDown", "Decrease width");
    public string DrawTip => GetToolTip("Draw", ModeActionLabel);
    public string ExpandTip => GetToolTip("Collapse", IsExpanded ? "Collapse settings" : "Expand settings");
    public bool IsPrimaryToolActive => IsDrawing && Tool != DrawingTool.Eraser;
    public bool IsEraserActive => IsDrawing && Tool == DrawingTool.Eraser;
    private string _color, _notice = "Ready · Desktop ink stays fixed when content scrolls.";
    private string _lessonLabel = "Untitled lesson", _pageLabel = "Board 1 / 1", _boardLabel = "Board: desktop", _shortcutHint = "";
    private double _thickness, _textSize;
    private bool _pressureEnabled, _palmRejection, _frozenScreen, _spotlightEnabled;
    private double _zoom = 1, _panX, _panY, _spotlightRadius = 110;
    private int _selectionCount;
    public int SelectionCount { get => _selectionCount; set { if (SetProperty(ref _selectionCount, value)) RefreshCommands(); } }
    public bool CanNavigateCanvas => !_isDesktop || FrozenScreen;
    public bool FrozenScreen
    {
        get => _frozenScreen;
        set { if (SetProperty(ref _frozenScreen, value)) { RestoreAvailableTool(); OnPropertyChanged(nameof(FreezeLabel)); OnPropertyChanged(nameof(CanNavigateCanvas)); RefreshCommands(); } }
    }
    public string FreezeLabel => FrozenScreen ? "Unfreeze screen" : "Freeze screen";
    public bool SpotlightEnabled { get => _spotlightEnabled; set => SetProperty(ref _spotlightEnabled, value); }
    public double SpotlightRadius { get => _spotlightRadius; set { if (double.IsFinite(value)) SetProperty(ref _spotlightRadius, Math.Clamp(value, 30, 400)); } }
    public double ZoomLevel => _zoom;
    public double PanX => _panX;
    public double PanY => _panY;
    public string ZoomLabel => $"{_zoom:P0}";
    public void SetView(double zoom, double x, double y)
    {
        if (!double.IsFinite(zoom) || !double.IsFinite(x) || !double.IsFinite(y)) return;
        _zoom = Math.Clamp(zoom, .25, 4); _panX = Math.Clamp(x, -100000, 100000); _panY = Math.Clamp(y, -100000, 100000);
        OnPropertyChanged(nameof(ZoomLevel)); OnPropertyChanged(nameof(PanX)); OnPropertyChanged(nameof(PanY)); OnPropertyChanged(nameof(ZoomLabel));
    }
    private readonly Dictionary<string, double> _toolWidths;
    public static IReadOnlyList<string> Palette { get; } = new[] {
        "#6366F1", "#EF4444", "#F59E0B", "#10B981", "#38BDF8", "#FFFFFF", "#171B29", "#000000",
        "#F43F5E", "#EC4899", "#D946EF", "#A855F7", "#8B5CF6", "#3B82F6", "#06B6D4", "#14B8A6",
        "#22C55E", "#84CC16", "#EAB308", "#F97316", "#92400E", "#64748B", "#94A3B8", "#CBD5E1" };
    public IReadOnlyList<string> Colors => Palette;
    public Dictionary<string, double> ToolWidths => new(_toolWidths);
    private int _fadeSeconds, _itemCount;
    private bool _canUndo, _canRedo, _canPrevious, _canNext, _canDelete, _canRestore, _isDesktop = true, _canAdd = true;

    public ToolbarViewModel(AppPreferences preferences, Action<CanvasAction> dispatch)
    {
        _color = preferences.Color;
        _toolWidths = preferences.ToolWidths is null ? new() : new(preferences.ToolWidths);
        _thickness = _toolWidths.GetValueOrDefault(nameof(DrawingTool.Pen), preferences.Thickness);
        _spotlightRadius = preferences.SpotlightRadius;
        _pressureEnabled = preferences.PressureEnabled; _palmRejection = preferences.PalmRejection;
        _haloEnabled = preferences.PointerHalo; _touchEnabled = preferences.TouchDrawing;
        // Start narrow even when upgrading a previously expanded installation.
        _isExpanded = false; _fadeSeconds = preferences.FadeSeconds; _textSize = preferences.TextSize;
        UpdateShortcuts(preferences.EffectiveShortcuts());
        ToggleDrawCommand = Track(new RelayCommand(() => IsDrawing = !IsDrawing, () => !IsBusy));
        SelectToolCommand = Track(new RelayCommand<DrawingTool>(tool => { Tool = tool; ShowInk = true; IsDrawing = true; }, tool => !IsBusy && (tool != DrawingTool.Pan || CanNavigateCanvas)));
        SelectColorCommand = Track(new RelayCommand<string>(color => { if (color is not null) Color = color; }, _ => !IsBusy));
        IRelayCommand Make(CanvasAction action, Func<bool>? enabled = null) =>
            Track(new RelayCommand(() => dispatch(action), () => !IsBusy && (enabled?.Invoke() ?? true)));
        CycleBoardCommand = Make(CanvasAction.CycleBoard);
        ToggleBackgroundCommand = Make(CanvasAction.ToggleBackground, () => !_isDesktop);
        UndoCommand = Make(CanvasAction.Undo, () => _canUndo);
        RedoCommand = Make(CanvasAction.Redo, () => _canRedo);
        ClearCommand = Make(CanvasAction.Clear, () => _itemCount > 0);
        ExportCommand = Make(CanvasAction.Export);
        ExitCommand = Make(CanvasAction.Exit);
        NewLessonCommand = Make(CanvasAction.NewLesson);
        OpenLessonCommand = Make(CanvasAction.OpenLesson);
        SaveLessonCommand = Make(CanvasAction.SaveLesson);
        SaveLessonAsCommand = Make(CanvasAction.SaveLessonAs);
        PreviousPageCommand = Make(CanvasAction.PreviousPage, () => _canPrevious);
        NextPageCommand = Make(CanvasAction.NextPage, () => _canNext);
        AddPageCommand = Make(CanvasAction.AddPage, () => _canAdd);
        DuplicatePageCommand = Make(CanvasAction.DuplicatePage, () => _canAdd && !_isDesktop);
        RenamePageCommand = Make(CanvasAction.RenamePage, () => _canDelete || _boardLabel != "Board: desktop");
        DeletePageCommand = Make(CanvasAction.DeletePage, () => _canDelete);
        RestorePageCommand = Make(CanvasAction.RestorePage, () => _canRestore);
        ClearTemporaryCommand = Make(CanvasAction.ClearTemporary);
        SettingsCommand = Make(CanvasAction.Settings);
        GhostCommand = Make(CanvasAction.Ghost);
        CustomColorCommand = Make(CanvasAction.CustomColor);
        CopyScreenshotCommand = Make(CanvasAction.CopyScreenshot);
        RegionScreenshotCommand = Make(CanvasAction.RegionScreenshot);
        DuplicateSelectionCommand = Make(CanvasAction.DuplicateSelection, () => SelectionCount > 0);
        DeleteSelectionCommand = Make(CanvasAction.DeleteSelection, () => SelectionCount > 0);
        FreezeCommand = Make(CanvasAction.Freeze, () => _isDesktop || FrozenScreen);
        ExportPdfCommand = Make(CanvasAction.ExportPdf);
        ImportCommand = Make(CanvasAction.Import, () => _canAdd);
        ZoomInCommand = Make(CanvasAction.ZoomIn, () => CanNavigateCanvas);
        ZoomOutCommand = Make(CanvasAction.ZoomOut, () => CanNavigateCanvas);
        ResetViewCommand = Make(CanvasAction.ResetView);
        ToggleSpotlightCommand = Make(CanvasAction.ToggleSpotlight);
        PatternNoneCommand = Make(CanvasAction.PatternNone, () => !_isDesktop);
        PatternGridCommand = Make(CanvasAction.PatternGrid, () => !_isDesktop);
        PatternRuledCommand = Make(CanvasAction.PatternRuled, () => !_isDesktop);
        RemoveImageCommand = Make(CanvasAction.RemoveImage, () => !_isDesktop);
        HideCommand = new RelayCommand(() => dispatch(CanvasAction.Hide));
        CollapseCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    private T Track<T>(T command) where T : IRelayCommand { _commands.Add(command); return command; }
    private void RefreshCommands() { foreach (var command in _commands) command.NotifyCanExecuteChanged(); }
    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) { if (value) IsDrawing = false; RefreshCommands(); } }
    }
    public bool IsDrawing
    {
        get => _isDrawing;
        set
        {
            if (value && IsBusy) return;
            if (SetProperty(ref _isDrawing, value))
            {
                OnPropertyChanged(nameof(ModeLabel)); OnPropertyChanged(nameof(ModeActionLabel));
                OnPropertyChanged(nameof(DrawTip)); NotifyToolState();
            }
        }
    }
    public string ModeLabel => IsDrawing ? "DRAW" : "INTERACT";
    public string ModeActionLabel => IsDrawing ? "Use desktop" : "Start drawing";
    public bool ShowInk { get => _showInk; set => SetProperty(ref _showInk, value); }
    public bool HaloEnabled { get => _haloEnabled; set => SetProperty(ref _haloEnabled, value); }
    public bool TouchEnabled { get => _touchEnabled; set => SetProperty(ref _touchEnabled, value); }
    public bool PressureEnabled { get => _pressureEnabled; set => SetProperty(ref _pressureEnabled, value); }
    public bool PalmRejection { get => _palmRejection; set => SetProperty(ref _palmRejection, value); }
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (SetProperty(ref _isExpanded, value)) OnPropertyChanged(nameof(ExpandTip)); }
    }
    private void NotifyToolState()
    {
        OnPropertyChanged(nameof(IsPrimaryToolActive)); OnPropertyChanged(nameof(IsEraserActive));
        OnPropertyChanged(nameof(PrimaryToolGlyph)); OnPropertyChanged(nameof(PrimaryToolTip));
    }
    public DrawingTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _toolWidths[_tool.ToString()] = _thickness;
            if (value == DrawingTool.Text) _previousTool = _tool;
            if (SetProperty(ref _tool, value))
            {
                if (value != DrawingTool.Eraser) _primaryTool = value;
                NotifyToolState();
                _thickness = _toolWidths.GetValueOrDefault(value.ToString(), 4);
                OnPropertyChanged(nameof(Thickness));
            }
        }
    }
    public void ReturnFromText() => Tool = _previousTool;
    public string Color { get => _color; set { if (LessonStore.IsColor(value)) SetProperty(ref _color, value); } }
    public double Thickness
    {
        get => _thickness;
        set { if (double.IsFinite(value) && SetProperty(ref _thickness, Math.Clamp(value, 1, 20))) _toolWidths[_tool.ToString()] = _thickness; }
    }
    public double TextSize { get => _textSize; set => SetProperty(ref _textSize, Math.Clamp(value, 8, 200)); }
    public IReadOnlyList<int> FadeDurations { get; } = new[] { 1, 3, 5 };
    public int FadeSeconds { get => _fadeSeconds; set => SetProperty(ref _fadeSeconds, value is 1 or 3 or 5 ? value : 3); }
    public string Notice { get => _notice; set => SetProperty(ref _notice, value); }
    public string LessonLabel { get => _lessonLabel; private set => SetProperty(ref _lessonLabel, value); }
    public string PageLabel { get => _pageLabel; private set => SetProperty(ref _pageLabel, value); }
    public string BoardLabel { get => _boardLabel; private set => SetProperty(ref _boardLabel, value); }
    public string ShortcutHint { get => _shortcutHint; private set => SetProperty(ref _shortcutHint, value); }
    public void UpdateShortcuts(IReadOnlyDictionary<string, uint> keys)
    {
        _shortcuts = new(keys); _shortcutRevision++;
        ShortcutHint = $"Ctrl + Alt + {ShortcutCatalog.KeyName(keys["Draw"])} · Draw / interact\nCtrl + Alt + {ShortcutCatalog.KeyName(keys["Hide"])} · Hide / show all\nCtrl + Alt + {ShortcutCatalog.KeyName(keys["Ghost"])} · Ghost Mode";
        OnPropertyChanged(nameof(ShortcutRevision)); OnPropertyChanged(nameof(PrimaryToolTip));
        OnPropertyChanged(nameof(DrawTip)); OnPropertyChanged(nameof(ExpandTip)); OnPropertyChanged(nameof(WidthTip));
    }
    public string GetToolTip(string action, string label, string? localKey = null)
    {
        var key = _shortcuts.GetValueOrDefault(action);
        var shortcut = key == 0 ? "Global shortcut: not assigned" : "Ctrl + Alt + " + ShortcutCatalog.KeyName(key);
        return label + "\n" + shortcut + (string.IsNullOrWhiteSpace(localKey) ? "" : "\n" + localKey + " (toolbar focused)");
    }

    private void RestoreAvailableTool()
    {
        if (!CanNavigateCanvas && Tool == DrawingTool.Pan) Tool = DrawingTool.Pen;
    }
    public void UpdateWorkspace(LessonWorkspace workspace, int strokeCount)
    {
        _isDesktop = workspace.IsDesktop;
        RestoreAvailableTool();
        OnPropertyChanged(nameof(CanNavigateCanvas));
        _canUndo = workspace.CanUndo; _canRedo = workspace.CanRedo;
        _itemCount = strokeCount + workspace.ActivePage.Texts.Count;
        _canPrevious = workspace.ActivePageIndex > 0 || workspace.IsDesktop;
        _canNext = workspace.ActivePageIndex < workspace.PageCount - 1 || workspace.IsDesktop;
        _canDelete = !workspace.IsDesktop && workspace.PageCount > 1;
        _canRestore = workspace.CanRestorePage;
        _canAdd = workspace.ImportCapacity > 0;
        LessonLabel = (workspace.FilePath is { } path ? Path.GetFileName(path) : "Untitled lesson") + (workspace.IsDirty ? " *" : "");
        PageLabel = $"{workspace.ActivePageIndex + 1} / {workspace.PageCount} · {workspace.BoardPageName}";
        BoardLabel = workspace.IsDesktop ? "Board: desktop" : workspace.ActivePage.Background == PageBackground.White ? "Board: white" : "Board: black";
        RefreshCommands();
    }

    public IRelayCommand ToggleDrawCommand { get; }
    public IRelayCommand<DrawingTool> SelectToolCommand { get; }
    public IRelayCommand<string> SelectColorCommand { get; }
    public IRelayCommand CycleBoardCommand { get; }
    public IRelayCommand ToggleBackgroundCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand ExportCommand { get; }
    public IRelayCommand HideCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand CollapseCommand { get; }
    public IRelayCommand NewLessonCommand { get; }
    public IRelayCommand OpenLessonCommand { get; }
    public IRelayCommand SaveLessonCommand { get; }
    public IRelayCommand SaveLessonAsCommand { get; }
    public IRelayCommand PreviousPageCommand { get; }
    public IRelayCommand NextPageCommand { get; }
    public IRelayCommand AddPageCommand { get; }
    public IRelayCommand DuplicatePageCommand { get; }
    public IRelayCommand RenamePageCommand { get; }
    public IRelayCommand DeletePageCommand { get; }
    public IRelayCommand RestorePageCommand { get; }
    public IRelayCommand ClearTemporaryCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand GhostCommand { get; }
    public IRelayCommand CustomColorCommand { get; }
    public IRelayCommand CopyScreenshotCommand { get; }
    public IRelayCommand RegionScreenshotCommand { get; }
    public IRelayCommand DuplicateSelectionCommand { get; }
    public IRelayCommand DeleteSelectionCommand { get; }
    public IRelayCommand FreezeCommand { get; }
    public IRelayCommand ExportPdfCommand { get; }
    public IRelayCommand ImportCommand { get; }
    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand ResetViewCommand { get; }
    public IRelayCommand ToggleSpotlightCommand { get; }
    public IRelayCommand PatternNoneCommand { get; }
    public IRelayCommand PatternGridCommand { get; }
    public IRelayCommand PatternRuledCommand { get; }
    public IRelayCommand RemoveImageCommand { get; }
}
