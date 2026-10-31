using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace AGY.PC.WinUI;

internal sealed class BuildPane {
    private readonly MainWindow window;
    private readonly Workspace work;
    private CancellationTokenSource? cancellation;
    internal ScrollViewer View { get; }
    private readonly ComboBox target = new() {
        Header = "执行位置",
        ItemsSource = new[] { "Windows 本机", "WSL · Linux x64", "服务器 · 网页 / 服务端" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        CornerRadius = new CornerRadius(8)
    };
    private readonly TextBox folder = new() {
        Header = "本地项目副本（WSL 会自动转换路径）",
        PlaceholderText = "同步服务器项目到独立副本，或选择本机文件夹",
        CornerRadius = new CornerRadius(8)
    };
    private readonly TextBox distribution = new() {
        Header = "WSL 发行版（留空使用默认）",
        PlaceholderText = "例如 Ubuntu",
        CornerRadius = new CornerRadius(8)
    };
    private readonly TextBox command = new() {
        Header = "构建命令",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 96,
        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
        FontSize = 13,
        PlaceholderText = "例如 dotnet publish -c Release -r win-x64",
        CornerRadius = new CornerRadius(8)
    };
    private readonly TextBox log = new() {
        Header = "本机 / WSL 构建日志",
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
        FontSize = 12.5,
        MinHeight = 200,
        MaxHeight = 460,
        CornerRadius = new CornerRadius(8)
    };
    private string? lastLog;

    public BuildPane(MainWindow window, Workspace work) {
        this.window = window;
        this.work = work;

        var syncButtons = Ui.Inline(
            Ui.Action("同步服务器项目到新副本", Symbol.Sync, () => window.Run(SynchronizeAsync)),
            Ui.Action("选择本机目录", Symbol.Folder, () => window.Run(async () => {
                var selected = await window.PickFolderAsync();
                if (selected is not null) folder.Text = selected;
            })));
        syncButtons.Spacing = 10;

        var projectSection = Ui.Stack(
            Ui.Title("1 · 选择执行位置与项目目录", 16),
            Ui.Caption("Windows 原生应用在本机构建，x64 Linux 任务通过 WSL 执行，服务端与前端任务交由当前 AGY 会话执行。"),
            target,
            distribution,
            folder,
            syncButtons);
        projectSection.Spacing = 14;

        var primaryActions = Ui.Inline(
            Ui.Action("检查并执行", Symbol.Play, () => window.Run(StartAsync), true),
            Ui.Action("停止本机 / WSL 构建", Symbol.Stop, Cancel),
            Ui.Action("回传构建日志", Symbol.Upload, () => window.Run(UploadLogAsync)),
            Ui.Action("回传产物文件", Symbol.Attach, () => window.Run(UploadArtifactsAsync)));
        primaryActions.Spacing = 10;

        var commandSection = Ui.Stack(
            Ui.Title("2 · 构建命令与产物回传", 16),
            Ui.Caption("每次执行前均会展示目标环境、目录与完整命令供你确认；日志与产物可按需回传至当前会话。"),
            command,
            primaryActions);
        commandSection.Spacing = 14;

        var output = new Expander {
            Header = "查看本机 / WSL 构建输出",
            Content = log,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12)
        };
        log.TextChanged += (_, _) => { if (!string.IsNullOrEmpty(log.Text)) output.IsExpanded = true; };

        var help = new Expander {
            Header = "混合工作流安全边界说明",
            Content = Ui.Caption("同步操作每次创建独立的本机目录副本，不会覆盖已有本地修改。WSL 模式会验证架构为 x86_64；单次产物回传上限 25 MiB。"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12)
        };

        var panel = Ui.Stack(
            Ui.Card(projectSection),
            Ui.Card(commandSection),
            output,
            help);
        panel.Spacing = 16;
        panel.MaxWidth = 840;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Margin = new Thickness(0, 4, 0, 16);
        View = new() { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void TargetChanged() {
            distribution.Visibility = Target == BuildTarget.WslLinuxX64 ? Visibility.Visible : Visibility.Collapsed;
            folder.Visibility = syncButtons.Visibility = Target == BuildTarget.Server ? Visibility.Collapsed : Visibility.Visible;
        }
        target.SelectionChanged += (_, _) => TargetChanged();
        TargetChanged();

        View.Loaded += (_, _) => {
            if (work.Build is { } saved) {
                target.SelectedIndex = saved.Target switch { BuildTarget.Windows => 0, BuildTarget.WslLinuxX64 => 1, _ => 2 };
                folder.Text = saved.Directory;
                command.Text = saved.Command;
                distribution.Text = saved.Distribution ?? "";
            }
        };
    }

    private BuildTarget Target => target.SelectedIndex switch {
        1 => BuildTarget.WslLinuxX64,
        2 => BuildTarget.Server,
        _ => BuildTarget.Windows
    };

    private async Task SynchronizeAsync() {
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY-PC", "Workspaces", Guid.NewGuid().ToString("N"), Ui.Tail(work.Settings.Workspace));
        await work.SynchronizeProjectAsync(destination);
        folder.Text = destination;
        log.Text = "项目已同步到独立本机副本。没有覆盖已有目录。";
    }

    private async Task StartAsync() {
        var plan = new BuildPlan(Target, Target == BuildTarget.Server ? work.Settings.Workspace : folder.Text, command.Text, distribution.Text);
        if (string.IsNullOrWhiteSpace(plan.Command)) throw new InvalidOperationException("请填写构建命令。");
        var result = await window.DialogAsync(
            "确认构建执行位置",
            Ui.Stack(
                Ui.Title(HybridBuild.Label(plan.Target), 18),
                Ui.Caption(plan.Directory),
                new TextBox { Text = plan.Command, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 240, CornerRadius = new CornerRadius(8) }),
            "执行这一次");
        if (result != ContentDialogResult.Primary) return;
        work.RememberBuild(plan);
        if (plan.Target == BuildTarget.Server) {
            if (!string.IsNullOrWhiteSpace(work.Draft.Text) || work.Draft.References.Length > 0)
                throw new InvalidOperationException("请先处理当前聊天草稿，再提交服务器构建。");
            work.UpdateDraft("请在当前服务器项目中执行下面的构建命令，并报告退出码、日志和产物。遵守当前审批策略。\n```sh\n" + plan.Command + "\n```");
            await work.SendAsync();
            window.ChatPage();
            return;
        }
        if (plan.Target == BuildTarget.WslLinuxX64 && !plan.Directory.StartsWith('/'))
            plan = plan with { Directory = await WslPathAsync(plan.Directory, plan.Distribution) };
        cancellation?.Dispose();
        cancellation = new();
        log.Text = "执行位置：" + HybridBuild.Label(plan.Target) + "\n";
        var lines = new System.Text.StringBuilder();
        try {
            var completed = await HybridBuild.RunAsync(plan, line => window.DispatcherQueue.TryEnqueue(() => {
                if (lines.Length < 2 * 1024 * 1024) {
                    lines.AppendLine(line);
                    log.Text = lines.ToString();
                }
            }), cancellation.Token);
            log.Text = completed.Log + "\n退出码：" + completed.ExitCode;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY-PC", "BuildLogs");
            Directory.CreateDirectory(directory);
            lastLog = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            await File.WriteAllTextAsync(lastLog, "目标：" + HybridBuild.Label(plan.Target) + "\n目录：" + plan.Directory + "\n命令：" + plan.Command + "\n" + log.Text);
        } finally {
            cancellation.Dispose();
            cancellation = null;
        }
    }

    private static async Task<string> WslPathAsync(string path, string? distro) {
        var start = new System.Diagnostics.ProcessStartInfo("wsl.exe") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(distro)) {
            start.ArgumentList.Add("-d");
            start.ArgumentList.Add(distro);
        }
        start.ArgumentList.Add("--exec");
        start.ArgumentList.Add("wslpath");
        start.ArgumentList.Add("-a");
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("无法调用 WSL，请先安装发行版。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var value = (await output).Trim();
        if (process.ExitCode != 0 || !value.StartsWith('/'))
            throw new IOException("WSL 路径转换失败：" + await error);
        return value;
    }

    private async Task UploadLogAsync() {
        if (lastLog is null) throw new InvalidOperationException("请先完成一次本机构建。");
        await work.UploadAsync(lastLog, false);
        log.Text += "\n构建日志已回传到当前会话上传区。";
    }

    private async Task UploadArtifactsAsync() {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        picker.FileTypeFilter.Add("*");
        var selected = await picker.PickMultipleFilesAsync();
        if (selected.Count > 8) throw new InvalidOperationException("每批最多回传 8 个产物。");
        foreach (var file in selected) await work.UploadAsync(file.Path, false);
        log.Text += "\n已回传 " + selected.Count + " 个产物到会话上传区。";
    }

    public void Cancel() => cancellation?.Cancel();
}
