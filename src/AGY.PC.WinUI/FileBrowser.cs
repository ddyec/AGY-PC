using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace AGY.PC.WinUI;
internal sealed class FileBrowser {
    private readonly MainWindow window;private readonly Workspace work;private readonly bool picking,chooseWorkspace;
    internal Grid View {get;}=new(){RowSpacing=12};
    private readonly ComboBox scope=new(){ItemsSource=new[]{"工作区","已上传","AGY 产物"},SelectedIndex=0,MinWidth=160};
    private readonly TextBox search=new(){PlaceholderText="搜索当前目录"};private readonly TextBlock trail=Ui.Caption("工作区");private readonly ListView entries=new(){SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=false,MinHeight=140};
    private readonly CheckBox hidden=new(){Content="隐藏文件"},recent=new(){Content="最近修改优先"};
    private readonly TextBlock selection=Ui.Caption("选择文件或文件夹");
    private Button reference=null!,download=null!,open=null!;
    private FileListing? listing;private string path="";private int generation;
    private bool busy;
    private async void Act(Func<Task> action){if(busy)return;busy=true;try{await action();}catch(Exception e){trail.Text=e.Message;}finally{busy=false;}}
    private string Scope=>scope.SelectedIndex switch{1=>"uploads",2=>"artifacts",_=>"workspace"};
    public FileBrowser(MainWindow window,Workspace work,bool picking,bool chooseWorkspace){this.window=window;this.work=work;this.picking=picking;this.chooseWorkspace=chooseWorkspace;
        View.RowDefinitions.Add(new(){Height=GridLength.Auto});View.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});View.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var nav=Ui.Inline(Ui.Icon(Symbol.Home,"返回项目根目录",()=>Act(async()=>{path="";await LoadAsync();})),Ui.Icon(Symbol.Back,"上一级",()=>Act(async()=>{path=string.Join('/',path.Split('/',StringSplitOptions.RemoveEmptyEntries).SkipLast(1));await LoadAsync();})),scope);
        var filters=Ui.Inline(hidden,recent);hidden.FontSize=12;recent.FontSize=12;
        var header=Ui.Stack(Ui.Title(chooseWorkspace?"选择项目":picking?"引用文件与文件夹":"文件与产物",24),nav,trail,search,filters);header.Spacing=12;header.Margin=new Thickness(0,12,0,0);View.Children.Add(header);
        var listSurface=Ui.Surface(entries,"CardBackgroundFillColorDefaultBrush",new Thickness(4),12);Grid.SetRow(listSurface,1);View.Children.Add(listSurface);
        var actions=new Grid{ColumnSpacing=8,RowSpacing=8};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        for(var row=0;row<3;row++)actions.RowDefinitions.Add(new(){Height=GridLength.Auto});
        open=Ui.Action("打开目录",Symbol.Folder,()=>Act(OpenAsync));actions.Children.Add(open);
        actions.Children.Add(Ui.Button(chooseWorkspace?"使用此项目":"@ 此文件夹",()=>Act(CurrentAsync),true));
        if(!chooseWorkspace){reference=Ui.Button("@ 选中项",()=>Act(ReferenceAsync));download=Ui.Action("下载",Symbol.Download,()=>Act(DownloadAsync));actions.Children.Add(reference);actions.Children.Add(download);actions.Children.Add(Ui.Action("上传文件",Symbol.Upload,()=>Act(window.UploadAsync)));}
        for(var index=0;index<actions.Children.Count;index++){Grid.SetRow((FrameworkElement)actions.Children[index],index/2);Grid.SetColumn((FrameworkElement)actions.Children[index],index%2);((FrameworkElement)actions.Children[index]).HorizontalAlignment=HorizontalAlignment.Stretch;}
        var footer=Ui.Stack(selection,actions);footer.Spacing=8;Grid.SetRow(footer,2);View.Children.Add(footer);
        entries.SelectionChanged+=(_,_)=>Selection();Selection();
        scope.IsEnabled=!chooseWorkspace;scope.SelectionChanged+=(_,_)=>Act(async()=>{path="";await LoadAsync();});search.TextChanged+=(_,_)=>Filter();hidden.Checked+=(_,_)=>Filter();hidden.Unchecked+=(_,_)=>Filter();recent.Checked+=(_,_)=>Filter();recent.Unchecked+=(_,_)=>Filter();
        entries.DoubleTapped+=(_,_)=>{if(Selected is {Directory:true})Act(OpenAsync);};
        entries.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter&&Selected is {Directory:true}){e.Handled=true;Act(OpenAsync);}};
    }
    public async Task LoadAsync(){var token=++generation;var result=await work.BrowseAsync(Scope,path,chooseWorkspace?work.Status!.Roots[0]:null);if(token!=generation)return;listing=result;trail.Text=path.Length==0?"当前项目":path+"/";Filter();}
    private FileEntry? Selected=>(entries.SelectedItem as ListViewItem)?.Tag as FileEntry;
    private void Filter(){entries.Items.Clear();foreach(var entry in (listing?.Entries??[]).Where(e=>(!chooseWorkspace||e.Directory)&&(hidden.IsChecked==true||!e.Name.StartsWith('.'))&&(search.Text.Length==0||e.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase))).OrderByDescending(e=>e.Directory).ThenBy(e=>recent.IsChecked==true?-e.Modified:0).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase)){
        var row=new Grid{ColumnSpacing=12,Padding=new Thickness(8,8,8,8)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(new SymbolIcon(entry.Directory?Symbol.Folder:Symbol.Document){Width=24,Height=24,VerticalAlignment=VerticalAlignment.Center});
        var name=Ui.Text(entry.Name,14);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;
        var label=Ui.Stack(name,Ui.Caption(entry.Directory?"文件夹 · 可整体引用":FormatSize(entry.Size)));label.Spacing=3;Grid.SetColumn(label,1);row.Children.Add(label);
        var date=Ui.Caption(entry.Modified>0?DateTimeOffset.FromUnixTimeSeconds((long)entry.Modified).ToLocalTime().ToString("MM-dd"):"");date.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(date,2);row.Children.Add(date);
        entries.Items.Add(new ListViewItem{Content=row,Tag=entry,HorizontalContentAlignment=HorizontalAlignment.Stretch});
    }Selection();}
    private static string FormatSize(long size)=>size>=1024*1024?$"{size/(1024d*1024):0.#} MB":size>=1024?$"{size/1024d:0.#} KB":$"{size} B";
    private void Selection(){var item=Selected;selection.Text=item is null?"选择文件或文件夹；双击打开文件夹。":item.Name+(item.Directory?" · 整个文件夹":" · "+FormatSize(item.Size));open.IsEnabled=item?.Directory==true;if(reference is not null)reference.IsEnabled=item is not null;if(download is not null)download.IsEnabled=item is {Directory:false};}
    private async Task OpenAsync(){if(Selected is not{Directory:true} e)return;path=string.Join('/',new[]{path,e.Name}.Where(s=>s.Length>0));await LoadAsync();}
    private Task ReferenceAsync(){if(Selected is {} e){work.AddReference(new(e.Path,FileRules.Label(Scope,path,e.Name,e.Directory),e.Directory));window.ChatPage();}return Task.CompletedTask;}
    private async Task CurrentAsync(){if(listing is not{Root.Length:>0} l)return;var full=l.Root.TrimEnd('/')+(path.Length>0?"/"+path:"");if(chooseWorkspace)await work.ConfigureAsync(work.Settings with{Workspace=full});else{work.AddReference(new(full,FileRules.Label(Scope,"",path.Length==0?"当前项目":path,true),true));window.ChatPage();}}
    private async Task DownloadAsync(){if(Selected is{Directory:false} e)await window.SaveRemoteAsync(e.Path,e.Name);}
    public async Task ShowFolderAsync(string absolute){scope.SelectedIndex=0;if(FileRules.Within(absolute,work.Settings.Workspace))path=absolute[work.Settings.Workspace.TrimEnd('/').Length..].Trim('/');await LoadAsync();}
}
