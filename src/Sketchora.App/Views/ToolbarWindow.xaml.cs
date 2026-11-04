using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Sketchora.App.Interop;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sketchora.App.ViewModels;

namespace Sketchora.App.Views;

public partial class ToolbarWindow : Window
{
    public ToolbarWindow(ToolbarViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        // A Popup owns a separate presentation source; give its choices the same model
        // before opening so their commands and live shortcut tips are ready immediately.
        ToolPopup.DataContext = ColourPopup.DataContext = CapturePopup.DataContext = model;
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 16);
        model.PropertyChanged += OnModelChanged;
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(KeepVisible));
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseFlyouts(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && (ToolPopup.IsOpen || ColourPopup.IsOpen || CapturePopup.IsOpen))
            { CloseFlyouts(); e.Handled = true; }
        };
        Closed += (_, _) => model.PropertyChanged -= OnModelChanged;
        InputBindings.Add(new KeyBinding(model.UndoCommand, new KeyGesture(Key.Z, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(model.RedoCommand, new KeyGesture(Key.Y, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(model.SaveLessonCommand, new KeyGesture(Key.S, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(model.SaveLessonAsCommand, new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift)));
        InputBindings.Add(new KeyBinding(model.OpenLessonCommand, new KeyGesture(Key.O, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(model.NewLessonCommand, new KeyGesture(Key.N, ModifierKeys.Control)));
    }

    private void KeepVisible()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) NativeMethods.ConstrainToolbar(handle);
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.IsExpanded)) CloseFlyouts();
        if (e.PropertyName == nameof(ToolbarViewModel.IsBusy) && sender is ToolbarViewModel { IsBusy: true }) CloseFlyouts();
    }
    private void CloseFlyouts() => ToolPopup.IsOpen = ColourPopup.IsOpen = CapturePopup.IsOpen = false;
    private void OnToolMenuOpened(object? sender, EventArgs e)
    {
        ColourPopup.IsOpen = CapturePopup.IsOpen = false;
        if (DataContext is ToolbarViewModel { IsBusy: false } model) { model.ShowInk = true; model.IsDrawing = true; }
    }
    private void OnToolChosen(object sender, RoutedEventArgs e) => ToolPopup.IsOpen = false;
    private void OnCaptureChosen(object sender, RoutedEventArgs e) => CapturePopup.IsOpen = false;
    private void OnCustomColorChosen(object sender, RoutedEventArgs e) => ColourPopup.IsOpen = false;

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        var target = e.OriginalSource as DependencyObject;
        while (target is not null)
        {
            if (target is ButtonBase) return;
            target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
