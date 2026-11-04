using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sketchora.Core;

namespace Sketchora.App.Views;

internal static class LessonDialogs
{
    private static Window Shell(Window owner, string title, UIElement content, double width = 460) => new()
    {
        Owner = owner, Title = title, Content = content, Width = width, SizeToContent = SizeToContent.Height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(245, 247, 255))
    };
    private static Button Button(string text) => new() { Content = text, MinWidth = 85, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(4) };

    public static string? Rename(Window owner, string name)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Board name", Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = name, MaxLength = 120, Padding = new Thickness(8) };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = Button("Rename"); var cancel = Button("Cancel"); cancel.IsCancel = true;
        buttons.Children.Add(done); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var window = Shell(owner, "Rename board", panel);
        done.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    public static void Shortcuts(Window owner, char draw, char hide, Func<char, char, string?> apply)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Choose two different Ctrl + Alt + letter shortcuts.", TextWrapping = TextWrapping.Wrap });
        ComboBox Selector(char selected)
        {
            var box = new ComboBox { ItemsSource = Enumerable.Range('A', 26).Select(key => (char)key).ToArray(), SelectedItem = selected, Margin = new Thickness(0, 5, 0, 12) };
            return box;
        }
        panel.Children.Add(new TextBlock { Text = "Draw / interact", Margin = new Thickness(0, 12, 0, 0) });
        var drawBox = Selector(draw); panel.Children.Add(drawBox);
        panel.Children.Add(new TextBlock { Text = "Hide / show all (recovery shortcut)" });
        var hideBox = Selector(hide); panel.Children.Add(hideBox);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var done = Button("Apply"); var cancel = Button("Cancel"); cancel.IsCancel = true;
        buttons.Children.Add(done); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var window = Shell(owner, "Sketchora shortcut settings", panel);
        done.Click += (_, _) =>
        {
            var problem = apply((char)drawBox.SelectedItem, (char)hideBox.SelectedItem);
            if (problem is null) window.DialogResult = true;
            else error.Text = problem;
        };
        window.ShowDialog();
    }
}

internal sealed class TextEditorWindow : Window
{
    private readonly TextBox _input;
    private readonly ComboBox _size;
    private readonly ComboBox _color;
    public bool DeleteRequested { get; private set; }
    public string LabelText => _input.Text;
    public double LabelSize => double.TryParse(_size.Text, out var size) && double.IsFinite(size) ? Math.Clamp(size, 8, 200) : 24;
    public string LabelColor => _color.SelectedItem as string ?? "#6366F1";

    public TextEditorWindow(Window owner, TextLabel label, bool existing)
    {
        Owner = owner; Title = existing ? "Edit text label" : "Add text label";
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "English / မြန်မာ · Enter adds a new line", FontFamily = new FontFamily("Myanmar Text, Segoe UI"), Margin = new Thickness(0, 0, 0, 8) });
        _input = new TextBox
        {
            Text = label.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 180, MaxLength = LessonStore.MaximumTextLength, Padding = new Thickness(8),
            FontFamily = new FontFamily(label.FontFamily), FontSize = 20
        };
        panel.Children.Add(_input);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        options.Children.Add(new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center });
        _size = new ComboBox { IsEditable = true, ItemsSource = new[] { 16, 20, 24, 32, 40, 48, 64 }, Text = label.FontSize.ToString("0"), Width = 85, Margin = new Thickness(8, 0, 20, 0) };
        options.Children.Add(_size);
        options.Children.Add(new TextBlock { Text = "Color", VerticalAlignment = VerticalAlignment.Center });
        _color = new ComboBox { ItemsSource = new[] { label.Color, "#6366F1", "#EF4444", "#F59E0B", "#10B981", "#38BDF8", "#FFFFFF", "#171B29" }.Distinct().ToArray(), SelectedItem = label.Color, Width = 120, Margin = new Thickness(8, 0, 0, 0) };
        options.Children.Add(_color); panel.Children.Add(options);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button Make(string text) { var button = new Button { Content = text, MinWidth = 85, Margin = new Thickness(4), Padding = new Thickness(10, 8, 10, 8) }; buttons.Children.Add(button); return button; }
        if (existing) Make("Delete").Click += (_, _) => { DeleteRequested = true; DialogResult = true; };
        Make("Done").Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_input.Text)) DialogResult = true; };
        var cancel = Make("Cancel"); cancel.IsCancel = true;
        panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.CaretIndex = _input.Text.Length; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
