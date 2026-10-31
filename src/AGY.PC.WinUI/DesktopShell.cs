using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AGY.PC.WinUI;

// The session list always has a full-width pane. Narrow windows use an overlay,
// never the NavigationView compact rail that clipped the custom history content.
internal sealed class DesktopShell {
    internal SplitView View { get; } = new() {
        OpenPaneLength = 264,
        CompactPaneLength = 0,
        DisplayMode = SplitViewDisplayMode.Inline,
        IsPaneOpen = true
    };
    internal Grid Pages { get; } = new();
    private readonly ListView destinations = new() {
        SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true
    };
    private readonly TextBlock location = Ui.Text("工作区", 12.5);
    private readonly TextBlock pageName = Ui.Title("对话", 16);
    private readonly TextBlock chatHeading;
    private readonly Button policyButton;
    private bool changing, narrow;
    internal string Page { get; private set; } = "chat";
    internal event Action<string>? Navigated;

    internal DesktopShell(
        ListView history,
        TextBox search,
        TextBlock connection,
        TextBlock heading,
        TextBlock taskSummary,
        Action newChat,
        Action chooseProject,
        Action openTaskOptions) {
        chatHeading = heading;
        chatHeading.FontSize = 16;
        chatHeading.FontWeight = new() { Weight = 600 };
        chatHeading.TextWrapping = TextWrapping.NoWrap;
        chatHeading.TextTrimming = TextTrimming.CharacterEllipsis;
        chatHeading.VerticalAlignment = VerticalAlignment.Center;

        var paneGrid = new Grid { Padding = new Thickness(14, 16, 14, 14), RowSpacing = 10 };
        foreach (var size in new[] {
            GridLength.Auto,
            GridLength.Auto,
            GridLength.Auto,
            GridLength.Auto,
            new GridLength(1, GridUnitType.Star),
            GridLength.Auto
        }) {
            paneGrid.RowDefinitions.Add(new() { Height = size });
        }

        // Brand row
        var brandRow = new Grid { Margin = new Thickness(6, 0, 4, 4), ColumnSpacing = 10 };
        brandRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brandRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        brandRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brandRow.Children.Add(Ui.Logo(26));
        var brandTitle = Ui.Title("AGY", 19);
        brandTitle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(brandTitle, 1);
        brandRow.Children.Add(brandTitle);
        var pcTag = Ui.Surface(Ui.Caption("PC"), "LayerFillColorAltBrush", new Thickness(7, 2, 7, 2), 6);
        pcTag.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pcTag, 2);
        brandRow.Children.Add(pcTag);
        paneGrid.Children.Add(brandRow);

