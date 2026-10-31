using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AGY.PC.Windows;
internal static class Theme {
    public static readonly Brush Background = Brush("#101319"), Surface = Brush("#191F29"), Border = Brush("#30394A"), Muted = Brush("#9AA9C0"), Accent = Brush("#A9C4FF"), Foreground = Brush("#EEF2FA");
    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    public static void Install(Application app) {
        var window = new Style(typeof(Window)); window.Setters.Add(new Setter(Control.BackgroundProperty,Background)); window.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));
        window.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Microsoft YaHei UI, Segoe UI"))); window.Setters.Add(new Setter(Control.FontSizeProperty,14d)); app.Resources.Add(typeof(Window),window);
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.BackgroundProperty,Surface)); button.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));
        button.Setters.Add(new Setter(Control.BorderBrushProperty,Border)); button.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3))); button.Setters.Add(new Setter(Control.CursorProperty,System.Windows.Input.Cursors.Hand));
        button.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));
        app.Resources.Add(typeof(Button),button);
        var text = new Style(typeof(TextBox)); text.Setters.Add(new Setter(Control.BackgroundProperty,Background)); text.Setters.Add(new Setter(Control.ForegroundProperty,Foreground)); text.Setters.Add(new Setter(Control.BorderBrushProperty,Border)); text.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10))); text.Setters.Add(new Setter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty,Accent)); app.Resources.Add(typeof(TextBox),text);
        var password = new Style(typeof(PasswordBox));password.Setters.Add(new Setter(Control.BackgroundProperty,Background));password.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));password.Setters.Add(new Setter(Control.BorderBrushProperty,Border));password.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));app.Resources.Add(typeof(PasswordBox),password);
        var combo = new Style(typeof(ComboBox)); combo.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#172130")));combo.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8)));combo.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,5,0,10)));app.Resources.Add(typeof(ComboBox),combo);
        var check = new Style(typeof(CheckBox));check.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));check.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(6)));app.Resources.Add(typeof(CheckBox),check);
    }
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,5) };
    public static Button Button(string text, Action click) { var b = new Button { Content=text };b.Click += (_,_)=>click();return b; }
    public static Border Card(UIElement child, Thickness? padding = null) => new() { Child=child, Background=Surface, BorderBrush=Border, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=padding ?? new Thickness(16), Margin=new Thickness(0,7,0,7) };
    public static Image Logo(double size) => new() { Source=new BitmapImage(new Uri("pack://application:,,,/Assets/Mark.png")), Width=size, Height=size, Stretch=Stretch.Uniform, Margin=new Thickness(0,0,12,0) };
    public static StackPanel Stack(params UIElement[] children) { var panel=new StackPanel();foreach(var c in children)panel.Children.Add(c);return panel; }
}
