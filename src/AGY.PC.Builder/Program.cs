using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AGY.PC.Core;

internal static class Program {
    private static readonly string BaseRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY-PC");
    private static readonly string ToolFolder = Path.Combine(BaseRoot, "BuildTools", "dotnet-10.0.403");
    private static readonly string WorkspaceFolder = Path.Combine(BaseRoot, "Workspace");
    private static readonly string AppFolder = Path.Combine(BaseRoot, "App");
    private static readonly string LogFolder = Path.Combine(BaseRoot, "Logs");
    private static readonly string LegacyBuildsRoot = Path.Combine(BaseRoot, "WindowsBuilds");
    private static StreamWriter? log;
    private static readonly object LogGate = new();

    public static async Task<int> Main() {
        Console.OutputEncoding = Encoding.UTF8;
        try {
            if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("这个构建入口用于 Windows x64。WSL/Linux 不能完成 WinUI 的 Windows 原生打包步骤。");

            Directory.CreateDirectory(LogFolder);
            log = new StreamWriter(Path.Combine(LogFolder, "build.log"), false, Encoding.UTF8) { AutoFlush = true };
            Say("AGY - PC · WinUI 3 本机构建 (v2.0.0-beta.9)");

            StopRunningClient();
            CleanupLegacyBuilds();

            var config = JsonSerializer.Deserialize<Bundle>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "builder.json")), Wire.Json)
                ?? throw new IOException("缺少构建描述。");
            var source = Path.Combine(AppContext.BaseDirectory, config.Source);
            await VerifyAsync(source, config.SourceSHA256, false);

            if (Directory.Exists(WorkspaceFolder)) {
                try { Directory.Delete(WorkspaceFolder, true); } catch { }
            }
            HybridBuild.ExtractSnapshot(source, WorkspaceFolder);
            var project = Path.Combine(WorkspaceFolder, "AGY-PC");
            var dotnet = await FindDotnetAsync(config);

            await RunAsync(dotnet, ["run", "--project", "tests/AGY.PC.Tests", "-c", "Release"], project);

            if (Directory.Exists(AppFolder)) {
                try { Directory.Delete(AppFolder, true); } catch { }
            }
            Directory.CreateDirectory(AppFolder);

            await RunAsync(dotnet, ["publish", "src/AGY.PC.WinUI", "-c", "Release", "-p:Platform=x64", "-r", "win-x64", "--self-contained", "true", "-p:WindowsAppSDKSelfContained=true", "-p:PublishSingleFile=false", "-o", AppFolder], project);

            // Clean intermediate build artifacts inside WorkspaceFolder to save disk space
            foreach (var sub in new[] { "src/AGY.PC.WinUI/bin", "src/AGY.PC.WinUI/obj", "src/AGY.PC.Core/bin", "src/AGY.PC.Core/obj", "tests/AGY.PC.Tests/bin", "tests/AGY.PC.Tests/obj" }) {
                var dir = Path.Combine(project, sub.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir)) {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }

            var missing = StartupChecks.MissingFiles(AppFolder);
            if (missing.Length > 0) {
                await CollectAsync(project, AppFolder);
                throw new IOException("发布目录缺少 WinUI 启动文件：" + string.Join(", ", missing) + "。诊断已保存在 " + LogFolder);
            }
            var exe = Path.Combine(AppFolder, "AGY-PC.exe");
            if (!File.Exists(exe)) throw new IOException("Windows 编译未生成 AGY-PC.exe。");

            Say("正在执行 WinUI 启动自检…");
            var check = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppFolder };
            check.ArgumentList.Add("--ui-check");
            check.Environment["AGY_PC_DIAGNOSTICS_DIR"] = Path.Combine(LogFolder, "startup-diagnostics");
            using (var process = Process.Start(check) ?? throw new IOException("无法启动 WinUI 界面检查。")) {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
                catch (TimeoutException) { process.Kill(true); throw new IOException("WinUI 界面检查超时，请查看日志。"); }
                if (process.ExitCode != 0) {
                    await CollectAsync(project, AppFolder);
                    throw new IOException("WinUI 启动失败：" + StartupChecks.ExitDescription(process.ExitCode) + "。请查看 " + Path.Combine(LogFolder, "startup-diagnostics", "diagnostic-summary.txt"));
                }
            }

            foreach (var name in new[] { "README.md", "THIRD_PARTY_NOTICES.md" })
                File.Copy(Path.Combine(project, name), Path.Combine(AppFolder, name), true);
            var licenses = Path.Combine(AppFolder, "licenses");
            Directory.CreateDirectory(licenses);
            foreach (var license in Directory.GetFiles(Path.Combine(project, "docs", "licenses")))
                File.Copy(license, Path.Combine(licenses, Path.GetFileName(license)), true);

            // Also mirror the compiled client into <current folder>\AGY-PC-App so the user has direct access right where they ran the builder
            var localAppOutput = Path.Combine(AppContext.BaseDirectory, "AGY-PC-App");
            try {
                CopyDirectory(AppFolder, localAppOutput);
                Say("本地便携目录：" + localAppOutput);
            } catch (Exception copyErr) {
                Say("提示：复制到当前目录 AGY-PC-App 跳过（" + copyErr.Message + "），主程序固定位于：" + AppFolder);
            }

            Say("编译完成！固定安装目录：" + AppFolder);
            Say("构建日志：" + Path.Combine(LogFolder, "build.log"));
            var launchDir = File.Exists(Path.Combine(localAppOutput, "AGY-PC.exe")) ? localAppOutput : AppFolder;
            Process.Start(new ProcessStartInfo(Path.Combine(launchDir, "AGY-PC.exe")) { UseShellExecute = true, WorkingDirectory = launchDir });
            return 0;
        } catch (Exception error) {
            Say("未完成：" + error.Message);
            Say("日志目录：" + LogFolder);
            if (!Console.IsInputRedirected) { Say("按 Enter 关闭。"); Console.ReadLine(); }
            return 1;
        } finally {
            log?.Dispose();
        }
    }

    private static void StopRunningClient() {
        try {
            foreach (var proc in Process.GetProcessesByName("AGY-PC")) {
                try { proc.Kill(true); proc.WaitForExit(3000); } catch { }
            }
        } catch { }
    }

    private static void CleanupLegacyBuilds() {
        try {
            if (!Directory.Exists(LegacyBuildsRoot)) return;
            Say("正在清理旧版临时构建目录 WindowsBuilds…");
            Directory.Delete(LegacyBuildsRoot, true);
        } catch (Exception e) {
            Say("清理部分旧临时文件跳过：" + e.Message);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir) {
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(sourceDir, dirPath);
            if (rel.StartsWith("ui-check", StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }
        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(sourceDir, filePath);
            if (rel.StartsWith("ui-check", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(filePath, dest, true);
        }
    }

    private static async Task CollectAsync(string project, string output) {
        try {
            var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            await RunAsync(shell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(project, "scripts", "collect-winui-diagnostics.ps1"), "-RuntimeDirectory", output, "-Destination", Path.Combine(LogFolder, "startup-diagnostics"), "-NoLaunch", "-ExpectXaml"], project);
        } catch (Exception error) { Say("补充诊断失败：" + error.Message); }
    }

    private static void Say(string value) { lock (LogGate) { Console.WriteLine(value); log?.WriteLine(value); } }

    private static async Task<string> FindDotnetAsync(Bundle config) {
        try {
            var result = await CaptureAsync("dotnet", ["--list-sdks"], AppContext.BaseDirectory);
            if (result.Split('\n').Any(line => Version.TryParse(line.Split(' ')[0], out var version) && version.Major == 10 && version >= new Version(10, 0, 403))) return "dotnet";
        } catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException) { }
        var exe = Path.Combine(ToolFolder, "dotnet.exe");
        var marker = Path.Combine(ToolFolder, "sdk-verified.json");
        if (File.Exists(exe) && File.Exists(marker)) return exe;
        Say("本机没有所需 .NET SDK，将从 Microsoft 下载固定版本，并校验 SHA-512。仅写入当前用户的 AGY-PC 工具目录。");
        Directory.CreateDirectory(Path.GetDirectoryName(ToolFolder)!);
        var zip = ToolFolder + ".download.zip";
        var uri = new Uri(config.SdkUrl);
        if (uri.Scheme != "https" || uri.Host != "builds.dotnet.microsoft.com") throw new IOException("SDK 下载来源不在微软固定地址。");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)) {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 1024];
            long received = 0, reported = 0;
            int count;
            while ((count = await source.ReadAsync(buffer)) != 0) {
                await target.WriteAsync(buffer.AsMemory(0, count));
                received += count;
                if (received - reported >= 16L * 1024 * 1024) {
                    reported = received;
                    Say($"SDK 已下载 {received / (1024 * 1024)} MiB" + (response.Content.Headers.ContentLength is long total ? $" / {total / (1024 * 1024)} MiB" : ""));
                }
            }
        }
        await VerifyAsync(zip, config.SdkSHA512, true);
        if (Directory.Exists(ToolFolder)) Directory.Delete(ToolFolder, true);
        Directory.CreateDirectory(ToolFolder);
        ZipFile.ExtractToDirectory(zip, ToolFolder);
        File.Delete(zip);
        File.WriteAllText(marker, "{\"sdk\":\"10.0.403\",\"sha512Verified\":true}");
        return exe;
    }

    private static async Task VerifyAsync(string file, string expected, bool sha512) {
        await using var stream = File.OpenRead(file);
        var bytes = sha512 ? await SHA512.HashDataAsync(stream) : await SHA256.HashDataAsync(stream);
        if (!Convert.ToHexString(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("下载或源码包校验失败：" + Path.GetFileName(file));
    }

    private static async Task RunAsync(string file, string[] args, string cwd) {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";
        Say("> " + Path.GetFileName(file) + " " + string.Join(' ', args));
        using var process = Process.Start(info) ?? throw new IOException("无法启动构建工具。");
        async Task Read(StreamReader reader) { while (await reader.ReadLineAsync() is { } line) Say(line); }
        await Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError), process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new IOException("构建命令失败，退出码 " + process.ExitCode);
    }

    private static async Task<string> CaptureAsync(string file, string[] args, string cwd) {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new IOException(await error);
        return await output;
    }

    private sealed record Bundle(string Source, string SourceSHA256, string SdkUrl, string SdkSHA512);
}
