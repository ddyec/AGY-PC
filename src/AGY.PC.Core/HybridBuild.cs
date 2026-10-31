using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace AGY.PC.Core;

public enum BuildTarget { Server, Windows, WslLinuxX64 }
public sealed record BuildPlan(BuildTarget Target, string Directory, string Command, string? Distribution = null);
public sealed record BuildResult(int ExitCode, string Log, DateTimeOffset Started, DateTimeOffset Finished);
public static class HybridBuild {
    public static string Label(BuildTarget target) => target switch { BuildTarget.Windows=>"Windows 本机",BuildTarget.WslLinuxX64=>"WSL · Linux x64",_=>"服务器" };
    public static ProcessStartInfo Prepare(BuildPlan plan, string? marker=null) {
        if(plan.Target==BuildTarget.Server) throw new InvalidOperationException("服务器构建通过当前 AGY 会话执行，本机不会代替服务器运行。");
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows / WSL 构建应在 Windows 设备执行。");
        if(string.IsNullOrWhiteSpace(plan.Command))throw new InvalidOperationException("请填写构建命令。");
        if(plan.Command.Length>32768)throw new InvalidOperationException("构建命令过长。");
        var process=new ProcessStartInfo { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        if(plan.Target==BuildTarget.Windows) {
            if(!System.IO.Directory.Exists(plan.Directory))throw new DirectoryNotFoundException("本地项目目录不存在，请先同步或选择本地副本。");
            process.WorkingDirectory=Path.GetFullPath(plan.Directory);
            process.FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            process.ArgumentList.Add("-NoLogo");process.ArgumentList.Add("-NoProfile");process.ArgumentList.Add("-NonInteractive");process.ArgumentList.Add("-Command");process.ArgumentList.Add("$ErrorActionPreference='Stop'; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; "+plan.Command+"\nif ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }");
        } else {
            if(!plan.Directory.StartsWith('/')||plan.Directory.Contains('\0'))throw new InvalidOperationException("WSL 目录必须是 Linux 绝对路径。");
            process.FileName="wsl.exe";
            if(!string.IsNullOrWhiteSpace(plan.Distribution)){process.ArgumentList.Add("--distribution");process.ArgumentList.Add(plan.Distribution);}
            process.ArgumentList.Add("--cd");process.ArgumentList.Add(plan.Directory);process.ArgumentList.Add("--exec");process.ArgumentList.Add("/bin/bash");process.ArgumentList.Add("--noprofile");process.ArgumentList.Add("--norc");process.ArgumentList.Add("-lc");
            // Refuse ARM64 WSL instead of silently producing ARM binaries.
            var build=plan.Command;
            if(marker is not null)build="command -v setsid >/dev/null || { echo 'WSL 缺少 setsid，请安装 util-linux。' >&2; exit 87; }\numask 077\nsetsid /bin/bash --noprofile --norc -lc "+Quote(plan.Command)+" &\ngroup=$!\nprintf '%s' \"$group\" > "+Quote(marker)+"\ntrap 'rm -f "+marker+"' EXIT\nwait \"$group\"\nexit $?";
            process.ArgumentList.Add("if [ \"$(uname -m)\" != x86_64 ]; then printf '%s\\n' '要求 Linux x64；当前 WSL 不是 x86_64。' >&2; exit 86; fi\n"+build);
        }
        process.StandardOutputEncoding=System.Text.Encoding.UTF8;process.StandardErrorEncoding=System.Text.Encoding.UTF8;
        return process;
    }
    private static string Quote(string value)=>"'"+value.Replace("'","'\"'\"'")+"'";
    public static async Task<BuildResult> RunAsync(BuildPlan plan, Action<string> output, CancellationToken ct) {
        var marker=plan.Target==BuildTarget.WslLinuxX64?"/tmp/agy-pc-build-"+Guid.NewGuid().ToString("N")+".pid":null;
        var info=Prepare(plan,marker);using var process=new Process{StartInfo=info};var started=DateTimeOffset.Now;
        ct.ThrowIfCancellationRequested();if(!process.Start())throw new IOException("无法启动构建进程。");
        var canceled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration=ct.Register(()=>canceled.TrySetResult());
        var log=new System.Text.StringBuilder();var gate=new object();
        async Task Read(StreamReader source){while(await source.ReadLineAsync() is {} line){lock(gate){if(log.Length<8*1024*1024)log.AppendLine(line);}output(line);}}
        var completed=Task.WhenAll(Read(process.StandardOutput),Read(process.StandardError),process.WaitForExitAsync());
        if(await Task.WhenAny(completed,canceled.Task)==canceled.Task){
            if(marker is not null){
                try{
                    var stop=new ProcessStartInfo("wsl.exe"){UseShellExecute=false,CreateNoWindow=true};
                    if(!string.IsNullOrWhiteSpace(plan.Distribution)){stop.ArgumentList.Add("-d");stop.ArgumentList.Add(plan.Distribution);}
                    foreach(var arg in new[]{"--exec","/bin/bash","-lc","p=$(cat "+Quote(marker)+" 2>/dev/null); case \"$p\" in ''|*[!0-9]*) exit 0;; esac; if [ \"$p\" -gt 1 ]; then kill -TERM -- -\"$p\" 2>/dev/null || true; sleep 1; kill -KILL -- -\"$p\" 2>/dev/null || true; fi; rm -f "+Quote(marker)})stop.ArgumentList.Add(arg);
                    using var stopper=Process.Start(stop);if(stopper is not null)await stopper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }catch(Exception error) when(error is IOException or System.ComponentModel.Win32Exception or TimeoutException){output("停止 WSL 进程组时遇到问题："+error.Message);}
            }
            try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            try{await completed.WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){}
            ct.ThrowIfCancellationRequested();
        }
        await completed;return new(process.ExitCode,log.ToString(),started,DateTimeOffset.Now);
    }
    public static void ExtractSnapshot(string archive,string destination) {
        if(System.IO.Directory.Exists(destination)&&System.IO.Directory.EnumerateFileSystemEntries(destination).Any())throw new InvalidOperationException("请使用空目录同步，避免覆盖本机改动。");
        using var zip=ZipFile.OpenRead(archive);var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in zip.Entries) {
            var relative=entry.FullName.Replace('\\','/');
            if(relative.StartsWith('/')||relative.Split('/').Any(p=>p==".."||p.Contains(':'))||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("项目快照包含不安全的路径或符号链接。");
            foreach(var part in relative.Split('/',StringSplitOptions.RemoveEmptyEntries))if(FileRules.SafeName(part)!=part)throw new InvalidDataException("项目文件名无法在 Windows 中无损保存："+part);
            if(!seen.Add(relative.TrimEnd('/')))throw new InvalidDataException("项目快照存在大小写冲突或重复路径。");
            var full=Path.GetFullPath(Path.Combine(destination,relative));if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("项目快照越界。");
            total+=entry.Length;if(total>FileRules.MaxFile||zip.Entries.Count>5000)throw new InvalidDataException("项目快照超出 25 MiB / 5000 项限制。");
        }
        System.IO.Directory.CreateDirectory(destination);
        foreach(var entry in zip.Entries) {
            var target=Path.Combine(destination,entry.FullName.Replace('/',Path.DirectorySeparatorChar));
            if(entry.FullName.EndsWith('/'))System.IO.Directory.CreateDirectory(target);
            else{System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,false);}
        }
    }
}
