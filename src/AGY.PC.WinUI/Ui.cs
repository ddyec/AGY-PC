using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
namespace AGY.PC.WinUI;
internal static class Ui {
    public static Grid Root(){StartupLog.Mark("root.xaml.parse");var root=(Grid)XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource ApplicationPageBackgroundThemeBrush}'/>");StartupLog.Mark("root.xaml.ready");return root;}
    public static Border Card(UIElement child) {
        var card=(Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='12' Padding='16'/>");card.Child=child;return card;
    }
    public static Border Surface(UIElement child,string brush,Thickness padding,double radius=12) {
        var border=(Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource "+brush+"}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1'/>");
        border.Child=child;border.Padding=padding;border.CornerRadius=new CornerRadius(radius);return border;
    }
    public static TextBlock Title(string value,double size=20) {var text=Text(value,size);text.FontWeight=new(){Weight=600};return text;}
    public static StackPanel Inline(params UIElement[] children) {var panel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};foreach(var child in children)panel.Children.Add(child);return panel;}
    public static Button Action(string label,Symbol symbol,Action click,bool accent=false) {
        var button=Button(label,click,accent);button.Content=Inline(new SymbolIcon(symbol){Width=16,Height=16},Text(label,13));button.Padding=new Thickness(12,8,12,8);return button;
    }
    public static Button Icon(Symbol symbol,string label,Action click) {
        var button=Button(label,click);button.Content=new SymbolIcon(symbol){Width=16,Height=16};button.Padding=new Thickness(9);button.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);button.BorderThickness=new Thickness(0);ToolTipService.SetToolTip(button,label);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label);return button;
    }
    public static TextBlock Text(string value,double size=14) => new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=false};
    public static TextBlock Prose(string value,double size=14){var text=Text(value,size);text.IsTextSelectionEnabled=true;text.LineHeight=22;return text;}
    public static TextBlock Caption(string value) {var text=Text(value,12);text.Opacity=0.72;return text;}
    public static StackPanel Stack(params UIElement[] children){var result=new StackPanel{Spacing=12};foreach(var c in children)result.Children.Add(c);return result;}
    public static Button Button(string value,Action click,bool accent=false){var result=new Button{Content=value,MinHeight=36};if(accent)result.Style=(Style)Application.Current.Resources["AccentButtonStyle"];result.Click+=(_,_)=>click();return result;}
    public static Image Logo(double size)=>new(){Source=new BitmapImage(new Uri("ms-appx:///Assets/Mark.png")),Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Left};
    public static string Tail(string path)=>path.Split('/',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"工作区";
}
