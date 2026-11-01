using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AGY.PC.WinUI;

internal sealed class FileBrowser {
    private readonly MainWindow window;
    private readonly Workspace work;
    private readonly bool picking, chooseWorkspace;
    internal Grid View { get; } = new() { RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox scope = new() { ItemsSource = new[] { "工作区", "已上传", "AGY 产物" }, SelectedIndex = 0, MinWidth = 136, CornerRadius = new CornerRadius(8) };
    private readonly TextBox search = new() { PlaceholderText = "搜索当前目录中的文件或文件夹…" };
    private readonly TextBox customPathBox = new() { PlaceholderText = "自由输入或粘贴服务器路径（支持绝对路径或相对工作区路径）…" };
    private readonly TextBlock trail = Ui.Caption("当前项目");
    private readonly ListView entries = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false, MinHeight = 220 };
    private readonly CheckBox hidden = new() { Content = "显示隐藏项", FontSize = 12, MinWidth = 0 }, recent = new() { Content = "最近修改优先", FontSize = 12, MinWidth = 0 };
    private readonly TextBlock selection = Ui.Caption("选择文件或文件夹");
    private Button reference = null!, download = null!, open = null!, preview = null!;
    private FileListing? listing;
    private string path = "";
    private int generation;
    private bool busy;

    private async void Act(Func<Task> action) {
        if (busy) return;
        busy = true;
        try { await action(); } catch (Exception e) { trail.Text = e.Message; } finally { busy = false; }
    }

    private string Scope => scope.SelectedIndex switch { 1 => "uploads", 2 => "artifacts", _ => "workspace" };

