using System.Text.Json;
using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace AGY.PC.WinUI;

internal sealed class MainWindow : Window {
    private readonly Workspace work;
    private readonly Grid root = Ui.Root(), host = new();
    private readonly DesktopShell shell;
    private readonly Grid conversationBody = new();
    private readonly TextBlock quickModelLabel = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer approvalScroll = new() { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        referenceScroll = new() { MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        queueScroll = new() { MaxHeight = 156, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button quickModel = new() { MinHeight = 32, Height = 32, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(8) };
    private UIElement welcome = null!;
    private string historySignature = "", effortFamily = "", dismissedNoticeKey = "";
    private ModelFamily[]? catalog;
    private readonly InfoBar notice = new() { IsClosable = true };
    private readonly TextBox composer = new() {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 52,
        MaxHeight = 180,
        FontSize = 14,
        Padding = new Thickness(2, 2, 2, 8),
        PlaceholderText = "输入消息，或输入 @ 引用文件…"
    };
    private readonly StackPanel messages = new() { Spacing = 18, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Stretch },
        references = new() { Orientation = Orientation.Horizontal, Spacing = 6 },
        approvals = new() { Spacing = 8 },
        pending = new() { Spacing = 8 },
        queuePanel = new() { Spacing = 6 };
    private readonly ScrollViewer transcript = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock heading = Ui.Title("新对话", 16), connection = Ui.Caption("尚未连接");
    private readonly ComboBox model = new() { Header = "模型", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) },
        effort = new() { Header = "思考强度", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) },
        permission = new() { Header = "审批策略", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) },
        mode = new() { Header = "执行方式", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) },
        theme = new() { Header = "界面主题", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) },
        presentation = new() { Header = "输出展示方式", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
    private readonly TextBox customWorkspaceBox = new() { Header = "服务器工作区目录路径", PlaceholderText = "/home/ubuntu/project", CornerRadius = new CornerRadius(8) };
    private readonly TextBox address = new() { Header = "服务器地址", Text = "https://agy.yectech.uk", PlaceholderText = "https://你的服务器", CornerRadius = new CornerRadius(8) };
    private readonly PasswordBox token = new() { Header = "连接密钥", PasswordRevealMode = PasswordRevealMode.Peek, CornerRadius = new CornerRadius(8) };
    private readonly ProgressRing progress = new() { IsActive = false, Width = 20, Height = 20 };
    private readonly ListView history = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, MaxHeight = 440 };
    private readonly TextBox historySearch = new() { PlaceholderText = "搜索历史对话…" };
    private readonly Button send, queueBtn, steerBtn, stop, save, latest;
    private readonly FileBrowser files;
    private readonly BuildPane builds;
    private readonly StackPanel customSkillsHost = new() { Spacing = 8 }, workspaceSkillsHost = new() { Spacing = 8 };
    private AgySkillItem[] discoveredSkills = [];
    private readonly bool uiCheck;
    private bool busy, rendering, following = true, mentionOpen, loadingSession, pendingMessageRender;
    private int sessionLoadEpoch;
    private DateTimeOffset lastMessageRenderAt;
    private Session[] currentHistoryItems = [];
    private string signature = "", session = "";
    private readonly UIElement login, chat, skillsPage, settings;

    public MainWindow(Workspace work, bool uiCheck) {
        StartupLog.Mark("window.constructor.enter");
        this.work = work;
        this.uiCheck = uiCheck;
        Title = "AGY - PC";
        try {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary)?.WorkArea;
            if (area is { Width: > 0, Height: > 0 } w) {
                var targetW = Math.Clamp((int)(w.Width * 0.86), 1440, 2240);
                var targetH = Math.Clamp((int)(w.Height * 0.86), 900, 1360);
                var posX = w.X + Math.Max(0, (w.Width - targetW) / 2);
                var posY = w.Y + Math.Max(0, (w.Height - targetH) / 2);
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(posX, posY, targetW, targetH));
            } else {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
            }
        } catch {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
        }
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AGY.ico")); } catch (Exception e) { StartupLog.Error("window.icon.optional", e); }
        try { if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop(); } catch (Exception e) { StartupLog.Error("window.mica.optional", e); }

        Ui.CleanEditableBox(composer);
        Content = root;
        root.ActualThemeChanged += (_, _) => { SyncTitleBar(); DispatcherQueue.TryEnqueue(SyncTitleBar); };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        notice.Margin = new Thickness(16, 8, 16, 0);
        notice.Closed += (_, _) => dismissedNoticeKey = CurrentNoticeKey();
        root.Children.Add(notice);
        Grid.SetRow(host, 1);
        root.Children.Add(host);

        login = MakeLogin();
        host.Children.Add(login);

        send = Ui.Action("发送", Symbol.Send, () => Run(async () => { FlushDraft(); following = true; await work.SendAsync(); }), true);
        send.MinHeight = 32;
        send.Height = 32;
        send.Padding = new Thickness(14, 4, 14, 4);
        send.CornerRadius = new CornerRadius(8);

        queueBtn = Ui.ToolbarButton("对话排队", Symbol.Clock, "当前回复完成后自动按顺序发送（Ctrl+Enter）", () => Run(async () => {
            FlushDraft();
            following = true;
            await work.EnqueueAsync();
        }));

        steerBtn = Ui.Action("立即引导", Symbol.Forward, () => Run(async () => {
            FlushDraft();
            following = true;
            await work.SteerNowAsync();
        }), true);
        steerBtn.MinHeight = 32;
        steerBtn.Height = 32;
        steerBtn.Padding = new Thickness(12, 4, 12, 4);
        steerBtn.CornerRadius = new CornerRadius(8);
        ToolTipService.SetToolTip(steerBtn, "立即打断当前生成，直接按新输入的方向继续引导对话（Ctrl+Shift+Enter）");

        stop = Ui.ToolbarButton("停止", Symbol.Stop, "停止当前生成", () => Run(work.StopAsync));
        save = Ui.Action("保存配置", Symbol.Accept, () => Run(SaveSettingsAsync), true);
        latest = Ui.Button("↓ 回到最新消息", () => { following = true; transcript.ChangeView(null, transcript.ScrollableHeight, null); }, true);
        latest.HorizontalAlignment = HorizontalAlignment.Center;
        latest.VerticalAlignment = VerticalAlignment.Bottom;
        latest.Margin = new Thickness(0, 0, 0, 14);
        latest.Padding = new Thickness(16, 6, 16, 6);
        latest.CornerRadius = new CornerRadius(18);
        latest.Visibility = Visibility.Collapsed;

        ConfigureQuickModelButton();

        shell = new DesktopShell(
            history,
            historySearch,
            connection,
            heading,
            () => { work.NewConversation(); ChatPage(); Render(); },
            () => Run(ChooseWorkspaceAsync),
            () => Run(work.ReloadAsync),
            () => Run(NetworkSettingsAsync));
        host.Children.Add(shell.View);

        chat = MakeChat();
        skillsPage = MakeSkillsPage();
        settings = MakeSettings();
        files = new FileBrowser(this, work, false, false);
        builds = new BuildPane(this, work);
        shell.Pages.Children.Add(chat);
        shell.Pages.Children.Add(files.View);
        shell.Pages.Children.Add(skillsPage);
        shell.Pages.Children.Add(builds.View);
        shell.Pages.Children.Add(settings);
        shell.Navigated += ShowPage;
        shell.Navigate("chat");

        root.SizeChanged += (_, _) => shell.Resize(root.ActualWidth);
        root.KeyDown += (_, e) => {
            if (e.Key == VirtualKey.N && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) && work.Connected && work.Pending is null) {
                e.Handled = true;
                work.NewConversation();
                ChatPage();
                Render();
            }
        };

        composer.TextChanged += (_, _) => {
            if (rendering || !work.Connected) return;
            try {
                FlushDraft();
                UpdateSendState();
                if (composer.Text.EndsWith('@') && !mentionOpen) Run(MentionAsync);
            } catch (Exception e) { Error(e); }
        };
        composer.KeyDown += (_, e) => {
            if (e.Key != VirtualKey.Enter) return;
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl) return;
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            e.Handled = true;
            if (work.Selected?.Running == true) {
                if (shift && steerBtn.IsEnabled) {
                    Run(async () => { FlushDraft(); following = true; await work.SteerNowAsync(); });
                } else if (queueBtn.IsEnabled) {
                    Run(async () => { FlushDraft(); following = true; await work.EnqueueAsync(); });
                }
            } else if (send.IsEnabled) {
                Run(async () => { FlushDraft(); following = true; await work.SendAsync(); });
            }
        };
        composer.AllowDrop = true;
        composer.DragOver += (_, e) => e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        composer.Drop += (_, e) => Run(async () => {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 8) throw new InvalidOperationException("每批最多 8 个文件。");
            foreach (var item in items) {
                if (item is not StorageFile file) throw new InvalidOperationException("本地文件夹请先压缩；服务器目录可直接 @ 引用。");
                await work.UploadAsync(file.Path, true);
            }
        });

        transcript.ViewChanged += (_, _) => {
            if (rendering) return;
            following = transcript.ScrollableHeight - transcript.VerticalOffset < 64;
            latest.Visibility = following ? Visibility.Collapsed : Visibility.Visible;
        };
        model.SelectionChanged += (_, _) => {
            if (rendering) return;
            Efforts(work.Settings.Effort);
            AutoSaveSettings();
        };
        effort.SelectionChanged += (_, _) => { if (!rendering) AutoSaveSettings(); };
        permission.SelectionChanged += (_, _) => { if (!rendering) AutoSaveSettings(); };
        mode.SelectionChanged += (_, _) => { if (!rendering) AutoSaveSettings(); };

        var historyMenu = new MenuFlyout();
        var refreshItem = new MenuFlyoutItem { Text = "重新从服务器读取此会话" };
        refreshItem.Click += (_, _) => {
            if (history.SelectedItem is Session s) SelectSessionAsync(s.Id, forceRefresh: true);
        };
        var rename = new MenuFlyoutItem { Text = "重命名" };
        rename.Click += (_, _) => Run(RenameAsync);
        var delete = new MenuFlyoutItem { Text = "删除会话历史" };
        delete.Click += (_, _) => Run(DeleteAsync);
        historyMenu.Items.Add(refreshItem);
        historyMenu.Items.Add(rename);
        historyMenu.Items.Add(delete);
        history.ContextFlyout = historyMenu;

        historySearch.TextChanged += (_, _) => History();
        history.SelectionChanged += (_, _) => {
            if (!rendering && history.SelectedItem is Session s && s.Id != work.Selected?.Id)
                SelectSessionAsync(s.Id);
        };
        history.ItemClick += (_, e) => {
            if (e.ClickedItem is Session selected) {
                if (selected.Id != work.Selected?.Id) SelectSessionAsync(selected.Id);
                else ChatPage();
            }
        };
        theme.SelectionChanged += (_, _) => {
            if (rendering) return;
            try { work.SetTheme((theme.SelectedItem as Choice)?.Id ?? "Default"); ApplyTheme(); } catch (Exception e) { Error(e); }
        };
        presentation.SelectionChanged += (_, _) => {
            if (rendering) return;
            try {
                var chosen = (presentation.SelectedItem as Choice)?.Id ?? (presentation.SelectedItem as string) ?? "原版";
                work.SetPresentation(chosen);
                signature = "";
                Render();
            } catch (Exception e) { Error(e); }
        };

        work.Changed += () => DispatcherQueue.TryEnqueue(Render);
        root.Loaded += (_, _) => {
            SyncTitleBar();
            if (uiCheck) { _ = CaptureThemesAsync(); return; }
            if (work.SavedConnection is { } saved) Run(() => work.ConnectAsync(saved));
        };
        Closed += (_, _) => { builds.Cancel(); work.Dispose(); };
        Render();
        StartupLog.Mark("window.constructor.ready");
    }

    internal sealed record Choice(string Id, string Name) { public override string ToString() => Name; }

    private void ConfigureQuickModelButton() {
        var chevron = Ui.FluentGlyph("\uE70E", 10);
        chevron.Opacity = 0.75;
        var contentRow = Ui.Inline(quickModelLabel, chevron);
        contentRow.Spacing = 6;
        quickModel.Content = contentRow;
        ToolTipService.SetToolTip(quickModel, "选择模型与思考强度");

        var flyoutModels = new ComboBox {
            Header = "模型",
            DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 240,
            CornerRadius = new CornerRadius(8)
        };
        var flyoutEfforts = new ComboBox {
            Header = "思考强度",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 240,
            CornerRadius = new CornerRadius(8)
        };
        var syncingFlyout = false;

        void PopulateFlyoutEfforts(string preferredEffort) {
            if (flyoutModels.SelectedItem is not ModelFamily fam) return;
            var items = fam.Efforts.Select(e => new Choice(e, FileRules.EffortLabel(e))).ToArray();
            flyoutEfforts.ItemsSource = items;
            flyoutEfforts.SelectedItem = items.FirstOrDefault(x => x.Id == preferredEffort)
                ?? items.FirstOrDefault(x => x.Id == fam.DefaultEffort)
                ?? items.FirstOrDefault();
        }

        async void ApplyFlyoutSelection() {
            if (syncingFlyout || !work.Connected || work.Pending is not null) return;
            if (flyoutModels.SelectedItem is not ModelFamily fam || flyoutEfforts.SelectedItem is not Choice eff) return;
            try {
                var next = work.Settings with { Model = fam.Id, Effort = eff.Id };
                if (next == work.Settings) return;
                await work.ConfigureAsync(next);
                Render();
            } catch (Exception err) {
                Error(err);
            }
        }

        flyoutModels.SelectionChanged += (_, _) => {
            if (syncingFlyout) return;
            syncingFlyout = true;
            PopulateFlyoutEfforts(work.Settings.Effort);
            syncingFlyout = false;
            ApplyFlyoutSelection();
        };
        flyoutEfforts.SelectionChanged += (_, _) => {
            if (!syncingFlyout) ApplyFlyoutSelection();
        };

        var flyoutPanel = Ui.Stack(flyoutModels, flyoutEfforts);
        flyoutPanel.Spacing = 12;
        flyoutPanel.MinWidth = 250;

        var flyout = new Flyout {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft,
            Content = flyoutPanel
        };
        flyout.Opening += (_, _) => {
            syncingFlyout = true;
            flyoutModels.ItemsSource = work.Families;
            flyoutModels.SelectedItem = work.Families.FirstOrDefault(f => f.Id == work.Settings.Model) ?? work.Families.FirstOrDefault();
            PopulateFlyoutEfforts(work.Settings.Effort);
            syncingFlyout = false;
        };
        quickModel.Flyout = flyout;
    }

    private UIElement MakeLogin() {
        var title = Ui.Title("连接 AGY 工作区", 24);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;

        var subtitle = Ui.Caption("连接现有 AGY 服务器，与你的 iPhone 共用会话、项目和文件。");
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.TextAlignment = TextAlignment.Center;

        var header = Ui.Stack(Ui.BrandBadge(64, 38), title, subtitle);
        header.Spacing = 10;
        header.Margin = new Thickness(0, 0, 0, 6);

        if (work.SavedConnection is { } credentials) {
            address.Text = credentials.Address;
            token.Password = credentials.Token;
        }

        var connect = Ui.Button("连接 AGY", () => Run(() => work.ConnectAsync(Credentials.Validate(address.Text, token.Password))), true);
        connect.HorizontalAlignment = HorizontalAlignment.Stretch;
        connect.MinHeight = 38;
        connect.CornerRadius = new CornerRadius(10);

        var netBtn = Ui.Action("网络设置 · DoH", Symbol.World, () => Run(NetworkSettingsAsync));
        netBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        var statusRow = Ui.Inline(netBtn, progress);
        statusRow.HorizontalAlignment = HorizontalAlignment.Center;

        var note = Ui.Caption("连接密钥保存在当前 Windows 用户的加密存储中。Google 账号由服务器统一管理。");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.TextAlignment = TextAlignment.Center;
        note.FontSize = 11.5;

        var form = Ui.Stack(header, address, token, connect, statusRow, note);
        form.Spacing = 14;

        var card = Ui.Card(form, 32, 18);
        card.MaxWidth = 440;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;

        return new ScrollViewer {
            Content = card,
            Padding = new Thickness(24, 32, 24, 32),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private UIElement MakeChat() {
        var page = new Grid { RowSpacing = 12, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });

        transcript.Content = messages;
        transcript.Padding = new Thickness(0, 8, 8, 16);
        conversationBody.Children.Add(transcript);

        // Centered Hero Empty State
        var heroTitle = Ui.Title("今天，一起完成什么？", 26);
        heroTitle.HorizontalAlignment = HorizontalAlignment.Center;
        heroTitle.TextAlignment = TextAlignment.Center;

        var heroSubtitle = Ui.Caption("关联当前项目上下文，分析代码架构、上传参考资料或浏览工作区文件。");
        heroSubtitle.FontSize = 13;
        heroSubtitle.HorizontalAlignment = HorizontalAlignment.Center;
        heroSubtitle.TextAlignment = TextAlignment.Center;

        var heroHeader = Ui.Stack(Ui.BrandBadge(60, 34), heroTitle, heroSubtitle);
        heroHeader.Spacing = 10;
        heroHeader.HorizontalAlignment = HorizontalAlignment.Center;

        var card1 = Ui.StarterCard(Symbol.Document, "梳理项目结构", "阅读当前目录层级与主要程序入口", () => {
            work.UpdateDraft("请阅读当前项目，说明它的结构和主要入口。");
            composer.Text = work.Draft.Text;
            composer.Focus(FocusState.Programmatic);
        });
        var card2 = Ui.StarterCard(Symbol.Attach, "上传参考资料", "添加文档、截图或待处理的附件", () => Run(UploadAsync));
        var card3 = Ui.StarterCard(Symbol.Folder, "浏览项目文件", "查看远程工作区文件或 @ 整个文件夹", () => shell.Navigate("files"));
        var cards = new[] { card1, card2, card3 };

        var suggestionsGrid = new Grid { ColumnSpacing = 12, RowSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 680 };
        void LayoutSuggestions(double width) {
            var stackVertically = width > 0 && width < 560;
            suggestionsGrid.ColumnDefinitions.Clear();
            suggestionsGrid.RowDefinitions.Clear();
            if (stackVertically) {
                suggestionsGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 0; i < cards.Length; i++) {
                    suggestionsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    Grid.SetRow(cards[i], i);
                    Grid.SetColumn(cards[i], 0);
                }
            } else {
                suggestionsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                for (var i = 0; i < cards.Length; i++) {
                    suggestionsGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetRow(cards[i], 0);
                    Grid.SetColumn(cards[i], i);
                }
            }
        }
        foreach (var c in cards) suggestionsGrid.Children.Add(c);
        LayoutSuggestions(680);

        var greeting = Ui.Stack(heroHeader, suggestionsGrid);
        greeting.Spacing = 24;
        greeting.MaxWidth = 680;
        greeting.HorizontalAlignment = HorizontalAlignment.Stretch;
        greeting.VerticalAlignment = VerticalAlignment.Center;
        greeting.Margin = new Thickness(12, 20, 12, 20);

        var emptyFrame = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        emptyFrame.Children.Add(greeting);
        conversationBody.SizeChanged += (_, _) => {
            emptyFrame.MinHeight = Math.Max(280, conversationBody.ActualHeight - 16);
            LayoutSuggestions(conversationBody.ActualWidth);
        };
        welcome = new ScrollViewer {
            Content = emptyFrame,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        conversationBody.Children.Add(welcome);
        Canvas.SetZIndex(latest, 10);
        conversationBody.Children.Add(latest);
        page.Children.Add(conversationBody);

        // Sleek Unified Composer Island
        approvalScroll.Content = approvals;
        referenceScroll.Content = references;
        referenceScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        referenceScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        queueScroll.Content = queuePanel;

        var toolbar = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        conversationBody.SizeChanged += (_, _) => quickModel.MaxWidth = Math.Max(120, Math.Min(280, conversationBody.ActualWidth - 320));
        quickModel.MaxWidth = 260;

        var attachBtn = Ui.ToolbarButton("附件", Symbol.Attach, "上传文件到当前对话", () => Run(UploadAsync));
        var mentionBtn = Ui.ToolbarButton("@ 引用", null, "引用工作区文件、文件夹或指定路径", () => Run(MentionAsync));
        var skillsBtn = Ui.ToolbarButton("/ 技能与指令", null, "选择斜杠工作流或 Skills", () => Run(SkillsPickerAsync));
        var actions = Ui.Inline(attachBtn, mentionBtn, skillsBtn, quickModel);
        actions.Spacing = 6;
        var actionsScroll = new ScrollViewer {
            Content = actions,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        toolbar.Children.Add(actionsScroll);

        var submit = Ui.Inline(queueBtn, steerBtn, stop, send);
        submit.Spacing = 6;
        Grid.SetColumn(submit, 1);
        toolbar.Children.Add(submit);

        var input = Ui.Stack(approvalScroll, pending, queueScroll, referenceScroll, composer, toolbar);
        input.Spacing = 6;

        var box = Ui.Surface(input, "CardBackgroundFillColorDefaultBrush", new Thickness(16, 12, 14, 10), 16);
        box.MaxWidth = 880;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(box, 1);
        page.Children.Add(box);

        return page;
    }

    private UIElement CreateSkillRowCard(AgySkillItem item, Action? onDelete = null) {
        var topRow = new Grid { ColumnSpacing = 10 };
        topRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var catTag = Ui.Surface(Ui.Caption(item.Category), "LayerFillColorDefaultBrush", new Thickness(7, 2, 7, 2), 6);
        var titleStack = Ui.Inline(Ui.Title(item.Name, 14), catTag);
        titleStack.Spacing = 8;
        topRow.Children.Add(titleStack);

        var useBtn = Ui.Button("填入对话", () => {
            work.ApplySkill(item);
            ChatPage();
            Render();
            composer.Focus(FocusState.Programmatic);
        }, true);
        useBtn.MinHeight = 30;
        useBtn.Height = 30;
        useBtn.Padding = new Thickness(12, 4, 12, 4);

        if (onDelete is not null) {
            var delBtn = Ui.ToolbarButton("删除", Symbol.Delete, "删除此自定义技能", onDelete);
            var btnRow = Ui.Inline(useBtn, delBtn);
            btnRow.Spacing = 6;
            Grid.SetColumn(btnRow, 1);
            topRow.Children.Add(btnRow);
        } else {
            Grid.SetColumn(useBtn, 1);
            topRow.Children.Add(useBtn);
        }

        var desc = Ui.Caption(item.Description);
        desc.LineHeight = 19;
        var body = Ui.Stack(topRow, desc);
        body.Spacing = 6;
        return Ui.Surface(body, "LayerFillColorAltBrush", new Thickness(14, 10, 12, 10), 10);
    }

    private void RenderCustomSkills() {
        customSkillsHost.Children.Clear();
        if (work.CustomSkills.Count == 0) {
            customSkillsHost.Children.Add(Ui.Caption("尚未创建自定义技能。点击上方「新建自定义技能 / 指令」可自由添加常用提示词、专属工作流或自定义 SKILL 模板，并自动持久记忆。"));
            return;
        }
        foreach (var skill in work.CustomSkills) {
            var skillId = skill.Id;
            customSkillsHost.Children.Add(CreateSkillRowCard(skill, () => {
                work.RemoveCustomSkill(skillId);
                RenderCustomSkills();
            }));
        }
    }

    private void RenderWorkspaceSkills() {
        workspaceSkillsHost.Children.Clear();
        if (discoveredSkills.Length == 0) {
            workspaceSkillsHost.Children.Add(Ui.Caption("点击「扫描当前工作区 Skills 与规则」可自动检测项目下的 .agents/skills/、.agent/skills/、AGENTS.md 与 GEMINI.md。"));
            return;
        }
        foreach (var item in discoveredSkills) {
            workspaceSkillsHost.Children.Add(CreateSkillRowCard(item));
        }
    }

    private async Task RefreshWorkspaceSkillsAsync() {
        discoveredSkills = await work.DiscoverWorkspaceSkillsAsync();
        RenderWorkspaceSkills();
    }

    private async Task NewCustomSkillAsync() {
        var nameBox = new TextBox { Header = "技能或指令名称", PlaceholderText = "如：/code-review  或  深度重构规范", CornerRadius = new CornerRadius(8) };
        var descBox = new TextBox { Header = "用途说明", PlaceholderText = "简要说明该技能适用的场景…", CornerRadius = new CornerRadius(8) };
        var triggerBox = new TextBox {
            Header = "触发提示词或指令内容（填入对话框的文本）",
            PlaceholderText = "输入点击该技能后自动填入对话输入框的完整指令…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            CornerRadius = new CornerRadius(8)
        };
        var fileBox = new TextBox {
            Header = "自动关联引用文件路径（可选，支持自由指定 SKILL.md 或规则文件）",
            PlaceholderText = "如 .agents/skills/my-skill/SKILL.md 或留空",
            CornerRadius = new CornerRadius(8)
        };
        var stack = Ui.Stack(nameBox, descBox, triggerBox, fileBox);
        stack.MinWidth = 420;
        if (await DialogAsync("新建自定义技能 / 指令", stack, "保存技能") != ContentDialogResult.Primary) return;
        work.SaveCustomSkill(nameBox.Text, descBox.Text, triggerBox.Text, fileBox.Text);
        RenderCustomSkills();
    }

    private async Task SkillsPickerAsync() {
        var listStack = Ui.Stack();
        listStack.Spacing = 8;
        listStack.MinWidth = 480;

        if (work.CustomSkills.Count > 0) {
            listStack.Children.Add(Ui.SectionLabel("自定义技能与常用指令"));
            foreach (var item in work.CustomSkills) listStack.Children.Add(CreateSkillRowCard(item));
        }

        listStack.Children.Add(Ui.SectionLabel("ANTIGRAVITY 官方斜杠工作流"));
        foreach (var item in AgyCatalog.OfficialSlashCommands) listStack.Children.Add(CreateSkillRowCard(item));

        listStack.Children.Add(Ui.SectionLabel("ANTIGRAVITY 官方内置 SKILLS 与定制模板"));
        foreach (var item in AgyCatalog.BuiltinSkills) listStack.Children.Add(CreateSkillRowCard(item));

        if (discoveredSkills.Length > 0) {
            listStack.Children.Add(Ui.SectionLabel("当前工作区 SKILLS 与规则"));
            foreach (var item in discoveredSkills) listStack.Children.Add(CreateSkillRowCard(item));
        }

        var result = await DialogAsync("选择技能与斜杠工作流", new ScrollViewer { Content = listStack, MaxHeight = 500 }, "关闭", "打开技能与扩展页");
        if (result == ContentDialogResult.Secondary) shell.Navigate("skills");
    }

    private UIElement MakeSkillsPage() {
        var customHeader = new Grid { ColumnSpacing = 12 };
        customHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        customHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        customHeader.Children.Add(Ui.Title("自定义技能与常用指令", 16));
        var addCustomBtn = Ui.Action("新建自定义技能 / 指令", Symbol.Add, () => Run(NewCustomSkillAsync), true);
        Grid.SetColumn(addCustomBtn, 1);
        customHeader.Children.Add(addCustomBtn);
        RenderCustomSkills();
        var customCard = Ui.Card(Ui.Stack(customHeader, customSkillsHost));

        var wsHeader = new Grid { ColumnSpacing = 12 };
        wsHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        wsHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        wsHeader.Children.Add(Ui.Title("当前项目工作区 Skills 与规则（SKILL.md / AGENTS.md / GEMINI.md）", 16));
        var scanBtn = Ui.Action("扫描当前工作区", Symbol.Refresh, () => Run(RefreshWorkspaceSkillsAsync));
        Grid.SetColumn(scanBtn, 1);
        wsHeader.Children.Add(scanBtn);
        RenderWorkspaceSkills();
        var wsCard = Ui.Card(Ui.Stack(wsHeader, workspaceSkillsHost));

        var builtinStack = Ui.Stack(Ui.Title("Antigravity 官方内置 Skills", 16));
        foreach (var item in AgyCatalog.BuiltinSkills) builtinStack.Children.Add(CreateSkillRowCard(item));
        var builtinCard = Ui.Card(builtinStack);

        var slashStack = Ui.Stack(Ui.Title("Antigravity 官方斜杠工作流", 16));
        foreach (var item in AgyCatalog.OfficialSlashCommands) slashStack.Children.Add(CreateSkillRowCard(item));
        var slashCard = Ui.Card(slashStack);

        var panel = Ui.Stack(customCard, wsCard, builtinCard, slashCard);
        panel.Spacing = 16;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Margin = new Thickness(0, 4, 12, 16);
        return new ScrollViewer {
            Content = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private UIElement MakeSettings() {
        permission.Items.Add(new Choice("review", "Restriction · 逐项审批"));
        permission.Items.Add(new Choice("turbo", "Turbo · 自动执行"));
        mode.Items.Add(new Choice("plan", "先做计划"));
        mode.Items.Add(new Choice("accept-edits", "直接实施"));
        theme.Items.Add(new Choice("Default", "跟随 Windows"));
        theme.Items.Add(new Choice("Light", "浅色"));
        theme.Items.Add(new Choice("Dark", "深色"));
        presentation.Items.Add(new Choice("步骤摘要", "思考摘要 · 直接展示思考过程，自动折叠工具调用"));
        presentation.Items.Add(new Choice("结论优先", "结论优先 · 直接展示结论，折叠思考与工具"));
        presentation.Items.Add(new Choice("原版", "原版 · 展示全部思考、工具与结论"));

        var taskButtons = Ui.Inline(save, Ui.Action("刷新可用模型", Symbol.Refresh, () => Run(work.RefreshModelsAsync)));
        var tasks = Ui.Stack(
            Ui.Title("模型与默认任务策略", 16),
            Ui.Pair(model, effort),
            Ui.Pair(permission, mode),
            taskButtons);
        tasks.Spacing = 14;

        var workspaceActions = Ui.Inline(
            Ui.Button("使用指定路径", () => Run(async () => {
                if (string.IsNullOrWhiteSpace(customWorkspaceBox.Text)) throw new InvalidOperationException("请输入服务器工作区路径。");
                await work.ConfigureAsync(work.Settings with { Workspace = customWorkspaceBox.Text.Trim() });
            }), true),
            Ui.Action("浏览服务器目录…", Symbol.Folder, () => Run(ChooseWorkspaceAsync)));
        var project = Ui.Stack(
            Ui.Title("当前服务器工作区", 16),
            customWorkspaceBox,
            workspaceActions);
        project.Spacing = 12;

        var appearance = Ui.Stack(
            Ui.Title("界面外观与输出展示方式", 16),
            Ui.Pair(theme, presentation),
            Ui.Action("打开技能与扩展（Skills / 斜杠指令）", Symbol.Setting, () => shell.Navigate("skills")));
        appearance.Spacing = 12;

        var accountActions = Ui.Inline(
            Ui.Action("查看真实账号额度", Symbol.Contact, () => Run(LimitsAsync), true),
            Ui.Action("网络设置 · DoH", Symbol.World, () => Run(NetworkSettingsAsync)),
            Ui.Action("切换服务器或连接密钥", Symbol.Switch, () => { work.Disconnect(); Render(); }));
        var account = Ui.Stack(
            Ui.Title("连接、网络与账号额度", 16),
            accountActions);
        account.Spacing = 12;

        var panel = Ui.Stack(
            Ui.Card(tasks),
            Ui.Pair(Ui.Card(project), Ui.Card(appearance)),
            Ui.Card(account));
        panel.Spacing = 16;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Margin = new Thickness(0, 4, 12, 16);
        return new ScrollViewer {
            Content = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private void Efforts(string preferred) {
        if (model.SelectedItem is not ModelFamily family) return;
        if (effortFamily != family.Id) {
            effortFamily = family.Id;
            effort.Items.Clear();
            foreach (var e in family.Efforts) effort.Items.Add(new Choice(e, FileRules.EffortLabel(e)));
        }
        effort.SelectedItem = effort.Items.Cast<Choice>().FirstOrDefault(e => e.Id == preferred) ?? effort.Items.Cast<Choice>().FirstOrDefault(e => e.Id == family.DefaultEffort);
    }

    private async void AutoSaveSettings() {
        if (rendering || !work.Connected || work.Pending is not null) return;
        try {
            if (model.SelectedItem is not ModelFamily fam || effort.SelectedItem is not Choice e || permission.SelectedItem is not Choice p || mode.SelectedItem is not Choice m)
                return;
            var workspacePath = !string.IsNullOrWhiteSpace(customWorkspaceBox.Text)
                ? customWorkspaceBox.Text.Trim()
                : work.Settings.Workspace;
            var candidate = new TaskSettings(fam.Id, e.Id, p.Id, m.Id, workspacePath);
            if (candidate == work.Settings) return;
            await work.ConfigureAsync(candidate);
            Render();
        } catch (Exception error) {
            Error(error);
        }
    }

    private async Task SaveSettingsAsync() {
        if (model.SelectedItem is not ModelFamily fam || effort.SelectedItem is not Choice e || permission.SelectedItem is not Choice p || mode.SelectedItem is not Choice m)
            throw new InvalidOperationException("请选择有效的模型、思考强度和策略。");
        var workspacePath = !string.IsNullOrWhiteSpace(customWorkspaceBox.Text)
            ? customWorkspaceBox.Text.Trim()
            : work.Settings.Workspace;
        await work.ConfigureAsync(new(fam.Id, e.Id, p.Id, m.Id, workspacePath));
    }

    private void ShowPage(string page) {
        chat.Visibility = page == "chat" ? Visibility.Visible : Visibility.Collapsed;
        files.View.Visibility = page == "files" ? Visibility.Visible : Visibility.Collapsed;
        skillsPage.Visibility = page == "skills" ? Visibility.Visible : Visibility.Collapsed;
        builds.View.Visibility = page == "build" ? Visibility.Visible : Visibility.Collapsed;
        settings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "files" && !uiCheck) Run(files.LoadAsync);
        if (page == "skills") {
            RenderCustomSkills();
            if (!uiCheck && work.Connected && discoveredSkills.Length == 0) Run(RefreshWorkspaceSkillsAsync);
        }
    }

    internal void ChatPage() { shell.Navigate("chat"); }

    private async void SelectSessionAsync(string id, bool forceRefresh = false) {
        if (string.IsNullOrWhiteSpace(id) || !work.Connected) return;
        var epoch = ++sessionLoadEpoch;
        loadingSession = true;
        notice.IsOpen = false;
        signature = "";
        ChatPage();
        Render();
        try {
            await work.SelectAsync(id, forceRefresh);
            if (epoch == sessionLoadEpoch) {
                signature = "";
                ChatPage();
            }
        } catch (Exception error) {
            if (epoch == sessionLoadEpoch) Error(error);
        } finally {
            if (epoch == sessionLoadEpoch) {
                loadingSession = false;
                signature = "";
                Render();
            }
        }
    }

    internal async void Run(Func<Task> action) {
        if (busy) return;
        busy = true;
        notice.IsOpen = false;
        Render();
        try { await action(); } catch (Exception error) { Error(error); } finally { busy = false; Render(); }
    }

    internal void Error(Exception error) {
        notice.Title = "操作未完成";
        notice.Message = error is OperationCanceledException ? "操作已取消或超时；不会自动重发任务。" : error.Message;
        notice.Severity = InfoBarSeverity.Error;
        notice.IsOpen = true;
    }

    private string CurrentNoticeKey() =>
        (work.Selected?.Id ?? "none") + ":" + (work.Selected?.Revision ?? 0) + ":" + (work.Selected?.Error ?? work.Notice ?? "");

    private void UpdateSendState() {
        var hasInput = !string.IsNullOrWhiteSpace(composer.Text) || work.Draft.References.Length > 0;
        var isRunning = work.Selected?.Running == true;
        var canAct = work.Connected && !busy && work.Pending is null;
        if (isRunning) {
            send.Visibility = Visibility.Collapsed;
            queueBtn.Visibility = Visibility.Visible;
            queueBtn.IsEnabled = canAct && hasInput;
            steerBtn.Visibility = Visibility.Visible;
            steerBtn.IsEnabled = canAct && (hasInput || work.Queue.Count > 0);
            stop.Visibility = Visibility.Visible;
            stop.IsEnabled = !busy;
            composer.PlaceholderText = "任务运行中，可输入后续排队或立即引导指令…";
        } else {
            send.Visibility = Visibility.Visible;
            send.IsEnabled = canAct && (hasInput || work.Queue.Count > 0);
            queueBtn.Visibility = Visibility.Visible;
            queueBtn.IsEnabled = canAct && hasInput;
            steerBtn.Visibility = Visibility.Collapsed;
            stop.Visibility = Visibility.Collapsed;
            composer.PlaceholderText = "输入消息，或点「@ 引用」与「/ 技能与指令」…";
        }
    }

    private void FlushDraft() {
        if (work.Connected && !rendering) work.UpdateDraft(composer.Text);
    }

    private void History() {
        var key = historySearch.Text + "/" + string.Join("|", work.Sessions.Select(s => s.Id + ":" + s.DisplayName + ":" + s.Modified));
        var old = rendering;
        rendering = true;
        try {
            if (key != historySignature) {
                historySignature = key;
                currentHistoryItems = work.Sessions
                    .Where(s => s.DisplayName.Contains(historySearch.Text, StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(s => s.Modified)
                    .ToArray();
                history.ItemsSource = currentHistoryItems;
            }
            var target = currentHistoryItems.FirstOrDefault(s => s.Id == work.Selected?.Id);
            if (!ReferenceEquals(history.SelectedItem, target)) {
                history.SelectedItem = target;
            }
        } finally {
            rendering = old;
        }
    }

    private void RenderQueue() {
        queuePanel.Children.Clear();
        if (work.Queue.Count == 0) {
            queueScroll.Visibility = Visibility.Collapsed;
            return;
        }
        queueScroll.Visibility = Visibility.Visible;

        var topRow = new Grid { ColumnSpacing = 8 };
        topRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var queueHeader = Ui.Caption($"排队中的对话 · {work.Queue.Count} 条");
        queueHeader.FontWeight = new() { Weight = 600 };
        topRow.Children.Add(queueHeader);
        if (work.Selected?.Running != true && work.Pending is null) {
            var runNow = Ui.ToolbarButton("立即发送排队", Symbol.Send, "立即发送排队中的第一条消息", () => Run(work.DrainQueueAsync));
            Grid.SetColumn(runNow, 1);
            topRow.Children.Add(runNow);
        }
        queuePanel.Children.Add(topRow);

        for (var i = 0; i < work.Queue.Count; i++) {
            var item = work.Queue[i];
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

            var indexBadge = Ui.Surface(Ui.Caption($"#{i + 1}"), "LayerFillColorDefaultBrush", new Thickness(6, 2, 6, 2), 6);
            row.Children.Add(indexBadge);

            var previewText = FileRules.CleanText(item.Text).Replace('\r', ' ').Replace('\n', ' ');
            if (item.References.Length > 0)
                previewText = (previewText.Length > 0 ? previewText + " " : "") + $"[+{item.References.Length} 个引用]";
            var preview = Ui.Text(previewText.Length > 0 ? previewText : "（仅引用文件）", 12.5);
            preview.TextWrapping = TextWrapping.NoWrap;
            preview.TextTrimming = TextTrimming.CharacterEllipsis;
            preview.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(preview, 1);
            row.Children.Add(preview);

            var itemId = item.Id;
            var itemActions = Ui.Inline(
                Ui.ToolbarButton("立即引导", Symbol.Forward, "打断当前运行并立即发送此条消息", () => Run(() => work.SteerNowAsync(itemId))),
                Ui.ToolbarButton("编辑", Symbol.Edit, "放回输入框编辑", () => Run(() => { work.EditQueued(itemId); return Task.CompletedTask; })),
                Ui.ToolbarButton("移除", Symbol.Cancel, "取消排队此条消息", () => Run(() => { work.RemoveQueued(itemId); return Task.CompletedTask; })));
            itemActions.Spacing = 4;
            Grid.SetColumn(itemActions, 2);
            row.Children.Add(itemActions);

            queuePanel.Children.Add(Ui.Surface(row, "LayerFillColorAltBrush", new Thickness(10, 6, 8, 6), 10));
        }
    }

    private void Render() {
        if (rendering) return;
        rendering = true;
        try {
            login.Visibility = work.Connected ? Visibility.Collapsed : Visibility.Visible;
            shell.View.Visibility = work.Connected ? Visibility.Visible : Visibility.Collapsed;
            progress.IsActive = busy || loadingSession;
            connection.Text = uiCheck && work.Connected ? "离线预览 · 示例数据" : (busy || loadingSession) ? "正在处理…" : work.ConnectionLabel;

            var rawError = work.Selected?.Error ?? work.Notice;
            var noticeKey = CurrentNoticeKey();
            if (!notice.IsOpen && noticeKey != dismissedNoticeKey && !string.IsNullOrWhiteSpace(rawError)) {
                if (rawError.Contains("The stream was interrupted", StringComparison.OrdinalIgnoreCase)) {
                    notice.Title = "上一轮流式传输曾中断";
                    notice.Message = "该对话上一轮生成时流式传输被中断。你可直接输入新消息，或点右侧按钮让 AGY 继续完成任务。";
                    notice.Severity = InfoBarSeverity.Informational;
                    notice.ActionButton = Ui.Button("继续完成任务", () => Run(async () => {
                        dismissedNoticeKey = CurrentNoticeKey();
                        notice.IsOpen = false;
                        work.UpdateDraft("请继续完成刚才中断的任务。");
                        await work.SendAsync();
                    }), true);
                } else {
                    notice.Title = "服务器状态";
                    notice.Message = rawError;
                    notice.Severity = InfoBarSeverity.Warning;
                    notice.ActionButton = null;
                }
                notice.IsOpen = true;
            }
            heading.Text = work.Selected?.DisplayName ?? "新对话";
            ToolTipService.SetToolTip(heading, heading.Text);
            shell.SetProject(work.Connected ? Ui.Tail(work.Settings.Workspace) : "工作区");

            var matchedFamily = work.Families.FirstOrDefault(f => f.Id == work.Settings.Model);
            var familyName = matchedFamily?.Name ?? (string.IsNullOrWhiteSpace(work.Settings.Model) ? "选择模型" : work.Settings.Model);
            quickModelLabel.Text = familyName + " · " + FileRules.EffortLabel(work.Settings.Effort);
            ToolTipService.SetToolTip(quickModel, "切换模型与思考强度");
            quickModel.IsEnabled = !busy && work.Pending is null;
            composer.IsReadOnly = busy || work.Pending is not null;

            if (!ReferenceEquals(catalog, work.Families)) {
                catalog = work.Families;
                model.ItemsSource = catalog;
                effortFamily = "";
            }
            model.SelectedItem = matchedFamily ?? work.Families.FirstOrDefault();
            customWorkspaceBox.Text = work.Settings.Workspace;
            Efforts(work.Settings.Effort);
            permission.SelectedItem = permission.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Settings.ApprovalMode);
            mode.SelectedItem = mode.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Settings.Mode);
            save.IsEnabled = !busy && work.Pending is null;
            model.IsEnabled = effort.IsEnabled = permission.IsEnabled = mode.IsEnabled = customWorkspaceBox.IsEnabled = save.IsEnabled;
            theme.SelectedItem = theme.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Theme);
            presentation.SelectedItem = presentation.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Presentation);
            ApplyTheme();

            if (composer.Text != work.Draft.Text) composer.Text = work.Draft.Text;

            references.Children.Clear();
            foreach (var r in work.Draft.References) {
                var chip = Ui.ToolbarButton("@ " + r.Label + "  ×", null, "移除此引用", () => Run(() => { work.RemoveReference(r.Path); return Task.CompletedTask; }));
                references.Children.Add(chip);
            }

            pending.Children.Clear();
            if (work.Pending is { } waiting) {
                var pendingActions = Ui.Wrap(8, 6,
                    Ui.Caption("发送结果待确认，不会自动重发。"),
                    Ui.ToolbarButton("核对会话", null, "切换到该会话检查状态", () => SelectSessionAsync(waiting.SessionID, forceRefresh: true)),
                    Ui.ToolbarButton("按原编号重试", null, "使用原请求编号安全重发", () => Run(work.SubmitPendingAsync)),
                    Ui.ToolbarButton("取消并解锁输入", Symbol.Cancel, "清除待确认状态并解锁输入框", () => { work.ClearPending(); Render(); }));
                pending.Children.Add(pendingActions);
            }

            RenderQueue();

            approvals.Children.Clear();
            foreach (var approval in work.Selected?.Approvals ?? []) {
                var details = new Expander {
                    Header = "查看实际参数 · 仅批准这一次",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBox { Text = approval.Parameters.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 150 }
                };
                var row = Ui.Inline(
                    Ui.Button("拒绝", () => Run(() => work.AnswerAsync(approval, false))),
                    Ui.Button("允许这一次", () => Run(() => work.AnswerAsync(approval, true)), true));
                approvals.Children.Add(Ui.Card(Ui.Stack(Ui.Title("等待审批 · " + approval.Tool, 15), details, row), 14, 12));
            }

            referenceScroll.Visibility = references.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            pending.Visibility = pending.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            approvalScroll.Visibility = approvals.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var key = (work.Selected?.Id ?? "new") + ":" + (work.Selected?.Revision ?? 0) + ":" + (work.Selected?.Running == true ? "run" : "idle") + ":" + work.Presentation + ":" + (loadingSession ? "loading" : "ready");
            if (signature != key) {
                var now = DateTimeOffset.UtcNow;
                var sessionChanged = session != (work.Selected?.Id ?? "new");
                if (!sessionChanged && work.Selected?.Running == true && (now - lastMessageRenderAt).TotalMilliseconds < 240) {
                    if (!pendingMessageRender) {
                        pendingMessageRender = true;
                        _ = Task.Delay(250).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => {
                            pendingMessageRender = false;
                            Render();
                        }));
                    }
                } else {
                    signature = key;
                    lastMessageRenderAt = now;
                    RenderMessages();
                }
            }
            History();
            history.IsEnabled = true;
            UpdateSendState();
        } finally {
            rendering = false;
        }
    }

    private void ApplyTheme() {
        root.RequestedTheme = work.Theme switch {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        SyncTitleBar();
    }

    private void SyncTitleBar() {
        try {
            var bar = AppWindow.TitleBar;
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Microsoft.UI.Windowing.AppWindowTitleBar", "PreferredTheme"))
                bar.PreferredTheme = root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Windowing.TitleBarTheme.Dark : Microsoft.UI.Windowing.TitleBarTheme.Light;
            if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) { bar.ResetToDefault(); return; }
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
            var background = (root.Background as SolidColorBrush)?.Color ?? (root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
            var foreground = root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = background;
            bar.ForegroundColor = bar.ButtonForegroundColor = foreground;
            bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
            bar.ButtonHoverBackgroundColor = bar.ButtonPressedBackgroundColor = null;
            bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = null;
        } catch (Exception e) { StartupLog.Error("window.titlebar.theme", e); }
    }

    private async Task NetworkSettingsAsync() {
        var current = work.Network;
        var proxyBox = new TextBox {
            Header = "HTTP 代理策略（留空自动跟随系统代理，填 direct 强制直连，或指定代理地址）",
            PlaceholderText = "留空自动跟随系统代理 / direct / http://127.0.0.1:7890",
            Text = current.ProxyUrl ?? "",
            CornerRadius = new CornerRadius(8)
        };
        var enable = new ToggleSwitch { Header = "加密 DNS（DoH，支持 RFC 8484 Wireformat 与 JSON）", IsOn = current.UseDoh, OnContent = "优先使用 DoH 解析", OffContent = "仅使用系统 DNS" };
        var provider = new ComboBox {
            Header = "DoH 服务商",
            ItemsSource = new[] { "AliDNS", "DNSPod", "Cloudflare", "Google", "Custom" },
            SelectedItem = new[] { "AliDNS", "DNSPod", "Cloudflare", "Google", "Custom" }.Contains(current.Provider) ? current.Provider : "Cloudflare",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        var customUrl = new TextBox {
            Header = "自定义 DoH 地址（可选）",
            PlaceholderText = "如 https://edge.1molchuan.top/linuxdo 或 https://dns.alidns.com/dns-query",
            Text = current.CustomUrl ?? "",
            CornerRadius = new CornerRadius(8)
        };
        var fallback = new CheckBox { Content = "DoH 不可达时自动回退至系统 DNS", IsChecked = current.SystemFallback };
        void SyncNetControls() {
            provider.IsEnabled = customUrl.IsEnabled = fallback.IsEnabled = enable.IsOn;
        }
        SyncNetControls();
        enable.Toggled += (_, _) => SyncNetControls();
        var dialog = new ContentDialog {
            XamlRoot = root.XamlRoot,
            Title = "网络链路与 DNS 设置",
            PrimaryButtonText = work.Connected ? "保存并重新连接" : "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = Ui.Stack(
                proxyBox,
                enable,
                provider,
                customUrl,
                fallback)
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var selectedProvider = provider.SelectedItem as string ?? "Cloudflare";
        var customUrlText = string.IsNullOrWhiteSpace(customUrl.Text) ? null : customUrl.Text.Trim();
        var proxyUrlText = string.IsNullOrWhiteSpace(proxyBox.Text) ? null : proxyBox.Text.Trim();
        work.SetNetwork(new(enable.IsOn, selectedProvider, fallback.IsChecked == true, customUrlText, proxyUrlText));
        if (work.Connected && work.SavedConnection is { } saved) await work.ConnectAsync(saved);
    }

    private static string SummarizeLine(string text, string fallback) {
        foreach (var raw in text.Split('\n')) {
            var line = raw.Trim().TrimStart('#', '*', '-', '>', ' ');
            if (line.Length > 0 && !line.StartsWith("```")) {
                return line.Length > 72 ? line[..72] + "…" : line;
            }
        }
        return fallback;
    }

    private static string DescribeTool(ToolCall tool) {
        var action = tool.Name switch {
            "read_file" or "view_file" or "read" => "读取文件",
            "write_file" or "write_to_file" or "write" => "写入文件",
            "edit_file" or "replace_file_content" or "multi_replace_file_content" or "edit" => "修改文件",
            "run_command" or "bash" => "执行命令",
            "search_web" => "检索网页",
            "grep" or "grep_search" => "检索代码内容",
            "find" or "find_by_name" => "查找文件",
            "invoke_subagent" => "调用子代理",
            _ => tool.Name
        };
        var detail = "";
        try {
            if (tool.Parameters.ValueKind == JsonValueKind.Object) {
                foreach (var key in new[] { "toolSummary", "Path", "TargetFile", "AbsolutePath", "path", "CommandLine", "query", "Role" }) {
                    if (tool.Parameters.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String && prop.GetString() is { Length: > 0 } val) {
                        detail = key is "Path" or "TargetFile" or "AbsolutePath" or "path" ? Ui.Tail(val) : (val.Length > 48 ? val[..48] + "…" : val);
                        break;
                    }
                }
            }
        } catch { }
        var stateLabel = tool.IsError == true ? "失败" : tool.State switch {
            "DONE" or "done" or "completed" => "已完成",
            "ACTIVE" or "active" or "running" => "执行中",
            _ => tool.State
        };
        return action
            + (detail.Length > 0 ? " · " + detail : "")
            + (stateLabel.Length > 0 ? " · " + stateLabel : "");
    }

    private static UIElement RenderToolDetails(ToolCall tool) {
        var stack = Ui.Stack();
        stack.Spacing = 6;
        var paramText = tool.Parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : tool.Parameters.ToString();
        if (!string.IsNullOrWhiteSpace(paramText) && paramText != "{}") {
            stack.Children.Add(Ui.SectionLabel("调用参数"));
            stack.Children.Add(new TextBox {
                Text = paramText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12,
                MaxHeight = 160,
                CornerRadius = new CornerRadius(6)
            });
        }
        var outputText = FileRules.CleanText(tool.Output);
        if (outputText.Length > 0) {
            stack.Children.Add(Ui.SectionLabel(tool.IsError == true ? "错误输出" : "执行结果"));
            stack.Children.Add(new TextBox {
                Text = outputText.Length > 4000 ? outputText[..4000] + "\n…（输出已截断）" : outputText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12,
                MaxHeight = 220,
                CornerRadius = new CornerRadius(6)
            });
        }
        if (stack.Children.Count == 0) {
            stack.Children.Add(Ui.Caption("已完成调用，无额外输出文本。"));
        }
        return stack;
    }

    private static Expander LazyExpander(string header, Func<UIElement> contentFactory, int radius = 8) {
        var host = new Border();
        var loaded = false;
        var expander = new Expander {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(radius),
            Content = host
        };
        expander.Expanding += (_, _) => {
            if (loaded) return;
            loaded = true;
            host.Child = contentFactory();
        };
        return expander;
    }

    private UIElement RenderProcessItem((int Order, string Kind, string Title, string RawText, Func<UIElement> ContentFactory) item) {
        if (item.Kind == "thinking") {
            var thinkHeader = Ui.SectionLabel("💡 " + item.Title);
            return Ui.Surface(Ui.Stack(thinkHeader, item.ContentFactory()), "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10);
        }
        if (item.Kind == "step") {
            return Ui.Surface(item.ContentFactory(), "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10);
        }
        return LazyExpander(item.Title, item.ContentFactory, 8);
    }

    private void RenderMessages() {
        var id = work.Selected?.Id ?? "new";
        if (session != id) { session = id; following = true; }
        var offset = transcript.VerticalOffset;
        var tail = following;
        messages.Children.Clear();

        var allMessages = work.Selected?.Messages ?? [];
        for (var idx = 0; idx < allMessages.Length; idx++) {
            var message = allMessages[idx];
            var isLast = idx == allMessages.Length - 1;
            var isRunningHere = isLast && work.Selected?.Running == true && message.Role != "user";

            var cleanMessageText = FileRules.CleanText(message.Text);
            if (message.Role != "user" && cleanMessageText.Length == 0 && !isRunningHere) {
                var fallbackStep = (message.Steps ?? []).LastOrDefault(s => FileRules.CleanText(s.Text).Length > 0);
                if (fallbackStep is not null) cleanMessageText = FileRules.CleanText(fallbackStep.Text);
            }

            var processItems = new List<(int Order, string Kind, string Title, string RawText, Func<UIElement> ContentFactory)>();
            if (message.Role != "user") {
                foreach (var step in message.Steps ?? []) {
                    var cleanThinking = FileRules.CleanText(step.Thinking);
                    if (cleanThinking.Length > 0) {
                        var summary = SummarizeLine(cleanThinking, "思考分析");
                        var capturedThinking = cleanThinking;
                        processItems.Add((step.Index * 2, "thinking", "思考摘要 · " + summary, capturedThinking, () => MessageText(capturedThinking)));
                    }
                    var cleanStepText = FileRules.CleanText(step.Text);
                    if (cleanStepText.Length > 0 && cleanStepText != cleanMessageText) {
                        var summary = SummarizeLine(cleanStepText, "中间进展");
                        var capturedStep = cleanStepText;
                        processItems.Add((step.Index * 2 + 1, "step", "阶段进展 · " + summary, capturedStep, () => MessageText(capturedStep)));
                    }
                }
                foreach (var tool in message.Tools ?? []) {
                    var order = int.TryParse(tool.Id, out var parsed) ? parsed * 2 + 1 : 9000;
                    var title = DescribeTool(tool);
                    var capturedTool = tool;
                    processItems.Add((order, "tool", title, "", () => RenderToolDetails(capturedTool)));
                }
                processItems.Sort((a, b) => a.Order.CompareTo(b.Order));
            }

            var hasAttachments = (message.Attachments?.Length ?? 0) > 0;
            if (message.Role != "user" && cleanMessageText.Length == 0 && processItems.Count == 0 && !hasAttachments && !isRunningHere) {
                var skippedBanner = Ui.Surface(
                    Ui.Caption("上一轮对话已在生成前中断，紧接下一条消息。"),
                    "LayerFillColorAltBrush",
                    new Thickness(12, 6, 12, 6),
                    8);
                skippedBanner.HorizontalAlignment = HorizontalAlignment.Left;
                messages.Children.Add(skippedBanner);
                continue;
            }

            var headerGrid = new Grid { ColumnSpacing = 8 };
            headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

            UIElement roleHeader;
            if (message.Role == "user") {
                roleHeader = Ui.SectionLabel("你");
            } else {
                var modeTag = Ui.Surface(
                    Ui.Caption(FileRules.PresentationLabel(work.Presentation)),
                    "LayerFillColorAltBrush",
                    new Thickness(7, 2, 7, 2),
                    6);
                roleHeader = Ui.Inline(Ui.Logo(18), Ui.Title("AGY", 13.5), modeTag);
            }
            headerGrid.Children.Add(roleHeader);

            var copyPayload = cleanMessageText.Length > 0
                ? cleanMessageText
                : string.Join("\n\n", processItems.Where(p => p.RawText.Length > 0).Select(p => p.RawText));
            var copy = Ui.Icon(Symbol.Copy, "复制消息", () => Copy(copyPayload));
            copy.Width = 28;
            copy.Height = 28;
            copy.MinWidth = 28;
            copy.MinHeight = 28;
            Grid.SetColumn(copy, 1);
            headerGrid.Children.Add(copy);

            var panel = Ui.Stack(headerGrid);
            panel.Spacing = 10;
            var messageLinks = new List<(string Label, string Link)>();

            if (message.Role == "user") {
                if (cleanMessageText.Length > 0) panel.Children.Add(MessageText(cleanMessageText, messageLinks));
            } else if (work.Presentation == "原版") {
                // 原版：完整按时间顺序展示所有思考过程、中间步骤、工具调用与最终回答
                if (processItems.Count > 0) {
                    var fullProcess = Ui.Stack();
                    fullProcess.Spacing = 8;
                    var inlineStart = processItems.Count > 10 ? processItems.Count - 6 : 0;
                    if (inlineStart > 0) {
                        var earlyItems = processItems.Take(inlineStart).ToArray();
                        fullProcess.Children.Add(LazyExpander(
                            $"展开早期 {earlyItems.Length} 个步骤（共 {processItems.Count} 步）",
                            () => {
                                var earlyStack = Ui.Stack();
                                earlyStack.Spacing = 8;
                                foreach (var early in earlyItems) earlyStack.Children.Add(RenderProcessItem(early));
                                return earlyStack;
                            },
                            8));
                    }
                    for (var pIdx = inlineStart; pIdx < processItems.Count; pIdx++) {
                        fullProcess.Children.Add(RenderProcessItem(processItems[pIdx]));
                    }
                    panel.Children.Add(fullProcess);
                }
                if (cleanMessageText.Length > 0) {
                    panel.Children.Add(MessageText(cleanMessageText, messageLinks));
                }
                if (isRunningHere) {
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption("AGY 正在输出…")));
                }
            } else if (work.Presentation == "步骤摘要") {
                // 思考摘要 · 自动折叠工具：直接展示思考过程与阶段进展，方便实时观察是否跑偏并随时「立即引导」；所有工具调用与执行记录自动折叠
                var thoughtItems = processItems.Where(p => p.Kind is "thinking" or "step").ToArray();
                var toolItems = processItems.Where(p => p.Kind == "tool").ToArray();

                if (thoughtItems.Length > 0) {
                    var thoughtStack = Ui.Stack();
                    thoughtStack.Spacing = 8;
                    var inlineStart = thoughtItems.Length > 6 ? thoughtItems.Length - 4 : 0;
                    if (inlineStart > 0) {
                        var earlyThoughts = thoughtItems.Take(inlineStart).ToArray();
                        thoughtStack.Children.Add(LazyExpander(
                            $"展开早期 {earlyThoughts.Length} 段思考记录",
                            () => {
                                var earlyStack = Ui.Stack();
                                earlyStack.Spacing = 8;
                                foreach (var early in earlyThoughts) earlyStack.Children.Add(RenderProcessItem(early));
                                return earlyStack;
                            },
                            8));
                    }
                    for (var tIdx = inlineStart; tIdx < thoughtItems.Length; tIdx++) {
                        thoughtStack.Children.Add(RenderProcessItem(thoughtItems[tIdx]));
                    }
                    panel.Children.Add(thoughtStack);
                }

                if (toolItems.Length > 0) {
                    panel.Children.Add(LazyExpander(
                        $"已自动折叠 {toolItems.Length} 次工具调用与执行记录",
                        () => {
                            var toolStack = Ui.Stack();
                            toolStack.Spacing = 6;
                            for (var k = 0; k < toolItems.Length; k++) {
                                var toolItem = toolItems[k];
                                toolStack.Children.Add(LazyExpander($"{k + 1}. {toolItem.Title}", toolItem.ContentFactory, 8));
                            }
                            return toolStack;
                        },
                        8));
                }

                if (isRunningHere) {
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption("AGY 正在思考与执行…")));
                }
                if (cleanMessageText.Length > 0) {
                    if (thoughtItems.Length > 0 || toolItems.Length > 0) {
                        panel.Children.Add(Ui.Divider());
                    }
                    panel.Children.Add(MessageText(cleanMessageText, messageLinks));
                }
            } else {
                // 结论优先：顶部直接展示最终结论，中间思考过程与工具调用统一收入底部折叠面板
                if (cleanMessageText.Length > 0) {
                    panel.Children.Add(MessageText(cleanMessageText, messageLinks));
                } else if (isRunningHere) {
                    var statusText = processItems.Count > 0
                        ? $"AGY 正在执行（已完成 {processItems.Count} 步）…"
                        : "AGY 正在思考…";
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption(statusText)));
                }

                if (processItems.Count > 0) {
                    var capturedItems = processItems.ToArray();
                    panel.Children.Add(LazyExpander(
                        $"查看思考过程与工具调用 · 共 {capturedItems.Length} 步",
                        () => {
                            var folded = Ui.Stack();
                            folded.Spacing = 6;
                            for (var sIdx = 0; sIdx < capturedItems.Length; sIdx++) {
                                var item = capturedItems[sIdx];
                                folded.Children.Add(LazyExpander($"步骤 {sIdx + 1}  ·  {item.Title}", item.ContentFactory, 8));
                            }
                            return folded;
                        },
                        10));
                }
            }

            if (BuildGroupedArtifacts(messageLinks, message.Attachments ?? []) is { } groupedArtifacts) {
                panel.Children.Add(groupedArtifacts);
            }

            if (message.Role == "user") {
                var bubble = Ui.Surface(panel, "LayerFillColorAltBrush", new Thickness(16, 12, 14, 12), 14);
                bubble.MaxWidth = 680;
                bubble.HorizontalAlignment = HorizontalAlignment.Right;
                messages.Children.Add(bubble);
            } else {
                var card = Ui.Surface(panel, "CardBackgroundFillColorDefaultBrush", new Thickness(18, 14, 16, 14), 14);
                card.HorizontalAlignment = HorizontalAlignment.Stretch;
                messages.Children.Add(card);
            }
        }

        if (messages.Children.Count == 0 && loadingSession) {
            var loadRow = Ui.Inline(new ProgressRing { Width = 18, Height = 18, IsActive = true }, Ui.Title("正在从服务器读取历史对话记录…", 14.5));
            loadRow.Spacing = 10;
            messages.Children.Add(Ui.Card(loadRow, 18, 14));
        } else if (messages.Children.Count == 0 && work.Selected is { } emptySession) {
            var emptyTitle = Ui.Title($"历史会话「{emptySession.DisplayName}」暂无消息记录", 15.5);
            var emptyDesc = Ui.Caption($"此会话创建于工作区 {emptySession.Cwd}（模型：{emptySession.Model}），目前在服务器上尚未保存任何对话消息。你可以直接在下方输入框发送消息继续该会话，或使用下方操作。");
            emptyDesc.LineHeight = 20;
            var emptyActions = Ui.Inline(
                Ui.Action("重新从服务器读取", Symbol.Refresh, () => SelectSessionAsync(emptySession.Id, forceRefresh: true), true),
                Ui.Action("删除此空会话", Symbol.Delete, () => Run(() => work.DeleteAsync(emptySession))));
            messages.Children.Add(Ui.Card(Ui.Stack(emptyTitle, emptyDesc, emptyActions), 20, 14));
        }

        welcome.Visibility = messages.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        transcript.Visibility = messages.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DispatcherQueue.TryEnqueue(() => {
            rendering = true;
            transcript.ChangeView(null, tail ? transcript.ScrollableHeight : offset, null, true);
            rendering = false;
        });
    }

    internal static bool IsImageFile(string? nameOrPath, string? mime = null) {
        if (mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (string.IsNullOrWhiteSpace(nameOrPath)) return false;
        var clean = nameOrPath.Split('#', '?')[0];
        var ext = Path.GetExtension(clean).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp";
    }

    private string? ResolveRemotePath(string link) {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var cleanLink = link.Split('#')[0];
        if (Uri.TryCreate(cleanLink, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return null;
        if (cleanLink.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(cleanLink, UriKind.Absolute, out var file) && string.IsNullOrEmpty(file.Host))
            return Uri.UnescapeDataString(file.AbsolutePath);
        if (cleanLink.StartsWith("sandbox:", StringComparison.OrdinalIgnoreCase))
            return Uri.UnescapeDataString(cleanLink[8..]);
        if (cleanLink.StartsWith('/'))
            return Uri.UnescapeDataString(cleanLink);
        if (!cleanLink.Contains(':'))
            return work.Settings.Workspace.TrimEnd('/') + "/" + Uri.UnescapeDataString(cleanLink);
        return null;
    }

    private UIElement? BuildGroupedArtifacts(List<(string Label, string Link)> links, Attachment[] attachments) {
        var imageItems = new List<(string DisplayName, string RemotePath)>();
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileButtons = new List<UIElement>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in attachments) {
            if (a.Path is not { Length: > 0 } path) continue;
            if (IsImageFile(a.Name, a.Mime)) {
                if (seenImages.Add(path)) imageItems.Add((a.Name, path));
            } else {
                if (!seenTargets.Add(path)) continue;
                var capturedPath = path;
                var capturedName = a.Name;
                var isDir = a.Mime == "inode/directory";
                fileButtons.Add(Ui.ToolbarButton(
                    (isDir ? "@ " : "") + capturedName,
                    isDir ? Symbol.Folder : Symbol.Download,
                    capturedPath,
                    () => Run(async () => {
                        if (isDir) {
                            shell.Navigate("files");
                            await files.ShowFolderAsync(capturedPath);
                        } else {
                            await SaveRemoteAsync(capturedPath, capturedName);
                        }
                    })));
            }
        }

        foreach (var (rawLabel, rawLink) in links) {
            var remotePath = ResolveRemotePath(rawLink);
            if (remotePath is not null && IsImageFile(remotePath, null)) {
                if (seenImages.Add(remotePath)) {
                    var imgName = string.IsNullOrWhiteSpace(rawLabel) ? Path.GetFileName(remotePath) : rawLabel.Trim('`', ' ');
                    imageItems.Add((imgName, remotePath));
                }
                continue;
            }
            var dedupKey = remotePath ?? rawLink.Split('#')[0];
            if (!seenTargets.Add(dedupKey)) continue;
            var cleanLabel = rawLabel.Trim('`', ' ');
            if (remotePath is not null && Path.GetFileName(remotePath) is { Length: > 0 } fileName && (cleanLabel.Contains("::") || cleanLabel.Length == 0 || !cleanLabel.Contains('.'))) {
                cleanLabel = fileName;
            }
            if (string.IsNullOrWhiteSpace(cleanLabel)) cleanLabel = dedupKey;
            var capturedLink = rawLink;
            var capturedLabel = cleanLabel;
            var isWeb = Uri.TryCreate(rawLink, UriKind.Absolute, out var webUri) && webUri.Scheme is "http" or "https";
            fileButtons.Add(Ui.ToolbarButton(
                capturedLabel,
                isWeb ? Symbol.Globe : Symbol.OpenFile,
                dedupKey,
                () => Run(() => OpenLinkAsync(capturedLink, capturedLabel))));
        }

        if (imageItems.Count == 0 && fileButtons.Count == 0) return null;

        var container = Ui.Stack();
        container.Spacing = 10;

        if (imageItems.Count > 0) {
            var imageWrap = Ui.Wrap(10, 10);
            foreach (var (displayName, remotePath) in imageItems) {
                imageWrap.Children.Add(CreateImageThumbnailCard(remotePath, displayName));
            }
            container.Children.Add(imageWrap);
        }

        if (fileButtons.Count > 0) {
            var groupStack = Ui.Stack(
                Ui.SectionLabel($"相关文件与产物 · {fileButtons.Count}"),
                Ui.Wrap(8, 8, fileButtons.ToArray()));
            groupStack.Spacing = 8;
            container.Children.Add(Ui.Surface(groupStack, "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10));
        }

        return container;
    }

    private UIElement CreateImageThumbnailCard(string remotePath, string displayName) {
        var previewFrame = new Grid {
            Width = 164,
            Height = 110,
            CornerRadius = new CornerRadius(8)
        };
        var placeholder = Ui.Stack(
            Ui.Glyph(Symbol.Pictures, 22),
            Ui.Caption("点击预览大图"));
        placeholder.Spacing = 4;
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Opacity = 0.65;

        var image = new Image {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        previewFrame.Children.Add(placeholder);
        previewFrame.Children.Add(image);

        var thumbBox = Ui.Surface(previewFrame, "LayerFillColorDefaultBrush", new Thickness(0), 8);

        var titleText = Ui.Text(displayName, 12);
        titleText.TextWrapping = TextWrapping.NoWrap;
        titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        var bottomRow = new Grid { ColumnSpacing = 6, Width = 164 };
        bottomRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bottomRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bottomRow.Children.Add(Ui.FluentGlyph("\uE8B9", 12));
        Grid.SetColumn(titleText, 1);
        bottomRow.Children.Add(titleText);

        var cardBody = Ui.Stack(thumbBox, bottomRow);
        cardBody.Spacing = 6;

        var cardButton = Ui.Button(displayName, () => Run(() => PreviewImageAsync(remotePath, displayName)));
        cardButton.Content = cardBody;
        cardButton.Padding = new Thickness(7, 7, 7, 6);
        cardButton.CornerRadius = new CornerRadius(10);
        ToolTipService.SetToolTip(cardButton, $"点击放大预览：{displayName}");

        if (!uiCheck) {
            _ = LoadImageThumbnailAsync(remotePath, image, placeholder);
        }

        return cardButton;
    }

    private async Task LoadImageThumbnailAsync(string remotePath, Image targetImage, UIElement placeholder) {
        var localFile = await work.GetCachedImageFileAsync(remotePath);
        if (string.IsNullOrWhiteSpace(localFile) || !File.Exists(localFile)) return;
        DispatcherQueue.TryEnqueue(() => {
            try {
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 360 };
                bmp.UriSource = new Uri(localFile);
                targetImage.Source = bmp;
                placeholder.Visibility = Visibility.Collapsed;
            } catch { }
        });
    }

    internal async Task PreviewImageAsync(string remotePath, string displayName) {
        var localFile = await work.GetCachedImageFileAsync(remotePath);
        if (string.IsNullOrWhiteSpace(localFile) || !File.Exists(localFile)) {
            await SaveRemoteAsync(remotePath, displayName);
            return;
        }
        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(localFile));
        var img = new Image {
            Source = bmp,
            Stretch = Stretch.Uniform,
            MaxHeight = 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var viewer = new ScrollViewer {
            Content = img,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.5f,
            MaxZoomFactor = 4.0f,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 700,
            MinWidth = 640
        };
        var result = await DialogAsync(displayName, viewer, "关闭", "另存为…");
        if (result == ContentDialogResult.Secondary) {
            await SaveRemoteAsync(remotePath, displayName);
        }
    }

    private UIElement MessageText(string value, List<(string Label, string Link)>? collectedLinks = null) {
        var panel = Ui.Stack();
        panel.Spacing = 10;
        var clean = FileRules.CleanText(value);
        if (clean.Length == 0) return panel;
        var localLinks = collectedLinks ?? [];
        var pieces = clean.Split("```");
        for (var i = 0; i < pieces.Length; i++) {
            var text = pieces[i];
            if (i % 2 == 0) {
                var inlined = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"!?\[([^\]\n]{1,1024})\]\(([^\)\n]{1,8192})\)",
                    match => {
                        var label = match.Groups[1].Value;
                        var link = match.Groups[2].Value;
                        localLinks.Add((label, link));
                        return label;
                    },
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(1)).Trim('\r', '\n');
                if (inlined.Length > 0) {
                    panel.Children.Add(Ui.Prose(inlined));
                }
                continue;
            }
            var newline = text.IndexOf('\n');
            var lang = newline >= 0 && text[..newline].Trim().Length > 0 ? text[..newline].Trim() : "CODE";
            var code = newline >= 0 ? text[(newline + 1)..].TrimEnd('\r', '\n') : text;
            var box = new TextBox {
                Text = code,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12.5,
                MaxHeight = 420,
                CornerRadius = new CornerRadius(8)
            };
            var wrap = new CheckBox { Content = "自动换行", FontSize = 12, MinWidth = 0 };
            wrap.Checked += (_, _) => box.TextWrapping = TextWrapping.Wrap;
            wrap.Unchecked += (_, _) => box.TextWrapping = TextWrapping.NoWrap;

            var codeTop = new Grid { ColumnSpacing = 8 };
            codeTop.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            codeTop.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            codeTop.Children.Add(Ui.SectionLabel(lang.ToUpperInvariant()));
            var codeTools = Ui.Inline(wrap, Ui.ToolbarButton("复制代码", Symbol.Copy, "复制此代码块", () => Copy(code)));
            Grid.SetColumn(codeTools, 1);
            codeTop.Children.Add(codeTools);

            var codeCard = Ui.Surface(Ui.Stack(codeTop, box), "LayerFillColorDefaultBrush", new Thickness(12, 10, 12, 12), 10);
            panel.Children.Add(codeCard);
        }
        if (collectedLinks is null && localLinks.Count > 0 && BuildGroupedArtifacts(localLinks, []) is { } standaloneArtifacts) {
            panel.Children.Add(standaloneArtifacts);
        }
        return panel;
    }

    private async Task OpenLinkAsync(string link, string label) {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)) {
            await Launcher.LaunchUriAsync(uri);
            return;
        }
        var path = ResolveRemotePath(link);
        if (path is not null) {
            if (IsImageFile(path, null)) {
                await PreviewImageAsync(path, Path.GetFileName(path));
            } else {
                var saveName = Path.GetFileName(path) is { Length: > 0 } fName ? fName : label;
                await SaveRemoteAsync(path, saveName);
            }
        }
    }

    internal void Copy(string text) {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    internal async Task<ContentDialogResult> DialogAsync(string title, UIElement content, string primary = "完成", string? secondary = null) {
        var dialog = new ContentDialog {
            XamlRoot = root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = root.RequestedTheme
        };
        if (secondary is not null) dialog.SecondaryButtonText = secondary;
        return await dialog.ShowAsync();
    }

    internal async Task UploadAsync() {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var picked = await picker.PickMultipleFilesAsync();
        if (picked.Count > 8) throw new InvalidOperationException("每批最多 8 个文件。");
        foreach (var item in picked) await work.UploadAsync(item.Path, true);
    }

    internal async Task<string?> PickFolderAsync() {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    internal async Task SaveRemoteAsync(string path, string name) {
        var picker = new FileSavePicker { SuggestedFileName = FileRules.SafeName(name) };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("文件", new List<string> { Path.GetExtension(name) is { Length: > 0 } ext ? ext : ".bin" });
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await work.DownloadAsync(path, file.Path);
    }

    private async Task MentionAsync() {
        mentionOpen = true;
        try {
            var browser = new FileBrowser(this, work, true, false);
            await browser.LoadAsync();
            await DialogAsync("引用文件或整个文件夹", browser.View, "关闭");
            ChatPage();
        } finally { mentionOpen = false; }
    }

    private async Task ChooseWorkspaceAsync() {
        if (work.Pending is not null || work.Selected?.Running == true)
            throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");
        var browser = new FileBrowser(this, work, false, true);
        await browser.LoadAsync();
        await DialogAsync("选择或自由指定服务器项目", browser.View, "完成");
    }

    private async Task RenameAsync() {
        if (history.SelectedItem is not Session selected) return;
        var input = new TextBox { Text = selected.DisplayName, Header = "会话名称", CornerRadius = new CornerRadius(8) };
        if (await DialogAsync("重命名会话", input, "保存") == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
            await work.RenameAsync(selected, input.Text);
    }

    private async Task DeleteAsync() {
        if (history.SelectedItem is not Session selected) return;
        if (await DialogAsync("删除会话历史", Ui.Text("只删除网关历史条目，不删除项目文件。"), "删除") == ContentDialogResult.Primary)
            await work.DeleteAsync(selected);
    }

    private async Task LimitsAsync() {
        var forceRefresh = false;
        while (true) {
            var reply = await work.LimitsAsync(forceRefresh);
            var panel = Ui.Stack();
            panel.Spacing = 14;
            panel.MinWidth = 460;

            foreach (var group in reply.Groups) {
                var groupStack = Ui.Stack();
                groupStack.Spacing = 10;

                var titleRow = new Grid { ColumnSpacing = 8 };
                titleRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                titleRow.Children.Add(Ui.Title(group.DisplayName, 15.5));
                if (group.DisplayName != group.Name) {
                    var rawTag = Ui.Caption(group.Name);
                    Grid.SetColumn(rawTag, 1);
                    titleRow.Children.Add(rawTag);
                }
                groupStack.Children.Add(titleRow);

                if (!string.IsNullOrWhiteSpace(group.DisplayDescription)) {
                    groupStack.Children.Add(Ui.Caption(group.DisplayDescription!));
                }

                foreach (var bucket in group.Buckets) {
                    var bucketBox = Ui.Stack();
                    bucketBox.Spacing = 6;

                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    row.Children.Add(Ui.Title(bucket.DisplayTitle, 13.5));

                    var rem = bucket.EffectiveRemaining;
                    var pctText = rem is { } f
                        ? $"剩余 {Math.Clamp(f * 100.0, 0.0, 100.0):0.#}%"
                        : "当前可用";
                    var pctLabel = Ui.Title(pctText, 13.5);
                    Grid.SetColumn(pctLabel, 1);
                    row.Children.Add(pctLabel);
                    bucketBox.Children.Add(row);

                    if (rem is { } fraction) {
                        var bar = new ProgressBar {
                            Minimum = 0,
                            Maximum = 100,
                            Value = Math.Clamp(fraction * 100.0, 0.0, 100.0),
                            Height = 8,
                            CornerRadius = new CornerRadius(4),
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        bucketBox.Children.Add(bar);
                    }

                    if (DateTimeOffset.TryParse(bucket.EffectiveReset, out var reset)) {
                        var localReset = reset.ToLocalTime();
                        var remainingSpan = localReset - DateTimeOffset.Now;
                        var deltaLabel = remainingSpan.TotalHours >= 24
                            ? $"（约 {(int)remainingSpan.TotalDays} 天 {Math.Max(0, remainingSpan.Hours)} 小时后重置）"
                            : remainingSpan.TotalMinutes > 0
                                ? $"（约 {(int)remainingSpan.TotalHours} 小时 {Math.Max(1, remainingSpan.Minutes)} 分钟后重置）"
                                : "";
                        bucketBox.Children.Add(Ui.Caption($"重置时间：{localReset:yyyy-MM-dd HH:mm}{deltaLabel}"));
                    } else if (rem is >= 0.999) {
                        bucketBox.Children.Add(Ui.Caption("状态：额度满额，尚未进入计费重置周期（首次调用后开始计时）"));
                    }

                    groupStack.Children.Add(Ui.Surface(bucketBox, "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10));
                }

                panel.Children.Add(Ui.Card(groupStack, 16, 12));
            }

            if (reply.FetchedAt > 0) {
                var fetchedTime = DateTimeOffset.FromUnixTimeSeconds((long)reply.FetchedAt).ToLocalTime();
                panel.Children.Add(Ui.Caption($"更新于：{fetchedTime:yyyy-MM-dd HH:mm:ss}  ·  数据来源：AGY 实时账户配额"));
            }

            var result = await DialogAsync("真实账号额度", new ScrollViewer { Content = panel, MaxHeight = 520 }, "完成", "刷新额度");
            if (result != ContentDialogResult.Secondary) break;
            forceRefresh = true;
        }
    }

    private async Task CaptureThemesAsync() {
        StartupLog.Mark("ui-check.begin");
        try {
            var folder = Path.Combine(AppContext.BaseDirectory, "ui-check");
            Directory.CreateDirectory(folder);
            async Task Capture(string name) {
                await Task.Delay(600);
                root.UpdateLayout();
                SyncTitleBar();
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && !new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast && AppWindow.TitleBar.BackgroundColor != (root.Background as SolidColorBrush)?.Color)
                    throw new InvalidOperationException("标题栏主题未同步。");
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await bitmap.RenderAsync(root);
                await SaveImageAsync(bitmap, await bitmap.GetPixelsAsync(), Path.Combine(folder, name + ".png"));
            }
            foreach (var value in new[] { ElementTheme.Light, ElementTheme.Dark }) {
                root.RequestedTheme = value;
                await Capture("login-" + value.ToString().ToLowerInvariant());
            }
            await work.ConnectAsync(VisualPreview.Connection);
            foreach (var value in new[] { ElementTheme.Light, ElementTheme.Dark }) {
                work.SetTheme(value.ToString());
                work.NewConversation();
                shell.Navigate("chat");
                Render();
                await Capture("workspace-" + value.ToString().ToLowerInvariant());
                await work.SelectAsync(VisualPreview.SessionID);
                Render();
                await Capture("conversation-" + value.ToString().ToLowerInvariant());
                shell.Navigate("files");
                await files.LoadAsync();
                await Capture("files-" + value.ToString().ToLowerInvariant());
                shell.Navigate("build");
                await Capture("build-" + value.ToString().ToLowerInvariant());
                shell.Navigate("settings");
                await Capture("settings-" + value.ToString().ToLowerInvariant());
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 760));
            work.NewConversation();
            shell.Navigate("chat");
            Render();
            await Capture("workspace-narrow");
            File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(new {
                framework = "WinUI 3",
                renderedOnWindows = true,
                data = "isolated offline examples",
                lightAndDarkCaptured = true,
                connectedPagesCaptured = new[] { "workspace", "conversation", "files", "build", "settings" },
                narrowWidthCaptured = true,
                screenshotIncludesNativeTitleBar = false
            }));
        } catch (Exception e) {
            StartupLog.Error("ui-check.failed", e);
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-error.txt"), e.ToString());
        } finally {
            Application.Current.Exit();
        }
    }

    private static async Task SaveImageAsync(Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap bitmap, Windows.Storage.Streams.IBuffer pixels, string path) {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        var bytes = new byte[pixels.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels)) {
            reader.ReadBytes(bytes);
        }
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
