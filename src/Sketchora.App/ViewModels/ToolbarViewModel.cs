using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sketchora.Core;

namespace Sketchora.App.ViewModels;

public enum DrawingTool { Pen, Highlighter, Eraser, Line, Arrow, Rectangle, Ellipse }
public enum BoardStyle { Desktop, White, Black }
public enum CanvasAction { Undo, Redo, Clear, Export, Hide, Exit }

public sealed class ToolbarViewModel : ObservableObject
{
    private bool _isDrawing;
    private bool _showInk = true;
    private bool _haloEnabled;
    private bool _touchEnabled;
    private bool _isExpanded = true;
    private DrawingTool _tool;
    private BoardStyle _board;
    private string _color;
    private double _thickness;
    private int _strokeCount;
    private string _notice = "Ready · Desktop ink stays fixed when content scrolls.";

    public ToolbarViewModel(AppPreferences preferences, Action<CanvasAction> dispatch)
    {
        _color = preferences.Color;
        _thickness = preferences.Thickness;
        _haloEnabled = preferences.PointerHalo;
        _touchEnabled = preferences.TouchDrawing;
        ToggleDrawCommand = new RelayCommand(() => IsDrawing = !IsDrawing);
        SelectToolCommand = new RelayCommand<DrawingTool>(tool => { Tool = tool; ShowInk = true; IsDrawing = true; });
        SelectColorCommand = new RelayCommand<string>(color => { if (color is not null) Color = color; });
        CycleBoardCommand = new RelayCommand(() =>
        {
            Board = (BoardStyle)(((int)Board + 1) % 3);
            ShowInk = true;
            IsDrawing = Board != BoardStyle.Desktop;
        });
        UndoCommand = new RelayCommand(() => dispatch(CanvasAction.Undo), () => CanUndo);
        RedoCommand = new RelayCommand(() => dispatch(CanvasAction.Redo), () => CanRedo);
        ClearCommand = new RelayCommand(() => dispatch(CanvasAction.Clear), () => StrokeCount > 0);
        ExportCommand = new RelayCommand(() => dispatch(CanvasAction.Export));
        HideCommand = new RelayCommand(() => dispatch(CanvasAction.Hide));
        ExitCommand = new RelayCommand(() => dispatch(CanvasAction.Exit));
        CollapseCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public bool IsDrawing
    {
        get => _isDrawing;
        set { if (SetProperty(ref _isDrawing, value)) { OnPropertyChanged(nameof(ModeLabel)); OnPropertyChanged(nameof(ModeActionLabel)); } }
    }
    public string ModeLabel => IsDrawing ? "DRAW" : "INTERACT";
    public string ModeActionLabel => IsDrawing ? "Use desktop" : "Start drawing";
    public bool ShowInk { get => _showInk; set => SetProperty(ref _showInk, value); }
    public bool HaloEnabled { get => _haloEnabled; set => SetProperty(ref _haloEnabled, value); }
    public bool TouchEnabled { get => _touchEnabled; set => SetProperty(ref _touchEnabled, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public DrawingTool Tool { get => _tool; set => SetProperty(ref _tool, value); }
    public BoardStyle Board
    {
        get => _board;
        set { if (SetProperty(ref _board, value)) OnPropertyChanged(nameof(BoardLabel)); }
    }
    public string BoardLabel => Board switch { BoardStyle.White => "Board: white", BoardStyle.Black => "Board: black", _ => "Board: desktop" };
    public string Color { get => _color; set => SetProperty(ref _color, value); }
    public double Thickness { get => _thickness; set => SetProperty(ref _thickness, Math.Clamp(value, 1, 20)); }
    public int StrokeCount
    {
        get => _strokeCount;
        private set { if (SetProperty(ref _strokeCount, value)) ClearCommand.NotifyCanExecuteChanged(); }
    }
    public string Notice { get => _notice; set => SetProperty(ref _notice, value); }
    public bool CanUndo { get; private set; }
    public bool CanRedo { get; private set; }

    public void UpdateHistory(bool undo, bool redo, int count)
    {
        CanUndo = undo;
        CanRedo = redo;
        StrokeCount = count;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public IRelayCommand ToggleDrawCommand { get; }
    public IRelayCommand<DrawingTool> SelectToolCommand { get; }
    public IRelayCommand<string> SelectColorCommand { get; }
    public IRelayCommand CycleBoardCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand ExportCommand { get; }
    public IRelayCommand HideCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand CollapseCommand { get; }
}