    public FileBrowser(MainWindow window, Workspace work, bool picking, bool chooseWorkspace) {
        this.window = window;
        this.work = work;
        this.picking = picking;
        this.chooseWorkspace = chooseWorkspace;
        if (picking || chooseWorkspace) View.MinWidth = 540;

        View.RowDefinitions.Add(new() { Height = GridLength.Auto });
        View.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        View.RowDefinitions.Add(new() { Height = GridLength.Auto });

        Ui.StyleSearchBox(search);
        Ui.StyleSearchBox(customPathBox);
        customPathBox.PlaceholderText = chooseWorkspace
            ? "自由指定服务器项目目录路径（如 /home/ubuntu/project），输入后点右侧按钮生效…"
            : "自由输入服务器文件或目录路径（如 src/Main.cs 或 /abs/path），可直接 @ 引用…";

        var customPathRow = new Grid { ColumnSpacing = 8 };
        customPathRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        customPathRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        customPathRow.Children.Add(customPathBox);
        var applyCustomPath = Ui.Button(chooseWorkspace ? "使用指定路径" : "@ 引用此路径", () => Act(ApplyCustomPathAsync), true);
        Grid.SetColumn(applyCustomPath, 1);
        customPathRow.Children.Add(applyCustomPath);
        customPathBox.KeyDown += (_, e) => {
            if (e.Key == Windows.System.VirtualKey.Enter) {
                e.Handled = true;
                Act(ApplyCustomPathAsync);
            }
        };

        trail.FontWeight = new() { Weight = 500 };
        var trailBadge = Ui.Surface(trail, "LayerFillColorAltBrush", new Thickness(10, 5, 10, 5), 6);

        var navLeft = Ui.Inline(
            Ui.Icon(Symbol.Home, "返回项目根目录", () => Act(async () => { path = ""; await LoadAsync(); })),
            Ui.Icon(Symbol.Back, "返回上一级", () => Act(async () => { path = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1)); await LoadAsync(); })),
            scope,
            trailBadge);
        navLeft.Spacing = 8;

        var filters = Ui.Inline(hidden, recent);
        filters.Spacing = 12;

        var topRow = new Grid { ColumnSpacing = 12 };
        topRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        topRow.Children.Add(navLeft);
        Grid.SetColumn(filters, 1);
        topRow.Children.Add(filters);

        var headerStack = Ui.Stack(topRow, customPathRow, search);
        headerStack.Spacing = 8;
        var headerCard = Ui.Surface(headerStack, "CardBackgroundFillColorDefaultBrush", new Thickness(14, 12, 14, 12), 12);
        View.Children.Add(headerCard);

        var listSurface = Ui.Surface(entries, "CardBackgroundFillColorDefaultBrush", new Thickness(6), 12);
        Grid.SetRow(listSurface, 1);
        View.Children.Add(listSurface);

        open = Ui.Action("打开目录", Symbol.Folder, () => Act(OpenAsync));
        var useCurrent = Ui.Button(chooseWorkspace ? "使用此项目" : "@ 当前目录", () => Act(CurrentAsync), true);

        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionButtons.Children.Add(open);
        if (!chooseWorkspace) {
            preview = Ui.Action("预览图片", Symbol.Pictures, () => Act(PreviewSelectedAsync));
            reference = Ui.Button("@ 选中项", () => Act(ReferenceAsync));
            download = Ui.Action("下载", Symbol.Download, () => Act(DownloadAsync));
            var upload = Ui.Action("上传文件", Symbol.Upload, () => Act(window.UploadAsync));
            actionButtons.Children.Add(preview);
            actionButtons.Children.Add(reference);
            actionButtons.Children.Add(useCurrent);
            actionButtons.Children.Add(download);
            actionButtons.Children.Add(upload);
        } else {
            actionButtons.Children.Add(useCurrent);
        }

        var footerGrid = new Grid { ColumnSpacing = 12 };
        footerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        selection.VerticalAlignment = VerticalAlignment.Center;
        selection.TextWrapping = TextWrapping.NoWrap;
        selection.TextTrimming = TextTrimming.CharacterEllipsis;
        footerGrid.Children.Add(selection);
        var actionScroll = new ScrollViewer {
            Content = actionButtons,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(actionScroll, 1);
        footerGrid.Children.Add(actionScroll);

        var footerCard = Ui.Surface(footerGrid, "CardBackgroundFillColorDefaultBrush", new Thickness(14, 10, 14, 10), 12);
        Grid.SetRow(footerCard, 2);
        View.Children.Add(footerCard);

        entries.SelectionChanged += (_, _) => Selection();
        Selection();
        scope.IsEnabled = !chooseWorkspace;
        scope.SelectionChanged += (_, _) => Act(async () => { path = ""; await LoadAsync(); });
        search.TextChanged += (_, _) => Filter();
        hidden.Checked += (_, _) => Filter();
        hidden.Unchecked += (_, _) => Filter();
        recent.Checked += (_, _) => Filter();
        recent.Unchecked += (_, _) => Filter();
        entries.DoubleTapped += (_, _) => {
            if (Selected is { Directory: true }) Act(OpenAsync);
            else if (Selected is { Directory: false } f && MainWindow.IsImageFile(f.Name)) Act(PreviewSelectedAsync);
        };
        entries.KeyDown += (_, e) => {
            if (e.Key == Windows.System.VirtualKey.Enter) {
                if (Selected is { Directory: true }) {
                    e.Handled = true;
                    Act(OpenAsync);
                } else if (Selected is { Directory: false } f && MainWindow.IsImageFile(f.Name)) {
                    e.Handled = true;
                    Act(PreviewSelectedAsync);
                }
            }
        };
    }

    public async Task LoadAsync() {
        var token = ++generation;
        var result = await work.BrowseAsync(Scope, path, chooseWorkspace ? work.Status!.Roots[0] : null);
        if (token != generation) return;
        listing = result;
        trail.Text = path.Length == 0 ? "根目录 /" : path + "/";
        if (chooseWorkspace && string.IsNullOrWhiteSpace(customPathBox.Text)) {
            customPathBox.Text = result.Root.TrimEnd('/') + (path.Length > 0 ? "/" + path : "");
        }
        Filter();
    }

    private FileEntry? Selected => (entries.SelectedItem as ListViewItem)?.Tag as FileEntry;

    private void Filter() {
        entries.Items.Clear();
        foreach (var entry in (listing?.Entries ?? [])
            .Where(e => (!chooseWorkspace || e.Directory) && (hidden.IsChecked == true || !e.Name.StartsWith('.')) && (search.Text.Length == 0 || e.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(e => e.Directory)
            .ThenBy(e => recent.IsChecked == true ? -e.Modified : 0)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)) {

            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 7, 8, 7) };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

            var iconBox = Ui.Surface(
                Ui.Glyph(entry.Directory ? Symbol.Folder : Symbol.Document, 16),
                "LayerFillColorAltBrush",
                new Thickness(0),
                8);
            iconBox.Width = 34;
            iconBox.Height = 34;
            iconBox.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(iconBox);

            var name = Ui.Title(entry.Name, 13.5);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var sub = Ui.Caption(entry.Directory ? "文件夹 · 双击打开或整体 @ 引用" : FormatSize(entry.Size));
            sub.FontSize = 11.5;
            var label = Ui.Stack(name, sub);
            label.Spacing = 2;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var date = Ui.Caption(entry.Modified > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)entry.Modified).ToLocalTime().ToString("yyyy-MM-dd") : "");
            date.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(date, 2);
            row.Children.Add(date);

            entries.Items.Add(new ListViewItem { Content = row, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8) });
        }
        Selection();
    }

    private static string FormatSize(long size) =>
        size >= 1024 * 1024 ? $"{size / (1024d * 1024):0.#} MB" :
        size >= 1024 ? $"{size / 1024d:0.#} KB" :
        $"{size} B";

    private void Selection() {
        var item = Selected;
        selection.Text = item is null ? "单击选择文件或文件夹；双击进入目录或预览图片。" : item.Name + (item.Directory ? " · 整个文件夹" : " · " + FormatSize(item.Size));
        open.IsEnabled = item?.Directory == true;
        if (preview is not null) preview.IsEnabled = item is { Directory: false } f && MainWindow.IsImageFile(f.Name);
        if (reference is not null) reference.IsEnabled = item is not null;
        if (download is not null) download.IsEnabled = item is { Directory: false };
    }

    private async Task PreviewSelectedAsync() {
        if (Selected is { Directory: false } e && MainWindow.IsImageFile(e.Name)) {
            await window.PreviewImageAsync(e.Path, e.Name);
        }
    }

    private async Task OpenAsync() {
        if (Selected is not { Directory: true } e) return;
        path = string.Join('/', new[] { path, e.Name }.Where(s => s.Length > 0));
        await LoadAsync();
    }

    private Task ReferenceAsync() {
        if (Selected is { } e) {
            work.AddReference(new(e.Path, FileRules.Label(Scope, path, e.Name, e.Directory), e.Directory));
            window.ChatPage();
        }
        return Task.CompletedTask;
    }

    private async Task CurrentAsync() {
        if (listing is not { Root.Length: > 0 } l) return;
        var full = l.Root.TrimEnd('/') + (path.Length > 0 ? "/" + path : "");
        if (chooseWorkspace) await work.ConfigureAsync(work.Settings with { Workspace = full });
        else {
            work.AddReference(new(full, FileRules.Label(Scope, "", path.Length == 0 ? "当前项目" : path, true), true));
            window.ChatPage();
        }
    }

    private async Task ApplyCustomPathAsync() {
        var raw = customPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        var normalized = raw.Replace('\\', '/');
        if (chooseWorkspace) {
            if (!normalized.StartsWith('/')) {
                var root = work.Status?.Roots.FirstOrDefault() ?? work.Settings.Workspace;
                normalized = root.TrimEnd('/') + "/" + normalized.TrimStart('/');
            }
            await work.ConfigureAsync(work.Settings with { Workspace = normalized.TrimEnd('/') });
            selection.Text = "已自由指定项目路径：" + normalized.TrimEnd('/');
        } else {
            var full = normalized.StartsWith('/')
                ? normalized
                : (listing?.Root ?? work.Settings.Workspace).TrimEnd('/') + "/" + normalized.TrimStart('/');
            var isDir = normalized.EndsWith('/');
            var cleanFull = full.Length > 1 ? full.TrimEnd('/') : full;
            var label = "指定/" + Ui.Tail(cleanFull) + (isDir ? "/" : "");
            work.AddReference(new(cleanFull, label, isDir));
            customPathBox.Text = "";
            selection.Text = "已添加引用：" + cleanFull;
            window.ChatPage();
        }
    }

    private async Task DownloadAsync() {
        if (Selected is { Directory: false } e) await window.SaveRemoteAsync(e.Path, e.Name);
    }

    public async Task ShowFolderAsync(string absolute) {
        scope.SelectedIndex = 0;
        if (FileRules.Within(absolute, work.Settings.Workspace))
            path = absolute[work.Settings.Workspace.TrimEnd('/').Length..].Trim('/');
        await LoadAsync();
    }
}
