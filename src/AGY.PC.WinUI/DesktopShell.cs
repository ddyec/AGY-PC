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
    private bool changing, narrow;
    internal string Page { get; private set; } = "chat";
    internal event Action<string>? Navigated;

    internal DesktopShell(
        ListView history,
        TextBox search,
        TextBlock connection,
        TextBlock heading,
        Action newChat,
        Action chooseProject,
        Action? refreshHistory = null,
        Action? openNetwork = null) {
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

        // Cohesive New Conversation button
        var createContent = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, ColumnSpacing = 10, Margin = new Thickness(4, 0, 2, 0) };
        createContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        createContent.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        createContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var plusIcon = Ui.FluentGlyph("\uE710", 13.5);
        createContent.Children.Add(plusIcon);
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
        create.MinHeight = 38;
        create.Padding = new Thickness(12, 8, 12, 8);
        create.CornerRadius = new CornerRadius(10);
        create.HorizontalAlignment = HorizontalAlignment.Stretch;
        create.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(create, 1);
        paneGrid.Children.Add(create);

        // Primary navigation destinations with unified Fluent icon badges and breathing room from selection indicator
        foreach (var item in new[] {
            ("chat", "对话", "\uE8BD"),
            ("files", "文件与产物", "\uE8B7"),
            ("skills", "技能与扩展", "\uE945"),
            ("build", "混合构建", "\uE90F"),
            ("settings", "配置与额度", "\uE713")
        }) {
            var badge = Ui.NavIconBadge(item.Item3, 26, 13.5);
            var label = Ui.Text(item.Item2, 13.5);
            label.FontWeight = new() { Weight = 500 };
            var row = Ui.Inline(badge, label);
            row.Spacing = 10;
            row.Margin = new Thickness(6, 0, 2, 0);
            destinations.Items.Add(new ListViewItem {
                Content = row,
                Tag = item.Item1,
                MinHeight = 38,
                Padding = new Thickness(8, 5, 10, 5),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });
        }
        Grid.SetRow(destinations, 2);
        paneGrid.Children.Add(destinations);

        // Recent conversations header + refresh action + clean search box
        search.PlaceholderText = "搜索历史对话…";
        Ui.StyleSearchBox(search);
        var divider = Ui.Divider();
        divider.Margin = new Thickness(4, 2, 4, 4);
        var sectionHeaderRow = new Grid { Margin = new Thickness(6, 0, 2, 0) };
        sectionHeaderRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        sectionHeaderRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var sectionTitle = Ui.SectionLabel("最近对话");
        sectionTitle.VerticalAlignment = VerticalAlignment.Center;
        sectionHeaderRow.Children.Add(sectionTitle);
        if (refreshHistory is not null) {
            var refreshBtn = Ui.Icon(Symbol.Refresh, "刷新历史会话列表", refreshHistory);
            refreshBtn.Width = 26;
            refreshBtn.Height = 26;
            refreshBtn.MinWidth = 26;
            refreshBtn.MinHeight = 26;
            Grid.SetColumn(refreshBtn, 1);
            sectionHeaderRow.Children.Add(refreshBtn);
        }
        var historyHeader = Ui.Stack(divider, sectionHeaderRow, search);
        historyHeader.Spacing = 6;
        Grid.SetRow(historyHeader, 3);
        paneGrid.Children.Add(historyHeader);

        // Session list (clean layout without redundant bullet dot colliding with selection bar)
        history.MaxHeight = double.PositiveInfinity;
        history.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        history.ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Padding='8,6,6,6'>" +
            "<TextBlock Text='{Binding DisplayName}' FontSize='13' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' ToolTipService.ToolTip='{Binding DisplayName}'/>" +
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
        var quickNet = Ui.ToolbarButton("网络", Symbol.World, "配置 DNS / DoH 与网络链路", () => (openNetwork ?? (() => Navigate("settings")))());
        quickNet.Height = 26;
        quickNet.MinHeight = 26;
        quickNet.Padding = new Thickness(8, 2, 8, 2);
        Grid.SetColumn(quickNet, 1);
        footerGrid.Children.Add(quickNet);

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

        var titleStack = Ui.Inline(chatHeading, pageName);
        titleStack.Spacing = 10;
        Grid.SetColumn(titleStack, 1);
        top.Children.Add(titleStack);

        location.FontWeight = new() { Weight = 500 };
        var projectBtn = Ui.Button("项目", chooseProject);
        projectBtn.Content = Ui.Inline(Ui.Glyph(Symbol.Folder, 14), location);
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
        pageName.Visibility = isChat ? Visibility.Collapsed : Visibility.Visible;
        pageName.Text = page switch {
            "files" => "文件与产物",
            "skills" => "技能与扩展",
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
        location.MaxWidth = Math.Clamp(width - 420, 96, 260);
        chatHeading.MaxWidth = Math.Clamp(width - 460, 180, 680);
        var next = width < 760;
        if (next == narrow) return;
        narrow = next;
        View.DisplayMode = narrow ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        View.IsPaneOpen = !narrow;
    }
}
