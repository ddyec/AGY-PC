using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
namespace AGY.PC.WinUI;
internal static class Ui {
    public static Grid Root(){StartupLog.Mark("root.xaml.parse");var root=(Grid)XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource ApplicationPageBackgroundThemeBrush}'/>");StartupLog.Mark("root.xaml.ready");return root;}
    public static Border Card(UIElement child) {
        var card=(Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='12' Padding='20' Margin='0,8,0,8'/>");card.Child=child;return card;
    }
    public static TextBlock Text(string value,double size=14) => new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
    public static TextBlock Caption(string value) {var text=Text(value,12);text.Opacity=0.72;return text;}
    public static StackPanel Stack(params UIElement[] children){var result=new StackPanel{Spacing=12};foreach(var c in children)result.Children.Add(c);return result;}
    public static Button Button(string value,Action click,bool accent=false){var result=new Button{Content=value,MinHeight=36};if(accent)result.Style=(Style)Application.Current.Resources["AccentButtonStyle"];result.Click+=(_,_)=>click();return result;}
    public static Image Logo(double size)=>new(){Source=new BitmapImage(new Uri("ms-appx:///Assets/Mark.png")),Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Left};
    public static string Tail(string path)=>path.Split('/',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"工作区";
}
