using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace AGY.PC.WinUI;
internal sealed class FileBrowser {
    private readonly MainWindow window;private readonly Workspace work;private readonly bool picking,chooseWorkspace;
    internal Grid View {get;}=new(){RowSpacing=12};
    private readonly ComboBox scope=new(){ItemsSource=new[]{"工作区","已上传","AGY 产物"},SelectedIndex=0,MinWidth=160};
    private readonly TextBox search=new(){PlaceholderText="搜索当前目录"};private readonly TextBlock trail=Ui.Caption("工作区");private readonly ListView entries=new(){SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true,MinHeight=220,MaxHeight=650};
    private readonly CheckBox hidden=new(){Content="隐藏文件"},recent=new(){Content="最近修改优先"};
    private FileListing? listing;private string path="";private int generation;
    private bool busy;
    private async void Act(Func<Task> action){if(busy)return;busy=true;try{await action();}catch(Exception e){trail.Text=e.Message;}finally{busy=false;}}
    private string Scope=>scope.SelectedIndex switch{1=>"uploads",2=>"artifacts",_=>"workspace"};
    public FileBrowser(MainWindow window,Workspace work,bool picking,bool chooseWorkspace){this.window=window;this.work=work;this.picking=picking;this.chooseWorkspace=chooseWorkspace;
        View.RowDefinitions.Add(new(){Height=GridLength.Auto});View.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});View.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var nav=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};nav.Children.Add(Ui.Button("根目录",()=>Act(async()=>{path="";await LoadAsync();})));nav.Children.Add(Ui.Button("上一级",()=>Act(async()=>{path=string.Join('/',path.Split('/',StringSplitOptions.RemoveEmptyEntries).SkipLast(1));await LoadAsync();})));nav.Children.Add(scope);
        var filters=new StackPanel{Orientation=Orientation.Horizontal,Spacing=16};filters.Children.Add(hidden);filters.Children.Add(recent);
        var header=Ui.Stack(Ui.Text(chooseWorkspace?"选择项目目录":picking?"@ 文件与整个文件夹":"文件与产物",24),nav,trail,search,filters);View.Children.Add(header);
        Grid.SetRow(entries,1);View.Children.Add(entries);
        var actions=new Grid{ColumnSpacing=8,RowSpacing=8};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});for(var row=0;row<3;row++)actions.RowDefinitions.Add(new(){Height=GridLength.Auto});actions.Children.Add(Ui.Button("打开目录",()=>Act(OpenAsync)));
        actions.Children.Add(Ui.Button(chooseWorkspace?"使用当前项目":"@ 当前整个文件夹",()=>Act(CurrentAsync)));
        if(!chooseWorkspace){actions.Children.Add(Ui.Button("@ 选中文件 / 文件夹",()=>Act(ReferenceAsync)));actions.Children.Add(Ui.Button("下载文件",()=>Act(DownloadAsync)));actions.Children.Add(Ui.Button("上传",()=>Act(window.UploadAsync)));}
        for(var index=0;index<actions.Children.Count;index++){Grid.SetRow((FrameworkElement)actions.Children[index],index/2);Grid.SetColumn((FrameworkElement)actions.Children[index],index%2);}
        Grid.SetRow(actions,2);View.Children.Add(actions);
        scope.IsEnabled=!chooseWorkspace;scope.SelectionChanged+=(_,_)=>Act(async()=>{path="";await LoadAsync();});search.TextChanged+=(_,_)=>Filter();hidden.Checked+=(_,_)=>Filter();hidden.Unchecked+=(_,_)=>Filter();recent.Checked+=(_,_)=>Filter();recent.Unchecked+=(_,_)=>Filter();
        entries.ItemClick+=(_,e)=>{if(e.ClickedItem is ListViewItem item&&item.Tag is FileEntry entry){entries.SelectedItem=item;if(entry.Directory)Act(OpenAsync);}};
    }
    public async Task LoadAsync(){var token=++generation;var result=await work.BrowseAsync(Scope,path,chooseWorkspace?work.Status!.Roots[0]:null);if(token!=generation)return;listing=result;trail.Text=path.Length==0?"当前项目":path+"/";Filter();}
    private FileEntry? Selected=>(entries.SelectedItem as ListViewItem)?.Tag as FileEntry;
    private void Filter(){entries.Items.Clear();foreach(var entry in (listing?.Entries??[]).Where(e=>(!chooseWorkspace||e.Directory)&&(hidden.IsChecked==true||!e.Name.StartsWith('.'))&&(search.Text.Length==0||e.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase))).OrderByDescending(e=>e.Directory).ThenBy(e=>recent.IsChecked==true?-e.Modified:0).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase)){var row=Ui.Stack(Ui.Text((entry.Directory?"📁  ":"📄  ")+entry.Name,14),Ui.Caption(entry.Directory?"文件夹 · 可整体引用":$"{entry.Size:N0} 字节"));entries.Items.Add(new ListViewItem{Content=row,Tag=entry,HorizontalContentAlignment=HorizontalAlignment.Stretch});}}
    private async Task OpenAsync(){if(Selected is not{Directory:true} e)return;path=string.Join('/',new[]{path,e.Name}.Where(s=>s.Length>0));await LoadAsync();}
    private Task ReferenceAsync(){if(Selected is {} e){work.AddReference(new(e.Path,FileRules.Label(Scope,path,e.Name,e.Directory),e.Directory));window.ChatPage();}return Task.CompletedTask;}
    private async Task CurrentAsync(){if(listing is not{Root.Length:>0} l)return;var full=l.Root.TrimEnd('/')+(path.Length>0?"/"+path:"");if(chooseWorkspace)await work.ConfigureAsync(work.Settings with{Workspace=full});else{work.AddReference(new(full,FileRules.Label(Scope,"",path.Length==0?"当前项目":path,true),true));window.ChatPage();}}
    private async Task DownloadAsync(){if(Selected is{Directory:false} e)await window.SaveRemoteAsync(e.Path,e.Name);}
    public async Task ShowFolderAsync(string absolute){scope.SelectedIndex=0;if(FileRules.Within(absolute,work.Settings.Workspace))path=absolute[work.Settings.Workspace.TrimEnd('/').Length..].Trim('/');await LoadAsync();}
}
