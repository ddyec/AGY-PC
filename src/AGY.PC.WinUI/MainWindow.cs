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
    private readonly TextBlock quickModelLabel = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 12.5 };
    private readonly TextBlock headerPresentationLabel = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 12 };
    private readonly TextBlock composerPresentationLabel = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, FontSize = 12.5 };
    private readonly TextBlock composerHint = Ui.Caption("Ctrl + Enter 发送   ·   Enter 换行   ·   运行中支持「对话排队」与「立即引导」");
    private readonly ScrollViewer approvalScroll = new() { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        referenceScroll = new() { MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        queueScroll = new() { MaxHeight = 156, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button quickModel = new() { MinHeight = 32, Height = 32, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(8) };
    private readonly Button headerPresentationBtn = new() { MinHeight = 28, Height = 28, Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(6) };
    private readonly Button composerPresentationBtn = new() { MinHeight = 32, Height = 32, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(8) };
    private readonly TextBlock taskSummary = Ui.Caption("选择模型与任务方式");
    private UIElement welcome = null!;
    private string historySignature = "", effortFamily = "";
    private ModelFamily[]? catalog;
    private readonly InfoBar notice = new() { IsClosable = true };
    private readonly TextBox composer = new() {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 52,
        MaxHeight = 180,
        FontSize = 14,
        Padding = new Thickness(2, 2, 2, 8),
        PlaceholderText = "描述任务，或输入 @ 引用文件与文件夹…"
    };
    private readonly StackPanel messages = new() { Spacing = 18, MaxWidth = 840, HorizontalAlignment = HorizontalAlignment.Stretch },
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
    private readonly TextBox address = new() { Header = "服务器地址", Text = "https://agy.yectech.uk", PlaceholderText = "https://你的服务器", CornerRadius = new CornerRadius(8) };
    private readonly PasswordBox token = new() { Header = "连接密钥", PasswordRevealMode = PasswordRevealMode.Peek, CornerRadius = new CornerRadius(8) };
    private readonly ProgressRing progress = new() { IsActive = false, Width = 20, Height = 20 };
    private readonly ListView history = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, MaxHeight = 440 };
    private readonly TextBox historySearch = new() { PlaceholderText = "搜索历史对话…" };
    private readonly Button send, queueBtn, steerBtn, stop, save, latest;
    private readonly FileBrowser files;
    private readonly BuildPane builds;
    private readonly bool uiCheck;
    private bool busy, rendering, following = true, mentionOpen;
    private string signature = "", session = "";
    private readonly UIElement login, chat, settings;

    public MainWindow(Workspace work, bool uiCheck) {
        StartupLog.Mark("window.constructor.enter");
        this.work = work;
        this.uiCheck = uiCheck;
        Title = "AGY - PC";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AGY.ico")); } catch (Exception e) { StartupLog.Error("window.icon.optional", e); }
        try { if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop(); } catch (Exception e) { StartupLog.Error("window.mica.optional", e); }

        Ui.CleanEditableBox(composer);
        Content = root;
        root.ActualThemeChanged += (_, _) => { SyncTitleBar(); DispatcherQueue.TryEnqueue(SyncTitleBar); };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        notice.Margin = new Thickness(16, 8, 16, 0);
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

        queueBtn = Ui.ToolbarButton("对话排队", Symbol.Clock, "加入对话排队：当前回复完成后自动按顺序发送（运行中按 Ctrl+Enter）", () => Run(async () => {
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
        save = Ui.Action("保存默认配置", Symbol.Accept, () => Run(SaveSettingsAsync), true);
        latest = Ui.Button("↓ 回到最新消息", () => { following = true; transcript.ChangeView(null, transcript.ScrollableHeight, null); });
        latest.HorizontalAlignment = HorizontalAlignment.Center;
        latest.Visibility = Visibility.Collapsed;

        ConfigurePresentationButtons();

        shell = new DesktopShell(
            history,
            historySearch,
            connection,
            heading,
            taskSummary,
            () => Run(() => { work.NewConversation(); ChatPage(); return Task.CompletedTask; }),
            () => Run(ChooseWorkspaceAsync),
            () => Run(TaskOptionsAsync),
            headerPresentationBtn);
        host.Children.Add(shell.View);

        chat = MakeChat();
        settings = MakeSettings();
        files = new FileBrowser(this, work, false, false);
        builds = new BuildPane(this, work);
        shell.Pages.Children.Add(chat);
        shell.Pages.Children.Add(files.View);
        shell.Pages.Children.Add(builds.View);
        shell.Pages.Children.Add(settings);
        shell.Navigated += ShowPage;
        shell.Navigate("chat");

        root.SizeChanged += (_, _) => shell.Resize(root.ActualWidth);
        root.KeyDown += (_, e) => {
            if (e.Key == VirtualKey.N && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) && work.Connected && !busy) {
                e.Handled = true;
                Run(() => { work.NewConversation(); ChatPage(); return Task.CompletedTask; });
            }
        };

        quickModel.Click += (_, _) => Run(TaskOptionsAsync);
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
        model.SelectionChanged += (_, _) => { if (!rendering) Efforts(work.Settings.Effort); };

        var historyMenu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = "重命名" };
        rename.Click += (_, _) => Run(RenameAsync);
        var delete = new MenuFlyoutItem { Text = "删除会话历史" };
        delete.Click += (_, _) => Run(DeleteAsync);
        historyMenu.Items.Add(rename);
        historyMenu.Items.Add(delete);
        history.ContextFlyout = historyMenu;

        historySearch.TextChanged += (_, _) => History();
        history.SelectionChanged += (_, _) => {
            if (!rendering && history.SelectedItem is Session s && s.Id != work.Selected?.Id)
                Run(async () => { await work.SelectAsync(s.Id); ChatPage(); });
        };
        history.ItemClick += (_, e) => {
            if (!busy && e.ClickedItem is Session selected && selected.Id == work.Selected?.Id) ChatPage();
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

    private MenuFlyout CreatePresentationFlyout() {
        var flyout = new MenuFlyout();
        foreach (var (id, title) in new[] {
            ("原版", "原版 · 展示全部内容（思考、步骤、工具与结论）"),
            ("步骤摘要", "步骤摘要 · 展示思考过程与分步折叠摘要"),
            ("结论优先", "结论优先 · 顶部直出结论，折叠中间思考与工具")
        }) {
            var item = new MenuFlyoutItem { Text = title };
            var modeId = id;
            item.Click += (_, _) => {
                work.SetPresentation(modeId);
                signature = "";
                Render();
            };
            flyout.Items.Add(item);
        }
        return flyout;
    }

    private void ConfigurePresentationButtons() {
        headerPresentationBtn.Content = headerPresentationLabel;
        headerPresentationBtn.Flyout = CreatePresentationFlyout();
        ToolTipService.SetToolTip(headerPresentationBtn, "切换消息展示方式：原版（全部内容） / 步骤摘要（思考过程） / 结论优先");

        composerPresentationBtn.Content = composerPresentationLabel;
        composerPresentationBtn.Flyout = CreatePresentationFlyout();
        ToolTipService.SetToolTip(composerPresentationBtn, "切换回复展示方式：原版 / 步骤摘要（思考过程） / 结论优先");
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

        quickModel.Content = quickModelLabel;
        conversationBody.SizeChanged += (_, _) => quickModel.MaxWidth = Math.Max(96, Math.Min(240, conversationBody.ActualWidth - 360));
        quickModel.MaxWidth = 240;

        var attachBtn = Ui.ToolbarButton("附件", Symbol.Attach, "上传文件到当前对话", () => Run(UploadAsync));
        var mentionBtn = Ui.ToolbarButton("@ 引用", null, "引用工作区文件或整个文件夹", () => Run(MentionAsync));
        var actions = Ui.Inline(attachBtn, mentionBtn, quickModel, composerPresentationBtn);
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
        composerHint.FontSize = 11.5;
        composerHint.Opacity = 0.58;
        composerHint.HorizontalAlignment = HorizontalAlignment.Center;

        var bottom = Ui.Stack(latest, box, composerHint);
        bottom.Spacing = 8;
        bottom.MaxWidth = 840;
        bottom.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(bottom, 1);
        page.Children.Add(bottom);

        return page;
    }

    private UIElement MakeSettings() {
        permission.Items.Add(new Choice("review", "Restriction · 逐项审批"));
        permission.Items.Add(new Choice("turbo", "Turbo · 自动执行"));
        mode.Items.Add(new Choice("plan", "先做计划"));
        mode.Items.Add(new Choice("accept-edits", "直接实施"));
        theme.Items.Add(new Choice("Default", "跟随 Windows"));
        theme.Items.Add(new Choice("Light", "浅色"));
        theme.Items.Add(new Choice("Dark", "深色"));
        presentation.Items.Add(new Choice("原版", "原版 · 展示全部内容"));
        presentation.Items.Add(new Choice("步骤摘要", "步骤摘要 · 展示思考过程"));
        presentation.Items.Add(new Choice("结论优先", "结论优先 · 优先展示结论"));

        var taskButtons = Ui.Inline(save, Ui.Action("刷新可用模型", Symbol.Refresh, () => Run(work.RefreshModelsAsync)));
        var tasks = Ui.Stack(
            Ui.Title("模型与默认任务策略", 16),
            Ui.Caption("保存后将作为新建对话的默认配置；已有历史对话保留各自原有的模型与策略。"),
            Ui.Pair(model, effort),
            Ui.Pair(permission, mode),
            taskButtons);
        tasks.Spacing = 14;

        var project = Ui.Stack(
            Ui.Title("当前服务器工作区", 16),
            Ui.Caption("选择远程工作文件夹；聊天与文件与产物面板共享同一个项目目录。"),
            Ui.Action("切换服务器项目目录", Symbol.Folder, () => Run(ChooseWorkspaceAsync)));
        project.Spacing = 12;

        var appearance = Ui.Stack(
            Ui.Title("界面外观与消息展示", 16),
            Ui.Caption("支持「原版（全部内容）」、「步骤摘要（展示思考过程）」与「结论优先」三种模式，也可在对话页直接切换。"),
            Ui.Pair(theme, presentation));
        appearance.Spacing = 12;

        var accountActions = Ui.Inline(
            Ui.Action("查看真实账号额度", Symbol.Contact, () => Run(LimitsAsync), true),
            Ui.Action("网络设置 · DoH", Symbol.World, () => Run(NetworkSettingsAsync)),
            Ui.Action("切换服务器或连接密钥", Symbol.Switch, () => { work.Disconnect(); Render(); }));
        var account = Ui.Stack(
            Ui.Title("连接、网络与真实模型额度", 16),
            Ui.Caption("查看 Gemini / Claude / GPT 模型的 5 小时与每周剩余额度百分比及重置时间，或管理加密 DNS（DoH）。"),
            accountActions);
        account.Spacing = 12;

        var panel = Ui.Stack(
            Ui.Card(tasks),
            Ui.Pair(Ui.Card(project), Ui.Card(appearance)),
            Ui.Card(account));
        panel.Spacing = 16;
        panel.MaxWidth = 840;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Margin = new Thickness(0, 4, 0, 16);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task TaskOptionsAsync() {
        var current = work.Settings;
        var models = new ComboBox { Header = "模型", ItemsSource = work.Families, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        var thoughts = new ComboBox { Header = "思考强度", HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        var policy = new ComboBox { Header = "审批策略", ItemsSource = new[] { new Choice("review", "Restriction · 逐项审批"), new Choice("turbo", "Turbo · 自动执行") }, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        var execution = new ComboBox { Header = "执行方式", ItemsSource = new[] { new Choice("plan", "先做计划"), new Choice("accept-edits", "直接实施") }, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        var outputMode = new ComboBox {
            Header = "输出展示方式",
            ItemsSource = new[] {
                new Choice("原版", "原版 · 展示全部内容"),
                new Choice("步骤摘要", "步骤摘要 · 展示思考过程"),
                new Choice("结论优先", "结论优先 · 优先展示结论")
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        void UpdateEfforts() {
            if (models.SelectedItem is not ModelFamily family) return;
            thoughts.ItemsSource = family.Efforts.Select(e => new Choice(e, FileRules.EffortLabel(e))).ToArray();
            thoughts.SelectedItem = thoughts.Items.Cast<Choice>().FirstOrDefault(e => e.Id == current.Effort) ?? thoughts.Items.Cast<Choice>().FirstOrDefault(e => e.Id == family.DefaultEffort);
        }
        models.SelectionChanged += (_, _) => UpdateEfforts();
        models.SelectedItem = work.Families.FirstOrDefault(f => f.Id == current.Model);
        UpdateEfforts();
        policy.SelectedItem = policy.Items.Cast<Choice>().FirstOrDefault(e => e.Id == current.ApprovalMode);
        execution.SelectedItem = execution.Items.Cast<Choice>().FirstOrDefault(e => e.Id == current.Mode);
        outputMode.SelectedItem = outputMode.Items.Cast<Choice>().FirstOrDefault(e => e.Id == work.Presentation) ?? outputMode.Items.Cast<Choice>().First();
        var dialogBody = Ui.Stack(models, thoughts, policy, execution, outputMode);
        dialogBody.MinWidth = 360;
        if (await DialogAsync("模型、任务策略与展示方式", dialogBody, "保存") != ContentDialogResult.Primary) return;
        if (outputMode.SelectedItem is Choice pres) {
            work.SetPresentation(pres.Id);
            signature = "";
        }
        if (models.SelectedItem is ModelFamily family && thoughts.SelectedItem is Choice e && policy.SelectedItem is Choice p && execution.SelectedItem is Choice m)
            await work.ConfigureAsync(new(family.Id, e.Id, p.Id, m.Id, current.Workspace));
        Render();
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

    private async Task SaveSettingsAsync() {
        if (model.SelectedItem is not ModelFamily f || effort.SelectedItem is not Choice e || permission.SelectedItem is not Choice p || mode.SelectedItem is not Choice m)
            throw new InvalidOperationException("请选择有效的模型、强度和策略。");
        await work.ConfigureAsync(new(f.Id, e.Id, p.Id, m.Id, work.Settings.Workspace));
    }

    private void ShowPage(string page) {
        chat.Visibility = page == "chat" ? Visibility.Visible : Visibility.Collapsed;
        files.View.Visibility = page == "files" ? Visibility.Visible : Visibility.Collapsed;
        builds.View.Visibility = page == "build" ? Visibility.Visible : Visibility.Collapsed;
        settings.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "files" && !uiCheck) Run(files.LoadAsync);
    }

    internal void ChatPage() { shell.Navigate("chat"); }

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
            composer.PlaceholderText = "任务运行中：输入后续指令可点「对话排队」自动接续，或点「立即引导」直接转向…";
            composerHint.Text = "任务运行中：Ctrl + Enter 加入对话排队   ·   Ctrl + Shift + Enter 立即引导   ·   无需先手动点停止";
        } else {
            send.Visibility = Visibility.Visible;
            send.IsEnabled = canAct && (hasInput || work.Queue.Count > 0);
            queueBtn.Visibility = Visibility.Visible;
            queueBtn.IsEnabled = canAct && hasInput;
            steerBtn.Visibility = Visibility.Collapsed;
            stop.Visibility = Visibility.Collapsed;
            composer.PlaceholderText = "描述任务，或输入 @ 引用文件与文件夹…";
            composerHint.Text = "Ctrl + Enter 发送   ·   Enter 换行   ·   运行中支持「对话排队」与「立即引导」";
        }
    }

    private void FlushDraft() {
        if (work.Connected && !rendering) work.UpdateDraft(composer.Text);
    }

    private void History() {
        var key = historySearch.Text + "/" + work.Selected?.Id + "/" + string.Join("|", work.Sessions.Select(s => s.Id + ":" + s.DisplayName + ":" + s.Modified));
        if (key == historySignature) return;
        historySignature = key;
        var old = rendering;
        rendering = true;
        history.ItemsSource = work.Sessions.Where(s => s.DisplayName.Contains(historySearch.Text, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(s => s.Modified).ToArray();
        history.SelectedItem = history.Items.Cast<Session>().FirstOrDefault(s => s.Id == work.Selected?.Id);
        rendering = old;
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
        var queueHeader = Ui.Caption($"排队中的对话 · {work.Queue.Count} 条（当前任务完成后将按顺序自动发送）");
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
            progress.IsActive = busy;
            connection.Text = uiCheck && work.Connected ? "离线预览 · 示例数据" : busy ? "正在处理…" : work.ConnectionLabel;
            if (!notice.IsOpen && !string.IsNullOrWhiteSpace(work.Selected?.Error ?? work.Notice)) {
                notice.Title = "服务器状态";
                notice.Message = work.Selected?.Error ?? work.Notice;
                notice.Severity = InfoBarSeverity.Warning;
                notice.IsOpen = true;
            }
            heading.Text = work.Selected?.DisplayName ?? "新对话";
            ToolTipService.SetToolTip(heading, heading.Text);
            shell.SetProject(work.Connected ? Ui.Tail(work.Settings.Workspace) : "工作区");

            var familyName = work.Families.FirstOrDefault(f => f.Id == work.Settings.Model)?.Name ?? "选择模型";
            quickModelLabel.Text = familyName + " · " + FileRules.EffortLabel(work.Settings.Effort) + " ⌄";
            ToolTipService.SetToolTip(quickModel, "更改模型、思考强度、审批策略和执行方式");
            quickModel.IsEnabled = !busy && work.Selected?.Running != true && work.Pending is null;

            headerPresentationLabel.Text = "展示：" + FileRules.PresentationLabel(work.Presentation) + " ⌄";
            composerPresentationLabel.Text = FileRules.PresentationLabel(work.Presentation) + " ⌄";

            taskSummary.Text = (work.Settings.ApprovalMode == "turbo" ? "Turbo 自动执行" : "Restriction 逐项审批") + " · " + (work.Settings.Mode == "plan" ? "先做计划" : "直接实施");
            composer.IsReadOnly = busy || work.Pending is not null;

            if (!ReferenceEquals(catalog, work.Families)) {
                catalog = work.Families;
                model.ItemsSource = catalog;
                effortFamily = "";
            }
            model.SelectedItem = work.Families.FirstOrDefault(f => f.Id == work.Settings.Model);
            Efforts(work.Settings.Effort);
            permission.SelectedItem = permission.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Settings.ApprovalMode);
            mode.SelectedItem = mode.Items.Cast<Choice>().FirstOrDefault(x => x.Id == work.Settings.Mode);
            save.IsEnabled = !busy && work.Selected?.Running != true && work.Pending is null;
            model.IsEnabled = effort.IsEnabled = permission.IsEnabled = mode.IsEnabled = save.IsEnabled;
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
                var pendingActions = Ui.Inline(
                    Ui.Caption("发送结果待确认，不会自动重发。"),
                    Ui.ToolbarButton("核对会话", null, "切换到该会话检查状态", () => Run(() => work.SelectAsync(waiting.SessionID))),
                    Ui.ToolbarButton("按原编号重试", null, "使用原请求编号安全重发", () => Run(work.SubmitPendingAsync)));
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

            var key = (work.Selected?.Id ?? "new") + ":" + (work.Selected?.Revision ?? 0) + ":" + (work.Selected?.Running == true ? "run" : "idle") + ":" + work.Presentation;
            if (signature != key) {
                signature = key;
                RenderMessages();
            }
            History();
            history.IsEnabled = !busy;
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
        var enable = new ToggleSwitch { Header = "加密 DNS（DoH）", IsOn = current.UseDoh, OnContent = "启用", OffContent = "使用系统 DNS" };
        var provider = new ComboBox { Header = "DoH 服务", ItemsSource = new[] { "Cloudflare", "Google" }, SelectedItem = current.Provider, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) };
        var fallback = new CheckBox { Content = "DoH 失败时回退到系统 DNS", IsChecked = current.SystemFallback };
        provider.IsEnabled = fallback.IsEnabled = enable.IsOn;
        enable.Toggled += (_, _) => provider.IsEnabled = fallback.IsEnabled = enable.IsOn;
        var dialog = new ContentDialog {
            XamlRoot = root.XamlRoot,
            Title = "网络设置",
            PrimaryButtonText = work.Connected ? "保存并重新连接" : "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = Ui.Stack(enable, provider, fallback, Ui.Caption("使用代理时由代理解析服务器域名。DoH 服务只接收域名查询，不接收连接密钥和聊天内容。设置保存在这台电脑上。"))
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        work.SetNetwork(new(enable.IsOn, provider.SelectedItem as string ?? "Cloudflare", fallback.IsChecked == true));
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

            var processItems = new List<(int Order, string Kind, string Title, string RawText, UIElement Content)>();
            if (message.Role != "user") {
                foreach (var step in message.Steps ?? []) {
                    var cleanThinking = FileRules.CleanText(step.Thinking);
                    if (cleanThinking.Length > 0) {
                        var summary = SummarizeLine(cleanThinking, "思考分析");
                        processItems.Add((step.Index * 2, "thinking", "思考过程 · " + summary, cleanThinking, MessageText(cleanThinking)));
                    }
                    var cleanStepText = FileRules.CleanText(step.Text);
                    if (cleanStepText.Length > 0 && cleanStepText != cleanMessageText) {
                        var summary = SummarizeLine(cleanStepText, "中间进展");
                        processItems.Add((step.Index * 2 + 1, "step", "阶段进展 · " + summary, cleanStepText, MessageText(cleanStepText)));
                    }
                }
                foreach (var tool in message.Tools ?? []) {
                    var order = int.TryParse(tool.Id, out var parsed) ? parsed * 2 + 1 : 9000;
                    var title = DescribeTool(tool);
                    processItems.Add((order, "tool", title, "", RenderToolDetails(tool)));
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
                    Ui.Caption(work.Presentation),
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

            if (message.Role == "user") {
                if (cleanMessageText.Length > 0) panel.Children.Add(MessageText(cleanMessageText));
            } else if (work.Presentation == "原版") {
                // 原版：完整按时间顺序展示所有思考过程、中间步骤、工具调用与最终回答
                if (processItems.Count > 0) {
                    var fullProcess = Ui.Stack();
                    fullProcess.Spacing = 8;
                    foreach (var item in processItems) {
                        if (item.Kind == "thinking") {
                            var thinkHeader = Ui.SectionLabel("💡 " + item.Title);
                            fullProcess.Children.Add(Ui.Surface(Ui.Stack(thinkHeader, item.Content), "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10));
                        } else if (item.Kind == "step") {
                            fullProcess.Children.Add(Ui.Surface(item.Content, "LayerFillColorAltBrush", new Thickness(12, 10, 12, 10), 10));
                        } else {
                            fullProcess.Children.Add(new Expander {
                                Header = item.Title,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                CornerRadius = new CornerRadius(8),
                                Content = item.Content
                            });
                        }
                    }
                    panel.Children.Add(fullProcess);
                }
                if (cleanMessageText.Length > 0) {
                    panel.Children.Add(MessageText(cleanMessageText));
                }
                if (isRunningHere) {
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption("AGY 正在思考与输出全部内容…")));
                }
            } else if (work.Presentation == "步骤摘要") {
                // 步骤摘要（展示思考过程）：分步编号展示思考与工具摘要，可逐项展开，下方呈现最终结论
                if (processItems.Count > 0) {
                    var stepTimeline = Ui.Stack();
                    stepTimeline.Spacing = 6;
                    stepTimeline.Children.Add(Ui.Caption($"思考过程与执行步骤 · 共 {processItems.Count} 步（点击展开详情）"));
                    for (var sIdx = 0; sIdx < processItems.Count; sIdx++) {
                        var item = processItems[sIdx];
                        stepTimeline.Children.Add(new Expander {
                            Header = $"步骤 {sIdx + 1}  ·  {item.Title}",
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            CornerRadius = new CornerRadius(8),
                            Content = item.Content
                        });
                    }
                    panel.Children.Add(stepTimeline);
                }
                if (isRunningHere) {
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption("正在推进下一步思考与工具调用…")));
                }
                if (cleanMessageText.Length > 0) {
                    if (processItems.Count > 0) {
                        panel.Children.Add(Ui.Divider());
                        panel.Children.Add(Ui.SectionLabel("最终结论"));
                    }
                    panel.Children.Add(MessageText(cleanMessageText));
                } else if (!isRunningHere && processItems.Count > 0) {
                    panel.Children.Add(Ui.Caption("本轮仅执行了上述步骤，未生成独立结论段落。"));
                }
            } else {
                // 结论优先：顶部直接展示最终结论，中间思考过程与工具调用统一收入底部折叠面板
                if (cleanMessageText.Length > 0) {
                    panel.Children.Add(MessageText(cleanMessageText));
                } else if (isRunningHere) {
                    var statusText = processItems.Count > 0
                        ? $"AGY 正在思考与执行（已完成 {processItems.Count} 个中间步骤），结论完成后将在此优先展示…"
                        : "AGY 正在思考…";
                    panel.Children.Add(Ui.Inline(new ProgressRing { Width = 16, Height = 16, IsActive = true }, Ui.Caption(statusText)));
                } else if (processItems.Count > 0) {
                    panel.Children.Add(Ui.Caption("本轮未产生独立文字结论，可展开下方过程查看执行结果。"));
                }

                if (processItems.Count > 0) {
                    var folded = Ui.Stack();
                    folded.Spacing = 6;
                    for (var sIdx = 0; sIdx < processItems.Count; sIdx++) {
                        var item = processItems[sIdx];
                        folded.Children.Add(new Expander {
                            Header = $"步骤 {sIdx + 1}  ·  {item.Title}",
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            CornerRadius = new CornerRadius(8),
                            Content = item.Content
                        });
                    }
                    panel.Children.Add(new Expander {
                        Header = $"查看思考过程与工具调用 · 共 {processItems.Count} 步",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        CornerRadius = new CornerRadius(10),
                        Content = folded
                    });
                }
            }

            if (hasAttachments) {
                var attachRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var a in message.Attachments ?? []) {
                    if (a.Path is { } path) {
                        attachRow.Children.Add(Ui.ToolbarButton(
                            (a.Mime == "inode/directory" ? "@ " : "下载 ") + a.Name,
                            a.Mime == "inode/directory" ? Symbol.Folder : Symbol.Download,
                            a.Name,
                            () => Run(async () => {
                                if (a.Mime == "inode/directory") {
                                    shell.Navigate("files");
                                    await files.ShowFolderAsync(path);
                                } else {
                                    await SaveRemoteAsync(path, a.Name);
                                }
                            })));
                    }
                }
                if (attachRow.Children.Count > 0) panel.Children.Add(attachRow);
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

        welcome.Visibility = messages.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        transcript.Visibility = messages.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DispatcherQueue.TryEnqueue(() => {
            rendering = true;
            transcript.ChangeView(null, tail ? transcript.ScrollableHeight : offset, null, true);
            rendering = false;
        });
    }

    private UIElement MessageText(string value) {
        var panel = Ui.Stack();
        panel.Spacing = 10;
        var clean = FileRules.CleanText(value);
        if (clean.Length == 0) return panel;
        var pieces = clean.Split("```");
        for (var i = 0; i < pieces.Length; i++) {
            var text = pieces[i];
            if (i % 2 == 0) {
                var offset = 0;
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"\[([^\]\n]{1,1024})\]\(([^\)\n]{1,8192})\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))) {
                    if (match.Index > offset) {
                        var before = text[offset..match.Index].Trim('\r', '\n');
                        if (before.Length > 0) panel.Children.Add(Ui.Prose(before));
                    }
                    var label = match.Groups[1].Value;
                    var link = match.Groups[2].Value;
                    panel.Children.Add(Ui.ToolbarButton(label, Symbol.OpenFile, link, () => Run(() => OpenLinkAsync(link, label))));
                    offset = match.Index + match.Length;
                }
                if (offset < text.Length) {
                    var trimmed = text[offset..].Trim('\r', '\n');
                    if (trimmed.Length > 0) panel.Children.Add(Ui.Prose(trimmed));
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
        return panel;
    }

    private async Task OpenLinkAsync(string link, string label) {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)) {
            await Launcher.LaunchUriAsync(uri);
            return;
        }
        string? path = null;
        if (link.StartsWith("file://") && Uri.TryCreate(link, UriKind.Absolute, out var file) && string.IsNullOrEmpty(file.Host))
            path = Uri.UnescapeDataString(file.AbsolutePath);
        else if (link.StartsWith("sandbox:"))
            path = Uri.UnescapeDataString(link[8..]);
        else if (link.StartsWith('/'))
            path = Uri.UnescapeDataString(link);
        else if (!link.Contains(':'))
            path = work.Settings.Workspace.TrimEnd('/') + "/" + Uri.UnescapeDataString(link);
        if (path is not null) await SaveRemoteAsync(path, label);
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
        if (work.Selected is not null) {
            if (work.Pending is not null || work.Selected.Running)
                throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");
            if (await DialogAsync("切换项目", Ui.Text("当前对话会保留。新建一个对话后，可以选择其他项目。"), "新建对话并选择") != ContentDialogResult.Primary)
                return;
            work.NewConversation();
            ChatPage();
        }
        var browser = new FileBrowser(this, work, false, true);
        await browser.LoadAsync();
        await DialogAsync("选择服务器项目", browser.View, "完成");
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
        while (true) {
            var reply = await work.LimitsAsync();
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
