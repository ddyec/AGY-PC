using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AGY.PC.Core;

namespace AGY.PC.Windows;
internal sealed class FilesWindow : Window {
    private sealed record RootChoice(string Path) { public override string ToString()=>Path.Split('/',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"工作区"; }
    private readonly Workspace work;
    private readonly bool picking, chooseWorkspace;
    private readonly ComboBox source=new(), roots=new();
    private readonly TextBox search=new();
    private readonly CheckBox hidden=new(){Content="隐藏文件"},recent=new(){Content="最近修改优先"};
    private readonly ListView list=new(){Background=Theme.Background,Foreground=Theme.Foreground,BorderBrush=Theme.Border};
    private readonly TextBlock breadcrumb=Theme.Text("工作区",14,Theme.Accent),status=Theme.Text("",13,Theme.Muted);
    private readonly Button open,reference,download,current;
    private FileListing? listing;
    private string path="",scope="workspace";
    private int generation;
    private bool busy;
    private readonly StackPanel header=new();
    public FilesWindow(Workspace work,bool picking,bool chooseWorkspace,string? initialAbsolute=null) {
        this.work=work;this.picking=picking;this.chooseWorkspace=chooseWorkspace;
        Title=chooseWorkspace?"选择项目文件夹":picking?"@ 引用文件或整个文件夹":"文件与 AGY 产物";Width=900;Height=680;MinWidth=680;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel{Margin=new Thickness(22)};Content=panel;
        var top=header;DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        var nav=new StackPanel{Orientation=Orientation.Horizontal};nav.Children.Add(Theme.Button("⌂ 根目录",()=>{path="";Load();}));nav.Children.Add(Theme.Button("↑ 上一级",()=>{path=string.Join('/',path.Split('/',StringSplitOptions.RemoveEmptyEntries).SkipLast(1));Load();}));nav.Children.Add(Theme.Button("刷新",Load));
        source.ItemsSource=new[]{"工作区","已上传","AGY 产物"};source.SelectedIndex=0;source.Width=140;source.Margin=new Thickness(10,3,8,3);source.IsEnabled=!chooseWorkspace;nav.Children.Add(source);
        roots.ItemsSource=work.Status!.Roots.Select(r=>new RootChoice(r)).ToArray();roots.SelectedIndex=0;roots.Visibility=chooseWorkspace?Visibility.Visible:Visibility.Collapsed;roots.Width=160;roots.ToolTip="选择允许的工作区";nav.Children.Add(roots);top.Children.Add(nav);top.Children.Add(breadcrumb);
        top.Children.Add(Theme.Text("双击文件夹可打开；选中文件夹后点 @，会引用整个文件夹。",12,Theme.Muted));
        var filter=new DockPanel();DockPanel.SetDock(hidden,Dock.Right);filter.Children.Add(hidden);DockPanel.SetDock(recent,Dock.Right);filter.Children.Add(recent);filter.Children.Add(search);top.Children.Add(filter);
        var foot=new StackPanel();DockPanel.SetDock(foot,Dock.Bottom);panel.Children.Add(foot);foot.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};
        open=Theme.Button("打开 / 预览",()=>{if(list.SelectedItem is FileEntry entry){if(entry.Directory)Enter(entry);else Preview(entry);}});
        reference=Theme.Button("@ 引用选中项",()=>{if(list.SelectedItem is FileEntry entry)Choose(entry);});
        current=Theme.Button(chooseWorkspace?"使用此项目文件夹":"@ 引用当前整个文件夹",ChooseCurrent);
        download=Theme.Button("下载文件",()=>{if(list.SelectedItem is FileEntry entry&&!entry.Directory)Download(entry);});
        actions.Children.Add(open);if(!chooseWorkspace)actions.Children.Add(reference);actions.Children.Add(current);if(!chooseWorkspace)actions.Children.Add(download);
        if(!chooseWorkspace)actions.Children.Add(Theme.Button("＋ 上传文件",Upload));foot.Children.Add(actions);
        list.View=new GridView();((GridView)list.View).Columns.Add(new GridViewColumn{Header="名称",DisplayMemberBinding=new System.Windows.Data.Binding("Label"),Width=460});((GridView)list.View).Columns.Add(new GridViewColumn{Header="字节",DisplayMemberBinding=new System.Windows.Data.Binding("Size"),Width=110});panel.Children.Add(list);
        list.MouseDoubleClick+=(_,_)=>{if(busy)return;if(list.SelectedItem is FileEntry entry){if(entry.Directory)Enter(entry);else if(picking)Choose(entry);else Preview(entry);}};
        list.SelectionChanged+=(_,_)=>UpdateButtons();
        source.SelectionChanged+=(_,_)=>{scope=source.SelectedIndex switch{1=>"uploads",2=>"artifacts",_=>"workspace"};path="";Load();};
        roots.SelectionChanged+=(_,_)=>{path="";Load();};search.TextChanged+=(_,_)=>Filter();hidden.Click+=(_,_)=>Filter();recent.Click+=(_,_)=>Filter();
        Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="请等待当前操作完成后关闭。";}};
        Loaded+=(_,_)=>{if(initialAbsolute is {} absolute&&FileRules.Within(absolute,work.Settings.Workspace)){path=absolute[work.Settings.Workspace.TrimEnd('/').Length..].Trim('/');}Load();};
    }
    private void UpdateButtons(){header.IsEnabled=!busy;list.IsEnabled=!busy;var entry=list.SelectedItem as FileEntry;open.IsEnabled=!busy&&entry is not null;reference.IsEnabled=!busy&&entry is not null&&work.Pending is null;download.IsEnabled=!busy&&entry is{Directory:false};current.IsEnabled=!busy&&listing is{Root.Length:>0}&&(!chooseWorkspace||work.Selected is null);}
    private async void Load() {
        if(!IsLoaded||busy)return;var token=++generation;busy=true;listing=null;UpdateButtons();status.Text="正在读取…";
        try{var result=await work.BrowseAsync(scope,path,chooseWorkspace?(roots.SelectedItem as RootChoice)?.Path:null);if(token!=generation)return;listing=result;breadcrumb.Text=path.Length==0?source.SelectedItem?.ToString()??"工作区":path+"/";status.Text=$"{result.Entries.Length} 项";Filter();}
        catch(Exception error){status.Text=error.Message;list.ItemsSource=null;}finally{if(token==generation){busy=false;UpdateButtons();}}
    }
    private void Filter(){var entries=(listing?.Entries??[]).Where(e=>(!chooseWorkspace||e.Directory)&&(hidden.IsChecked==true||!e.Name.StartsWith('.'))&&(search.Text.Length==0||e.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)));list.ItemsSource=recent.IsChecked==true?entries.OrderByDescending(e=>e.Directory).ThenByDescending(e=>e.Modified).ToArray():entries.OrderByDescending(e=>e.Directory).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();}
    private void Enter(FileEntry entry){path=string.Join('/',new[]{path,entry.Name}.Where(x=>x.Length>0));search.Text="";Load();}
    private void Choose(FileEntry entry){try{work.AddReference(new(entry.Path,FileRules.Label(scope,path,entry.Name,entry.Directory),entry.Directory));if(picking)DialogResult=true;else status.Text="已加入聊天引用。";}catch(Exception e){status.Text=e.Message;}}
    private async void ChooseCurrent(){if(listing is not{Root.Length:>0} l)return;try{var full=l.Root.TrimEnd('/')+(path.Length>0?"/"+path:"");if(chooseWorkspace){await work.ConfigureAsync(work.Settings with{Workspace=full});DialogResult=true;}else{work.AddReference(new(full,path.Length==0?"工作区/":path+"/",true));if(picking)DialogResult=true;else status.Text="已引用整个文件夹。";}}catch(Exception e){status.Text=e.Message;}}
    private async void Download(FileEntry entry){var dialog=new SaveFileDialog{FileName=FileRules.SafeName(entry.Name)};if(dialog.ShowDialog(this)!=true)return;await Operate(async()=>{await work.DownloadAsync(entry.Path,dialog.FileName);status.Text="下载完成。";});}
    private async void Preview(FileEntry entry){await Operate(async()=>{var folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"AGY-PC",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(folder);var local=System.IO.Path.Combine(folder,FileRules.SafeName(entry.Name));await work.DownloadAsync(entry.Path,local);new PreviewWindow(local){Owner=this}.ShowDialog();status.Text="预览文件已下载。";});}
    private async Task Operate(Func<Task> action){if(busy)return;busy=true;UpdateButtons();try{await action();}catch(Exception e){status.Text=e.Message;}finally{busy=false;UpdateButtons();}}
    private void Upload(){var dialog=new OpenFileDialog{Multiselect=true,Title="上传到当前会话（不会发送任务）"};if(dialog.ShowDialog(this)!=true)return;if(dialog.FileNames.Length>8){status.Text="每批最多 8 个文件。";return;}_=Operate(async()=>{var completed=0;try{foreach(var file in dialog.FileNames){status.Text=$"正在上传 {completed+1}/{dialog.FileNames.Length}";await work.UploadAsync(file,false);completed++;}}finally{status.Text=$"已上传 {completed}/{dialog.FileNames.Length} 个文件";}scope="uploads";source.SelectedIndex=1;path="";busy=false;Load();});}
}
