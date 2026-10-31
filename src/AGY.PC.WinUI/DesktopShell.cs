using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AGY.PC.WinUI;

// The session list always has a full-width pane. Narrow windows use an overlay,
// never the NavigationView compact rail that clipped the custom history content.
internal sealed class DesktopShell {
    internal SplitView View { get; } = new() { OpenPaneLength=256, CompactPaneLength=0, DisplayMode=SplitViewDisplayMode.Inline, IsPaneOpen=true };
    internal Grid Pages { get; } = new();
    private readonly ListView destinations = new() { SelectionMode=ListViewSelectionMode.Single, IsItemClickEnabled=true };
    private readonly TextBlock location=Ui.Text("工作区",13), pageName=Ui.Text("对话",13);
    private readonly Button settings;
    private bool changing, narrow;
    internal string Page { get; private set; } = "chat";
    internal event Action<string>? Navigated;
    internal DesktopShell(ListView history,TextBox search,TextBlock connection,Action newChat,Action chooseProject) {
        var pane=new Grid { Padding=new Thickness(16,18,16,14), RowSpacing=12 };
        foreach(var size in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})pane.RowDefinitions.Add(new(){Height=size});
        var logo=new StackPanel { Orientation=Orientation.Horizontal, Spacing=10, Margin=new Thickness(8,0,0,8) };
        logo.Children.Add(Ui.Logo(30));logo.Children.Add(Ui.Title("AGY",22));pane.Children.Add(logo);
        var create=Ui.Action("新建对话",Symbol.Add,newChat,true);create.HorizontalAlignment=HorizontalAlignment.Stretch;create.HorizontalContentAlignment=HorizontalAlignment.Left;Grid.SetRow(create,1);pane.Children.Add(create);
        foreach(var item in new[]{("chat","对话",Symbol.Message),("files","文件与产物",Symbol.Folder),("build","混合构建",Symbol.Repair)}) {
            var row=Ui.Inline(new SymbolIcon(item.Item3){Width=20,Height=20},Ui.Text(item.Item2,14));
            destinations.Items.Add(new ListViewItem{Content=row,Tag=item.Item1,Padding=new Thickness(12,10,12,10),HorizontalContentAlignment=HorizontalAlignment.Stretch});
        }
        Grid.SetRow(destinations,2);pane.Children.Add(destinations);
        search.PlaceholderText="搜索对话";search.FontSize=13;
        var historyHeader=Ui.Stack(Ui.Caption("最近对话"),search);historyHeader.Spacing=8;historyHeader.Margin=new Thickness(4,8,4,0);Grid.SetRow(historyHeader,3);pane.Children.Add(historyHeader);
        history.MaxHeight=double.PositiveInfinity;history.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        history.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Padding='4,8'><TextBlock Text='{Binding Name}' FontSize='13' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis' ToolTipService.ToolTip='{Binding Name}'/></Grid></DataTemplate>");
        Grid.SetRow(history,4);pane.Children.Add(history);
        settings=Ui.Action("配置与额度",Symbol.Setting,()=>Navigate("settings"));settings.HorizontalAlignment=HorizontalAlignment.Stretch;settings.HorizontalContentAlignment=HorizontalAlignment.Left;
        connection.FontSize=11;connection.Margin=new Thickness(12,0,0,0);
        var footer=Ui.Stack(settings,connection);footer.Spacing=8;Grid.SetRow(footer,5);pane.Children.Add(footer);View.Pane=pane;
        var content=new Grid();content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var top=new Grid { Padding=new Thickness(20,12,20,12),ColumnSpacing=12 };
        top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});top.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var toggle=Ui.Icon(Symbol.GlobalNavigationButton,"展开或收起会话列表",()=>View.IsPaneOpen=!View.IsPaneOpen);top.Children.Add(toggle);
        var breadcrumb=Ui.Inline(pageName,Ui.Caption("/"),location);breadcrumb.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(breadcrumb,1);top.Children.Add(breadcrumb);
        var project=Ui.Action("项目",Symbol.Folder,chooseProject);project.FontSize=12;Grid.SetColumn(project,2);top.Children.Add(project);content.Children.Add(top);
        var pageSurface=Ui.Surface(Pages,"LayerFillColorDefaultBrush",new Thickness(24,8,24,20),16);pageSurface.Margin=new Thickness(0,0,12,12);Grid.SetRow(pageSurface,1);content.Children.Add(pageSurface);View.Content=content;
        destinations.SelectionChanged+=(_,_)=>{if(!changing&&destinations.SelectedItem is ListViewItem item)Navigate(item.Tag as string??"chat");};
    }
    internal void Navigate(string page) {
        Page=page;changing=true;destinations.SelectedItem=destinations.Items.Cast<ListViewItem>().FirstOrDefault(i=>i.Tag as string==page);changing=false;
        pageName.Text=page switch{"files"=>"文件与产物","build"=>"混合构建","settings"=>"配置与额度",_=>"对话"};
        if(narrow)View.IsPaneOpen=false;Navigated?.Invoke(page);
    }
    internal void SetProject(string project) {location.TextWrapping=TextWrapping.NoWrap;location.Text=project;location.TextTrimming=TextTrimming.CharacterEllipsis;}
    internal void Resize(double width) {
        location.MaxWidth=Math.Clamp(width-500,60,180);var next=width<980;if(next==narrow)return;narrow=next;
        View.DisplayMode=narrow?SplitViewDisplayMode.Overlay:SplitViewDisplayMode.Inline;View.IsPaneOpen=!narrow;
    }
}
