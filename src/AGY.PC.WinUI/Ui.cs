using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AGY.PC.WinUI;

internal static class Ui {
    public static Grid Root() {
        StartupLog.Mark("root.xaml.parse");
        var root = (Grid)XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource ApplicationPageBackgroundThemeBrush}'/>");
        StartupLog.Mark("root.xaml.ready");
        return root;
    }

    public static Border Card(UIElement child, double padding = 20, double radius = 14) {
        var card = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1'/>");
        card.CornerRadius = new CornerRadius(radius);
        card.Padding = new Thickness(padding);
        card.Child = child;
        return card;
    }

    public static Border Surface(UIElement child, string brush, Thickness padding, double radius = 12, bool bordered = true) {
        var xaml = bordered
            ? $"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{{ThemeResource {brush}}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}' BorderThickness='1'/>"
            : $"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{{ThemeResource {brush}}}' BorderThickness='0'/>";
        var border = (Border)XamlReader.Load(xaml);
        border.Child = child;
        border.Padding = padding;
        border.CornerRadius = new CornerRadius(radius);
        return border;
    }

    public static Border Divider(bool vertical = false) {
        var line = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource DividerStrokeColorDefaultBrush}'/>");
        if (vertical) {
            line.Width = 1;
            line.HorizontalAlignment = HorizontalAlignment.Stretch;
            line.VerticalAlignment = VerticalAlignment.Stretch;
        } else {
            line.Height = 1;
            line.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        return line;
    }

    public static TextBlock Title(string value, double size = 20) {
        var text = Text(value, size);
        text.FontWeight = new() { Weight = 600 };
        return text;
    }

    public static TextBlock SectionLabel(string value) {
        var text = Text(value, 11.5);
        text.FontWeight = new() { Weight = 600 };
        text.Opacity = 0.56;
        text.CharacterSpacing = 24;
        return text;
    }

    public static StackPanel Inline(params UIElement[] children) {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) {
            if (child is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(child);
        }
        return panel;
    }

    public static Button Action(string label, Symbol symbol, Action click, bool accent = false) {
        var button = Button(label, click, accent);
        var icon = new SymbolIcon(symbol) { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };
        var text = Text(label, 13);
        text.FontWeight = new() { Weight = 500 };
        button.Content = Inline(icon, text);
        button.Padding = new Thickness(12, 7, 12, 7);
        return button;
    }

    public static Button ToolbarButton(string label, Symbol? symbol, string tooltip, Action click) {
        var button = Button(label, click);
        button.MinHeight = 32;
        button.Height = 32;
        button.Padding = new Thickness(10, 4, 10, 4);
        button.CornerRadius = new CornerRadius(8);
        var text = Text(label, 12.5);
        text.TextWrapping = TextWrapping.NoWrap;
        if (symbol is { } s) {
            var icon = new SymbolIcon(s) { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            button.Content = Inline(icon, text);
        } else {
            button.Content = text;
        }
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    public static Button StarterCard(Symbol symbol, string title, string subtitle, Action click) {
        var iconBadge = Surface(
            new SymbolIcon(symbol) { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            "LayerFillColorAltBrush",
            new Thickness(0),
            8);
        iconBadge.Width = 34;
        iconBadge.Height = 34;
        iconBadge.HorizontalAlignment = HorizontalAlignment.Left;

        var heading = Title(title, 13.5);
        var desc = Caption(subtitle);
        desc.FontSize = 12;
        desc.LineHeight = 18;

        var body = Stack(iconBadge, Stack(heading, desc));
        ((StackPanel)body.Children[1]).Spacing = 4;
        body.Spacing = 12;

        var button = Button(title, click);
        button.Content = body;
        button.Padding = new Thickness(16, 14, 16, 14);
        button.CornerRadius = new CornerRadius(12);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Top;
        return button;
    }

    public static Button Icon(Symbol symbol, string label, Action click) {
        var button = Button(label, click);
        button.Content = new SymbolIcon(symbol) { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        button.Width = 34;
        button.Height = 34;
        button.MinWidth = 34;
        button.MinHeight = 34;
        button.Padding = new Thickness(0);
        button.CornerRadius = new CornerRadius(8);
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    public static TextBlock Text(string value, double size = 14) => new() {
        Text = value,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = false
    };

    public static TextBlock Prose(string value, double size = 14) {
        var text = Text(value, size);
        text.IsTextSelectionEnabled = true;
        text.LineHeight = 23;
        return text;
    }

    public static TextBlock Caption(string value) {
        var text = Text(value, 12);
        text.Opacity = 0.68;
        return text;
    }

    public static StackPanel Stack(params UIElement[] children) {
        var result = new StackPanel { Spacing = 12 };
        foreach (var c in children) result.Children.Add(c);
        return result;
    }

    public static Grid Pair(UIElement left, UIElement right, double spacing = 12) {
        var grid = new Grid { ColumnSpacing = spacing, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (left is FrameworkElement l) l.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (right is FrameworkElement r) {
            r.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(r, 1);
        }
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static Button Button(string value, Action click, bool accent = false) {
        var result = new Button {
            Content = value,
            MinHeight = 34,
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(8),
            FontSize = 13
        };
        if (accent) result.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        result.Click += (_, _) => click();
        return result;
    }

    public static Image Logo(double size, HorizontalAlignment align = HorizontalAlignment.Left) => new() {
        Source = new BitmapImage(new Uri("ms-appx:///Assets/Mark.png")),
        Width = size,
        Height = size,
        HorizontalAlignment = align,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Border BrandBadge(double boxSize = 60, double iconSize = 34) {
        var logo = Logo(iconSize, HorizontalAlignment.Center);
        var badge = Surface(logo, "CardBackgroundFillColorDefaultBrush", new Thickness(0), Math.Round(boxSize * 0.28));
        badge.Width = boxSize;
        badge.Height = boxSize;
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        return badge;
    }

    public static void CleanEditableBox(TextBox box) {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        box.Background = clear;
        box.BorderThickness = new Thickness(0);
        box.Resources["TextControlBackground"] = clear;
        box.Resources["TextControlBackgroundPointerOver"] = clear;
        box.Resources["TextControlBackgroundFocused"] = clear;
        box.Resources["TextControlBackgroundDisabled"] = clear;
        box.Resources["TextControlBorderBrush"] = clear;
        box.Resources["TextControlBorderBrushPointerOver"] = clear;
        box.Resources["TextControlBorderBrushFocused"] = clear;
        box.Resources["TextControlBorderBrushDisabled"] = clear;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
    }

    public static void StyleSearchBox(TextBox box) {
        box.CornerRadius = new CornerRadius(8);
        box.MinHeight = 34;
        box.FontSize = 12.5;
        box.Padding = new Thickness(10, 6, 10, 6);
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(1);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
    }

    public static string Tail(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "工作区";
}
