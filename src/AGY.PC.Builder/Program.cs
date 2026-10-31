using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AGY.PC.Core;

internal static class Program {
    private static readonly string ToolFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","BuildTools","dotnet-10.0.403");
    private static readonly string LogFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","WindowsBuilds",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
    private static StreamWriter? log;
    private static readonly object LogGate=new();
    public static async Task<int> Main() {
        Console.OutputEncoding=Encoding.UTF8;
        try {
            if(!OperatingSystem.IsWindows()||RuntimeInformation.OSArchitecture!=Architecture.X64)throw new PlatformNotSupportedException("这个构建入口用于 Windows x64。WSL/Linux 不能完成 WinUI 的 Windows 原生打包步骤。");
            Directory.CreateDirectory(LogFolder);log=new StreamWriter(Path.Combine(LogFolder,"build.log"),false,Encoding.UTF8){AutoFlush=true};
            Say("AGY - PC · WinUI 3 本机构建");Say("将在当前 Windows 上构建、检查浅色/深色界面，并启动新版。不会运行 GitHub Actions。");
            var config=JsonSerializer.Deserialize<Bundle>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"builder.json")),Wire.Json)??throw new IOException("缺少构建描述。");
            var source=Path.Combine(AppContext.BaseDirectory,config.Source);
            await VerifyAsync(source,config.SourceSHA256,false);
            var workspace=Path.Combine(LogFolder,"source");HybridBuild.ExtractSnapshot(source,workspace);
            var project=Path.Combine(workspace,"AGY-PC");
            var dotnet=await FindDotnetAsync(config);
            await RunAsync(dotnet,["run","--project","tests/AGY.PC.Tests","-c","Release"],project);
            var output=Path.Combine(LogFolder,"AGY-PC-WinUI");
            await RunAsync(dotnet,["publish","src/AGY.PC.WinUI","-c","Release","-p:Platform=x64","-r","win-x64","--self-contained","true","-p:WindowsAppSDKSelfContained=true","-p:PublishSingleFile=false","-o",output],project);
            var exe=Path.Combine(output,"AGY-PC.exe");if(!File.Exists(exe))throw new IOException("Windows 编译未生成 AGY-PC.exe。");
            Say("正在运行真正的 WinUI 浅色 / 深色截图检查…");
            var check=new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=output};check.ArgumentList.Add("--ui-check");
            using(var process=Process.Start(check)??throw new IOException("无法启动 WinUI 界面检查。")){
                try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));}
                catch(TimeoutException){process.Kill(true);throw new IOException("WinUI 界面检查超时，请查看日志。");}
                if(process.ExitCode!=0)throw new IOException("WinUI 界面检查退出码："+process.ExitCode);
            }
            var screenshots=Path.Combine(output,"ui-check");
            if(!File.Exists(Path.Combine(screenshots,"login-light.png"))||!File.Exists(Path.Combine(screenshots,"login-dark.png"))||!File.Exists(Path.Combine(screenshots,"report.json")))
                throw new IOException("未生成界面检查截图，不能认定 WinUI 已运行。请查看 "+Path.Combine(output,"ui-check-error.txt"));
            File.WriteAllText(Path.Combine(output,"windows-build-report.json"),JsonSerializer.Serialize(new{builtOnWindows=true,uiCaptureCompleted=true,visualReviewStillRequired=true,sdk="10.0.403",buildID=Path.GetFileName(LogFolder),exeSHA256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(exe)))},Wire.Json));
            foreach(var name in new[]{"README.md","THIRD_PARTY_NOTICES.md"})File.Copy(Path.Combine(project,name),Path.Combine(output,name),true);
            var licenses=Path.Combine(output,"licenses");Directory.CreateDirectory(licenses);foreach(var license in Directory.GetFiles(Path.Combine(project,"docs","licenses")))File.Copy(license,Path.Combine(licenses,Path.GetFileName(license)),true);
            var archive=Path.Combine(LogFolder,"AGY-PC-2.0.0-beta.2-win-x64.zip");ZipFile.CreateFromDirectory(output,archive,CompressionLevel.Optimal,false);
            Say("编译和截图生成完成。截图需要人工确认，不等同于全部交互验收。");Say("运行目录："+output);Say("运行包："+archive);Say("构建日志："+Path.Combine(LogFolder,"build.log"));
            Process.Start(new ProcessStartInfo("explorer.exe",screenshots){UseShellExecute=true});
            Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=output});return 0;
        }catch(Exception error){Say("未完成："+error.Message);Say("日志目录："+LogFolder);if(!Console.IsInputRedirected){Say("按 Enter 关闭。");Console.ReadLine();}return 1;}
        finally{log?.Dispose();}
    }
    private static void Say(string value){lock(LogGate){Console.WriteLine(value);log?.WriteLine(value);}}
    private static async Task<string> FindDotnetAsync(Bundle config){
        try{var result=await CaptureAsync("dotnet",["--list-sdks"],AppContext.BaseDirectory);if(result.Split('\n').Any(line=>Version.TryParse(line.Split(' ')[0],out var version)&&version.Major==10&&version>=new Version(10,0,403)))return "dotnet";}catch(Exception error) when(error is System.ComponentModel.Win32Exception or IOException){ }
        var exe=Path.Combine(ToolFolder,"dotnet.exe");var marker=Path.Combine(ToolFolder,"sdk-verified.json");if(File.Exists(exe)&&File.Exists(marker))return exe;
        Say("本机没有所需 .NET SDK，将从 Microsoft 下载固定版本，并校验 SHA-512。仅写入当前用户的 AGY-PC 工具目录。");
        Directory.CreateDirectory(Path.GetDirectoryName(ToolFolder)!);var zip=ToolFolder+".download.zip";
        var uri=new Uri(config.SdkUrl);if(uri.Scheme!="https"||uri.Host!="builds.dotnet.microsoft.com")throw new IOException("SDK 下载来源不在微软固定地址。");
        using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
        using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead)){response.EnsureSuccessStatusCode();await using var source=await response.Content.ReadAsStreamAsync();await using var target=new FileStream(zip,FileMode.Create,FileAccess.Write,FileShare.None);var buffer=new byte[1024*1024];long received=0,reported=0;int count;while((count=await source.ReadAsync(buffer))!=0){await target.WriteAsync(buffer.AsMemory(0,count));received+=count;if(received-reported>=16L*1024*1024){reported=received;Say($"SDK 已下载 {received/(1024*1024)} MiB"+(response.Content.Headers.ContentLength is long total?$" / {total/(1024*1024)} MiB":""));}}}
        await VerifyAsync(zip,config.SdkSHA512,true);
        if(Directory.Exists(ToolFolder))Directory.Delete(ToolFolder,true);Directory.CreateDirectory(ToolFolder);ZipFile.ExtractToDirectory(zip,ToolFolder);File.Delete(zip);File.WriteAllText(marker,"{\"sdk\":\"10.0.403\",\"sha512Verified\":true}");return exe;
    }
    private static async Task VerifyAsync(string file,string expected,bool sha512){await using var stream=File.OpenRead(file);var bytes=sha512?await SHA512.HashDataAsync(stream):await SHA256.HashDataAsync(stream);if(!Convert.ToHexString(bytes).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("下载或源码包校验失败："+Path.GetFileName(file));}
    private static async Task RunAsync(string file,string[] args,string cwd){var info=new ProcessStartInfo(file){UseShellExecute=false,WorkingDirectory=cwd,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";info.Environment["DOTNET_NOLOGO"]="1";Say("> "+Path.GetFileName(file)+" "+string.Join(' ',args));using var process=Process.Start(info)??throw new IOException("无法启动构建工具。");async Task Read(StreamReader reader){while(await reader.ReadLineAsync() is {} line)Say(line);}await Task.WhenAll(Read(process.StandardOutput),Read(process.StandardError),process.WaitForExitAsync());if(process.ExitCode!=0)throw new IOException("构建命令失败，退出码 "+process.ExitCode);}
    private static async Task<string> CaptureAsync(string file,string[] args,string cwd){var info=new ProcessStartInfo(file){UseShellExecute=false,WorkingDirectory=cwd,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};foreach(var arg in args)info.ArgumentList.Add(arg);using var process=Process.Start(info)??throw new IOException();var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await Task.WhenAll(output,error,process.WaitForExitAsync());if(process.ExitCode!=0)throw new IOException(await error);return await output;}
    private sealed record Bundle(string Source,string SourceSHA256,string SdkUrl,string SdkSHA512);
}
