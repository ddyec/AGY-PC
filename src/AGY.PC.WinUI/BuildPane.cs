using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
namespace AGY.PC.WinUI;
internal sealed class BuildPane {
    private readonly MainWindow window;private readonly Workspace work;private CancellationTokenSource? cancellation;
    internal ScrollViewer View{get;}
    private readonly ComboBox target=new(){Header="执行位置",ItemsSource=new[]{"Windows 本机","WSL · Linux x64","服务器 · 网页 / 服务端"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox folder=new(){Header="本地项目副本（WSL 会自动转换路径）",PlaceholderText="同步项目或选择本地文件夹"},distribution=new(){Header="WSL 发行版（留空使用默认）"},command=new(){Header="构建命令",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=110,PlaceholderText="例如 dotnet publish -c Release -r win-x64"},log=new(){Header="本机 / WSL 构建日志",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,MinHeight=200,MaxHeight=500};
    private string? lastLog;
    public BuildPane(MainWindow window,Workspace work){this.window=window;this.work=work;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};buttons.Children.Add(Ui.Button("同步服务器项目到新副本",()=>window.Run(SynchronizeAsync)));buttons.Children.Add(Ui.Button("选择本机目录",()=>window.Run(async()=>{var selected=await window.PickFolderAsync();if(selected is not null)folder.Text=selected;})));
        var actions=new Grid{ColumnSpacing=10,RowSpacing=10};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.RowDefinitions.Add(new(){Height=GridLength.Auto});actions.RowDefinitions.Add(new(){Height=GridLength.Auto});actions.Children.Add(Ui.Button("检查并执行",()=>window.Run(StartAsync),true));actions.Children.Add(Ui.Button("停止本机 / WSL 构建",Cancel));actions.Children.Add(Ui.Button("回传构建日志",()=>window.Run(UploadLogAsync)));actions.Children.Add(Ui.Button("回传产物文件",()=>window.Run(UploadArtifactsAsync)));
        for(var index=0;index<actions.Children.Count;index++){Grid.SetRow((FrameworkElement)actions.Children[index],index/2);Grid.SetColumn((FrameworkElement)actions.Children[index],index%2);}
        var panel=Ui.Stack(Ui.Text("混合构建",28),Ui.Caption("Windows 程序在本机编译，Linux x64 进入 WSL，网页与服务端任务留在服务器。不会把 ARM64 服务器伪装成 x64。"),target,folder,buttons,distribution,command,Ui.Caption("执行前会展示目标、目录和完整命令。服务器构建通过当前 AGY 会话审批；本机 / WSL 命令需要你单独确认。同步总是写入新目录，不覆盖本机改动。"),actions,log,Ui.Caption("回传日志和产物不会自动发起新任务；可在“已上传”中查看并引用。单个回传文件目前上限 25 MiB。WSL 内部独立工程的大文件同步和无人值守远程构建调度尚未提供。"));
        panel.MaxWidth=1000;View=new(){Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8)};
        View.Loaded+=(_,_)=>{if(work.Build is {} saved){target.SelectedIndex=saved.Target switch{BuildTarget.Windows=>0,BuildTarget.WslLinuxX64=>1,_=>2};folder.Text=saved.Directory;command.Text=saved.Command;distribution.Text=saved.Distribution??"";}};
    }
    private BuildTarget Target=>target.SelectedIndex switch{1=>BuildTarget.WslLinuxX64,2=>BuildTarget.Server,_=>BuildTarget.Windows};
    private async Task SynchronizeAsync(){var destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","Workspaces",Guid.NewGuid().ToString("N"),Ui.Tail(work.Settings.Workspace));await work.SynchronizeProjectAsync(destination);folder.Text=destination;log.Text="项目已同步到独立本机副本。没有覆盖已有目录。";}
    private async Task StartAsync(){
        var plan=new BuildPlan(Target,Target==BuildTarget.Server?work.Settings.Workspace:folder.Text,command.Text,distribution.Text);
        if(string.IsNullOrWhiteSpace(plan.Command))throw new InvalidOperationException("请填写构建命令。");
        var result=await window.DialogAsync("确认构建执行位置",Ui.Stack(Ui.Text(HybridBuild.Label(plan.Target),20),Ui.Text(plan.Directory),new TextBox{Text=plan.Command,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=240}),"执行这一次");if(result!=ContentDialogResult.Primary)return;
        work.RememberBuild(plan);
        if(plan.Target==BuildTarget.Server){if(!string.IsNullOrWhiteSpace(work.Draft.Text)||work.Draft.References.Length>0)throw new InvalidOperationException("请先处理当前聊天草稿，再提交服务器构建。");work.UpdateDraft("请在当前服务器项目中执行下面的构建命令，并报告退出码、日志和产物。遵守当前审批策略。\n```sh\n"+plan.Command+"\n```");await work.SendAsync();window.ChatPage();return;}
        if(plan.Target==BuildTarget.WslLinuxX64&&!plan.Directory.StartsWith('/'))plan=plan with{Directory=await WslPathAsync(plan.Directory,plan.Distribution)};
        cancellation?.Dispose();cancellation=new();log.Text="执行位置："+HybridBuild.Label(plan.Target)+"\n";var lines=new System.Text.StringBuilder();
        try{var completed=await HybridBuild.RunAsync(plan,line=>window.DispatcherQueue.TryEnqueue(()=>{if(lines.Length<2*1024*1024){lines.AppendLine(line);log.Text=lines.ToString();}}),cancellation.Token);log.Text=completed.Log+"\n退出码："+completed.ExitCode;var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","BuildLogs");Directory.CreateDirectory(directory);lastLog=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");await File.WriteAllTextAsync(lastLog,"目标："+HybridBuild.Label(plan.Target)+"\n目录："+plan.Directory+"\n命令："+plan.Command+"\n"+log.Text);}
        finally{cancellation.Dispose();cancellation=null;}
    }
    private static async Task<string> WslPathAsync(string path,string? distro){var start=new System.Diagnostics.ProcessStartInfo("wsl.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};if(!string.IsNullOrWhiteSpace(distro)){start.ArgumentList.Add("-d");start.ArgumentList.Add(distro);}start.ArgumentList.Add("--exec");start.ArgumentList.Add("wslpath");start.ArgumentList.Add("-a");start.ArgumentList.Add("-u");start.ArgumentList.Add(path);using var process=System.Diagnostics.Process.Start(start)??throw new IOException("无法调用 WSL，请先安装发行版。");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var value=(await output).Trim();if(process.ExitCode!=0||!value.StartsWith('/'))throw new IOException("WSL 路径转换失败："+await error);return value;}
    private async Task UploadLogAsync(){if(lastLog is null)throw new InvalidOperationException("请先完成一次本机构建。");await work.UploadAsync(lastLog,false);log.Text+="\n构建日志已回传到当前会话上传区。";}
    private async Task UploadArtifactsAsync(){var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(window));picker.FileTypeFilter.Add("*");var selected=await picker.PickMultipleFilesAsync();if(selected.Count>8)throw new InvalidOperationException("每批最多回传 8 个产物。");foreach(var file in selected)await work.UploadAsync(file.Path,false);log.Text+="\n已回传 "+selected.Count+" 个产物到会话上传区。";}
    public void Cancel()=>cancellation?.Cancel();
}
