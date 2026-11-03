using System.Windows;
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
        InputBindings.Add(new KeyBinding(model.UndoCommand, new KeyGesture(Key.Z, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(model.RedoCommand, new KeyGesture(Key.Y, ModifierKeys.Control)));
    }

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
