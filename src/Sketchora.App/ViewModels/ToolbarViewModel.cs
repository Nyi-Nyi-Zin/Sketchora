using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sketchora.Core;

namespace Sketchora.App.ViewModels;

public enum DrawingTool { Pen, Highlighter, Eraser, Line, Arrow, Rectangle, Ellipse, Text, Fading }
public enum CanvasAction
{
    Undo, Redo, Clear, Export, Hide, Exit, NewLesson, OpenLesson, SaveLesson, SaveLessonAs,
    CycleBoard, ToggleBackground, PreviousPage, NextPage, AddPage, DuplicatePage, RenamePage, DeletePage, RestorePage,
    ClearTemporary, Settings, Ghost, CustomColor, CopyScreenshot, RegionScreenshot
}

public sealed class ToolbarViewModel : ObservableObject
{
    private readonly List<IRelayCommand> _commands = [];
    private bool _isDrawing, _isBusy;
    private bool _showInk = true, _haloEnabled, _touchEnabled, _isExpanded;
    private DrawingTool _tool, _previousTool;
    private string _color, _notice = "Ready · Desktop ink stays fixed when content scrolls.";
    private string _lessonLabel = "Untitled lesson", _pageLabel = "Board 1 / 1", _boardLabel = "Board: desktop", _shortcutHint = "";
    private double _thickness, _textSize;
    private bool _pressureEnabled, _palmRejection;
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
        _pressureEnabled = preferences.PressureEnabled; _palmRejection = preferences.PalmRejection;
        _haloEnabled = preferences.PointerHalo; _touchEnabled = preferences.TouchDrawing;
        _isExpanded = preferences.ExpandedToolbar; _fadeSeconds = preferences.FadeSeconds; _textSize = preferences.TextSize;
        UpdateShortcuts(preferences.EffectiveShortcuts());
        ToggleDrawCommand = Track(new RelayCommand(() => IsDrawing = !IsDrawing, () => !IsBusy));
        SelectToolCommand = Track(new RelayCommand<DrawingTool>(tool => { Tool = tool; ShowInk = true; IsDrawing = true; }, _ => !IsBusy));
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
            if (SetProperty(ref _isDrawing, value)) { OnPropertyChanged(nameof(ModeLabel)); OnPropertyChanged(nameof(ModeActionLabel)); }
        }
    }
    public string ModeLabel => IsDrawing ? "DRAW" : "INTERACT";
    public string ModeActionLabel => IsDrawing ? "Use desktop" : "Start drawing";
    public bool ShowInk { get => _showInk; set => SetProperty(ref _showInk, value); }
    public bool HaloEnabled { get => _haloEnabled; set => SetProperty(ref _haloEnabled, value); }
    public bool TouchEnabled { get => _touchEnabled; set => SetProperty(ref _touchEnabled, value); }
    public bool PressureEnabled { get => _pressureEnabled; set => SetProperty(ref _pressureEnabled, value); }
    public bool PalmRejection { get => _palmRejection; set => SetProperty(ref _palmRejection, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
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
    public void UpdateShortcuts(IReadOnlyDictionary<string, uint> keys) => ShortcutHint =
        $"Ctrl + Alt + {ShortcutCatalog.KeyName(keys["Draw"])} · Draw / interact\nCtrl + Alt + {ShortcutCatalog.KeyName(keys["Hide"])} · Hide / show all\nCtrl + Alt + {ShortcutCatalog.KeyName(keys["Ghost"])} · Ghost Mode";

    public void UpdateWorkspace(LessonWorkspace workspace, int strokeCount)
    {
        _isDesktop = workspace.IsDesktop;
        _canUndo = workspace.CanUndo; _canRedo = workspace.CanRedo;
        _itemCount = strokeCount + workspace.ActivePage.Texts.Count;
        _canPrevious = workspace.ActivePageIndex > 0 || workspace.IsDesktop;
        _canNext = workspace.ActivePageIndex < workspace.PageCount - 1 || workspace.IsDesktop;
        _canDelete = !workspace.IsDesktop && workspace.PageCount > 1;
        _canRestore = workspace.CanRestorePage;
        _canAdd = workspace.PageCount < LessonStore.MaximumPages;
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
}