        // New conversation button (sleek elevated card-button rather than glaring full-width neon bar)
        var createContent = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, ColumnSpacing = 10 };
        createContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        createContent.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        createContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var plusBadge = Ui.Surface(
            new SymbolIcon(Symbol.Add) { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            "LayerFillColorAltBrush",
            new Thickness(0),
            6,
            false);
        plusBadge.Width = 24;
        plusBadge.Height = 24;
        createContent.Children.Add(plusBadge);
        var createLabel = Ui.Title("新建对话", 13.5);
        createLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(createLabel, 1);
        createContent.Children.Add(createLabel);
        var shortcutHint = Ui.Caption("Ctrl+N");
        shortcutHint.FontSize = 11;
        shortcutHint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(shortcutHint, 2);
        createContent.Children.Add(shortcutHint);

        var create = Ui.Button("新建对话", newChat);
        create.Content = createContent;
        create.Padding = new Thickness(10, 8, 12, 8);
        create.CornerRadius = new CornerRadius(10);
        create.HorizontalAlignment = HorizontalAlignment.Stretch;
        create.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(create, 1);
        paneGrid.Children.Add(create);

        // Primary navigation destinations
        foreach (var item in new[] {
            ("chat", "对话", Symbol.Message),
            ("files", "文件与产物", Symbol.Folder),
            ("build", "混合构建", Symbol.Repair),
            ("settings", "配置与额度", Symbol.Setting)
        }) {
            var icon = new SymbolIcon(item.Item3) { Width = 16, Height = 16 };
            var label = Ui.Text(item.Item2, 13.5);
            var row = Ui.Inline(icon, label);
            row.Spacing = 10;
            destinations.Items.Add(new ListViewItem {
                Content = row,
                Tag = item.Item1,
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });
        }
        Grid.SetRow(destinations, 2);
        paneGrid.Children.Add(destinations);

        // Recent conversations header + clean search box
        search.PlaceholderText = "搜索历史对话…";
        Ui.StyleSearchBox(search);
        var divider = Ui.Divider();
        divider.Margin = new Thickness(4, 2, 4, 4);
        var sectionTitle = Ui.SectionLabel("最近对话");
        sectionTitle.Margin = new Thickness(6, 0, 4, 0);
        var historyHeader = Ui.Stack(divider, sectionTitle, search);
        historyHeader.Spacing = 8;
        Grid.SetRow(historyHeader, 3);
        paneGrid.Children.Add(historyHeader);

        // Session list
        history.MaxHeight = double.PositiveInfinity;
        history.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        history.ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Padding='6,7' ColumnSpacing='8'>" +
            "<Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" +
            "<Border Width='6' Height='6' CornerRadius='3' Background='{ThemeResource TextFillColorTertiaryBrush}' VerticalAlignment='Center' Opacity='0.65'/>" +
            "<TextBlock Grid.Column='1' Text='{Binding Name}' FontSize='13' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' ToolTipService.ToolTip='{Binding Name}'/>" +
            "</Grid></DataTemplate>");
        Grid.SetRow(history, 4);
        paneGrid.Children.Add(history);

        // Connection status footer bar
        var statusDot = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='8' Height='8' CornerRadius='4' Background='{ThemeResource SystemFillColorSuccessBrush}' VerticalAlignment='Center'/>");
        connection.FontSize = 12;
        connection.TextWrapping = TextWrapping.NoWrap;
        connection.TextTrimming = TextTrimming.CharacterEllipsis;
        var statusLeft = Ui.Inline(statusDot, connection);
        statusLeft.Spacing = 8;

        var footerGrid = new Grid { ColumnSpacing = 8 };
        footerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footerGrid.Children.Add(statusLeft);
        var quickSettings = Ui.Icon(Symbol.Setting, "配置与额度", () => Navigate("settings"));
        quickSettings.Width = 28;
        quickSettings.Height = 28;
        quickSettings.MinWidth = 28;
        quickSettings.MinHeight = 28;
        Grid.SetColumn(quickSettings, 1);
        footerGrid.Children.Add(quickSettings);

        var footerCard = Ui.Surface(footerGrid, "CardBackgroundFillColorDefaultBrush", new Thickness(12, 7, 8, 7), 10);
        Grid.SetRow(footerCard, 5);
        paneGrid.Children.Add(footerCard);

        var paneWrapper = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource LayerFillColorDefaultBrush}' BorderBrush='{ThemeResource DividerStrokeColorDefaultBrush}' BorderThickness='0,0,1,0'/>");
        paneWrapper.Child = paneGrid;
        View.Pane = paneWrapper;

        // Main workspace content area with unified top header
        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid { Padding = new Thickness(18, 10, 20, 10), ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var toggle = Ui.Icon(Symbol.GlobalNavigationButton, "展开或收起会话侧栏", () => View.IsPaneOpen = !View.IsPaneOpen);
        top.Children.Add(toggle);

        taskSummary.FontSize = 12;
        taskSummary.TextWrapping = TextWrapping.NoWrap;
        policyButton = Ui.Button("任务策略", openTaskOptions);
        policyButton.Content = taskSummary;
        policyButton.MinHeight = 28;
        policyButton.Height = 28;
        policyButton.Padding = new Thickness(10, 3, 10, 3);
        policyButton.CornerRadius = new CornerRadius(6);
        ToolTipService.SetToolTip(policyButton, "点击配置模型、思考强度与审批策略");

        var titleStack = Ui.Inline(chatHeading, pageName, policyButton);
        titleStack.Spacing = 10;
        Grid.SetColumn(titleStack, 1);
        top.Children.Add(titleStack);

        location.FontWeight = new() { Weight = 500 };
        var projectBtn = Ui.Button("项目", chooseProject);
        projectBtn.Content = Ui.Inline(new SymbolIcon(Symbol.Folder) { Width = 14, Height = 14 }, location);
        projectBtn.MinHeight = 32;
        projectBtn.Height = 32;
        projectBtn.Padding = new Thickness(11, 5, 11, 5);
        projectBtn.CornerRadius = new CornerRadius(8);
        ToolTipService.SetToolTip(projectBtn, "切换当前服务器工作区项目");
        Grid.SetColumn(projectBtn, 2);
        top.Children.Add(projectBtn);

        content.Children.Add(top);

        var topDivider = Ui.Divider();
        Grid.SetRow(topDivider, 1);
        content.Children.Add(topDivider);

        Pages.Margin = new Thickness(24, 12, 24, 16);
        Grid.SetRow(Pages, 2);
        content.Children.Add(Pages);
        View.Content = content;

        destinations.SelectionChanged += (_, _) => {
            if (!changing && destinations.SelectedItem is ListViewItem item)
                Navigate(item.Tag as string ?? "chat");
        };
    }

    internal void Navigate(string page) {
        Page = page;
        changing = true;
        destinations.SelectedItem = destinations.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag as string == page);
        changing = false;
        var isChat = page == "chat";
        chatHeading.Visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        policyButton.Visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        pageName.Visibility = isChat ? Visibility.Collapsed : Visibility.Visible;
        pageName.Text = page switch {
            "files" => "文件与产物",
            "build" => "混合构建",
            "settings" => "配置与额度",
            _ => "对话"
        };
        if (narrow) View.IsPaneOpen = false;
        Navigated?.Invoke(page);
    }

    internal void SetProject(string project) {
        location.TextWrapping = TextWrapping.NoWrap;
        location.Text = project;
        location.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    internal void Resize(double width) {
        location.MaxWidth = Math.Clamp(width - 520, 72, 200);
        chatHeading.MaxWidth = Math.Clamp(width - 560, 120, 420);
        var next = width < 960;
        if (next == narrow) return;
        narrow = next;
        View.DisplayMode = narrow ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        View.IsPaneOpen = !narrow;
    }
}
