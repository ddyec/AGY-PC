using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AGY.PC.Core;

namespace AGY.PC.Windows;
internal sealed class MainWindow : Window {
    private readonly Workspace work;
    private readonly Grid root = new(), shell = new();
    private readonly StackPanel offline = new() { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, MaxWidth=460 };
    private readonly TextBlock offlineError=Theme.Text("",14,Theme.Muted);
    private readonly ComboBox presentation=new();
    private readonly ListBox history = new() { Background=Theme.Background, Foreground=Theme.Foreground, BorderThickness=new Thickness(0), DisplayMemberPath="Name" };
    private readonly TextBox search = new() { ToolTip="搜索会话", Margin=new Thickness(0,8,0,8) };
    private readonly TextBox composer = new() { AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, MinHeight=78, MaxHeight=200, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock connection = Theme.Text("尚未连接",12,Theme.Muted), heading = Theme.Text("新对话",24), issue = Theme.Text("",13,Theme.Brush("#F9B38F"));
    private readonly ScrollViewer transcript = new() { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Padding=new Thickness(26,14,26,14) };
    private readonly StackPanel messages = new(), refs = new(), approvals = new(), pending = new();
    private readonly ComboBox model = new(), effort = new(), permission = new(), mode = new();
    private readonly TextBlock project = Theme.Text("工作区",12,Theme.Muted), policy = Theme.Text("",12,Theme.Muted), limits = Theme.Text("点击刷新读取真实额度",12,Theme.Muted);
    private readonly StackPanel controls = new();
    private readonly Button send, stop, apply;
    private readonly DispatcherTimer draftTimer = new() { Interval=TimeSpan.FromMilliseconds(350) };
    private bool rendering, busy, following=true;
    private string signature="", sessionID="";
    private string? localError;
    private readonly Button latest;
    public MainWindow(Workspace work) {
        this.work=work; Title="AGY - PC"; Width=1440; Height=920; MinWidth=1020; MinHeight=680;
        Icon=Theme.Logo(24).Source; WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Content=root;
        shell.ColumnDefinitions.Add(new(){Width=new GridLength(230)});shell.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});shell.ColumnDefinitions.Add(new(){Width=new GridLength(292)});
        root.Children.Add(shell);
        offline.Children.Add(Theme.Logo(72));offline.Children.Add(Theme.Text("AGY - PC",32));offline.Children.Add(Theme.Text("连接你的服务器，继续工作。",17,Theme.Muted));offline.Children.Add(offlineError);offline.Children.Add(Theme.Button("连接服务器",ShowConnection));root.Children.Add(offline);
        var sidebar=new DockPanel { Margin=new Thickness(14) };
        var brand=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,10,0,18) };brand.Children.Add(Theme.Logo(34));brand.Children.Add(Theme.Text("AGY - PC",21));
        var top=Theme.Stack(brand,Theme.Button("＋ 新建对话",()=>Run(()=>{FlushDraft();work.NewConversation();return Task.CompletedTask;})),search);
        DockPanel.SetDock(top,Dock.Top);sidebar.Children.Add(top);
        var bottom=Theme.Stack(Theme.Button("文件与产物",()=>OpenFiles(false)),Theme.Button("刷新会话",()=>Run(work.ReloadAsync)),connection,Theme.Button("重新连接 / 更换密钥",ShowConnection));DockPanel.SetDock(bottom,Dock.Bottom);sidebar.Children.Add(bottom);
        sidebar.Children.Add(history);shell.Children.Add(sidebar);
        search.TextChanged+=(_,_)=>RenderHistory();
        history.SelectionChanged+=(_,_)=> { if(!rendering && history.SelectedItem is Session s && s.Id!=work.Selected?.Id)Run(async()=>{FlushDraft();await work.SelectAsync(s.Id);}); };
        history.ContextMenu=new ContextMenu(); var rename=new MenuItem { Header="重命名" };rename.Click+=(_,_)=>Rename();history.ContextMenu.Items.Add(rename);
        var delete=new MenuItem { Header="删除网关历史" };delete.Click+=(_,_)=>Delete();history.ContextMenu.Items.Add(delete);
        var chat=new Grid();chat.RowDefinitions.Add(new(){Height=GridLength.Auto});chat.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});chat.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetColumn(chat,1);shell.Children.Add(chat);
        heading.Margin=new Thickness(26,18,26,12);chat.Children.Add(heading);
        latest=Theme.Button("↓ 回到最新",()=>{following=true;transcript.ScrollToEnd();});latest.HorizontalAlignment=HorizontalAlignment.Right;
        transcript.Content=messages;Grid.SetRow(transcript,1);chat.Children.Add(transcript);
        transcript.ScrollChanged+=(_,e)=>{if(e.ExtentHeightChange==0){following=transcript.ScrollableHeight-transcript.VerticalOffset<64;latest.Visibility=following?Visibility.Collapsed:Visibility.Visible;}};
        transcript.PreviewMouseWheel+=(_,e)=>{if(e.Delta>0)following=false;};
        var composePanel=new StackPanel { Margin=new Thickness(22,6,22,18) };
        composePanel.Children.Add(latest);composePanel.Children.Add(new ScrollViewer{Content=approvals,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});composePanel.Children.Add(pending);composePanel.Children.Add(issue);composePanel.Children.Add(refs);composePanel.Children.Add(composer);
        var actions=new DockPanel();
        var tools=new StackPanel { Orientation=Orientation.Horizontal };tools.Children.Add(Theme.Button("＋ 上传",Upload));tools.Children.Add(Theme.Button("@ 文件 / 文件夹",()=>OpenFiles(true)));tools.Children.Add(Theme.Text("Ctrl+Enter 发送 · 可拖入文件",11,Theme.Muted));actions.Children.Add(tools);
        var right=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        stop=Theme.Button("停止",()=>Run(work.StopAsync));send=Theme.Button("发送 ↑",()=>Run(async()=>{FlushDraft();following=true;await work.SendAsync();}));send.Background=Theme.Accent;send.Foreground=Theme.Background;right.Children.Add(stop);right.Children.Add(send);actions.Children.Add(right);composePanel.Children.Add(actions);
        Grid.SetRow(composePanel,2);chat.Children.Add(composePanel);
        composer.TextChanged+=(_,_)=>{if(rendering)return;draftTimer.Stop();draftTimer.Start();if(composer.Text.EndsWith('@'))Dispatcher.BeginInvoke(()=>OpenFiles(true));};
        composer.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){e.Handled=true;send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        draftTimer.Tick+=(_,_)=>{draftTimer.Stop();try{FlushDraft();}catch(Exception e){ShowError(e);}};
        AllowDrop=true;DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)UploadPaths(paths,true);};
        var configScroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(14,18,18,18) };configScroll.Content=controls;Grid.SetColumn(configScroll,2);shell.Children.Add(configScroll);
        controls.Children.Add(Theme.Text("任务配置",20));controls.Children.Add(Theme.Text("为当前任务选择模型和执行策略。主动保存的配置会用于新对话。",12,Theme.Muted));
        controls.Children.Add(Theme.Text("模型",13,Theme.Muted));controls.Children.Add(model);controls.Children.Add(Theme.Text("思考强度",13,Theme.Muted));controls.Children.Add(effort);
        controls.Children.Add(Theme.Text("审批策略",13,Theme.Muted));permission.Items.Add(new Choice("review","Restriction · 逐项审批"));permission.Items.Add(new Choice("turbo","Turbo · 自动执行"));controls.Children.Add(permission);controls.Children.Add(policy);
        controls.Children.Add(Theme.Text("执行方式",13,Theme.Muted));mode.Items.Add(new Choice("plan","先做计划"));mode.Items.Add(new Choice("accept-edits","直接实施"));controls.Children.Add(mode);
        apply=Theme.Button("保存配置",()=>Run(ApplySettingsAsync));controls.Children.Add(apply);controls.Children.Add(Theme.Button("刷新可用模型",()=>Run(work.RefreshModelsAsync)));
        controls.Children.Add(Theme.Text("项目文件夹",13,Theme.Muted));controls.Children.Add(project);controls.Children.Add(Theme.Button("浏览并选择项目",()=>OpenFiles(false,true)));
        controls.Children.Add(Theme.Text("输出展示",13,Theme.Muted));presentation.ItemsSource=new[]{"原版","步骤摘要","结论优先"};presentation.SelectedIndex=0;presentation.SelectionChanged+=(_,_)=>{if(rendering)return;try{work.SetPresentation(presentation.SelectedItem as string??"原版");signature="";Render();}catch(Exception e){ShowError(e);}};controls.Children.Add(presentation);
        controls.Children.Add(Theme.Text("账号额度",18));controls.Children.Add(Theme.Button("刷新额度",()=>Run(LoadLimitsAsync)));controls.Children.Add(limits);
        controls.Children.Add(Theme.Text("AGY - PC 1.0.0\n独立客户端 · 官方 AGY 标识\nGoogle 登录由服务器管理",11,Theme.Muted));
        model.SelectionChanged+=(_,_)=>{if(!rendering)PopulateEfforts(work.Settings.Effort);};
        permission.SelectionChanged+=(_,_)=>UpdatePolicy();
        work.Changed+=()=>{if(Dispatcher.CheckAccess())Render();else Dispatcher.BeginInvoke(Render);};
        Closed+=(_,_)=>{draftTimer.Stop();try{FlushDraft();}catch(IOException){}work.Dispose();};
        Loaded+=(_,_)=>{if(work.SavedConnection is {} credentials)Run(async()=>{await work.ConnectAsync(credentials);Render();});else ShowConnection();};
        Render();
    }
    private sealed record Choice(string Id,string Label) { public override string ToString()=>Label; }
    private void UpdatePolicy()=>policy.Text=(permission.SelectedItem as Choice)?.Id=="turbo"?"无需逐项确认即可读写文件和执行工具；AGY 明确禁止的规则仍有效。":"每项工具操作等待你的允许或拒绝；审批只生效一次，超时拒绝。";
    private void PopulateEfforts(string preferred) {
        effort.Items.Clear();if(model.SelectedItem is not ModelFamily family)return;
        foreach(var value in family.Efforts)effort.Items.Add(new Choice(value,FileRules.EffortLabel(value)));
        effort.SelectedItem=effort.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==preferred)??effort.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==family.DefaultEffort);
    }
    private async Task ApplySettingsAsync() {
        FlushDraft();
        if(model.SelectedItem is not ModelFamily family || effort.SelectedItem is not Choice thinking || permission.SelectedItem is not Choice approval || mode.SelectedItem is not Choice agent)throw new InvalidOperationException("请选全模型、思考强度和审批策略。");
        await work.ConfigureAsync(new(family.Id,thinking.Id,approval.Id,agent.Id,work.Settings.Workspace));
    }
    private void FlushDraft(){draftTimer.Stop();if(work.Connected && !rendering)work.UpdateDraft(composer.Text);}
    private async void Run(Func<Task> action) {
        if(busy)return;try{FlushDraft();}catch(Exception error){ShowError(error);return;}busy=true;localError=null;Render();
        try{await action();}catch(Exception error){ShowError(error);}finally{busy=false;Render();}
    }
    private void ShowError(Exception error){localError=error is OperationCanceledException?"操作超时或被取消。任务不会自动重发，请核对会话。":error.Message;Render();}
    private void RenderHistory() {
        var original=rendering;rendering=true;
        history.ItemsSource=work.Sessions.Where(s=>s.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(s=>s.Modified).ToArray();
        history.SelectedItem=history.Items.Cast<Session>().FirstOrDefault(s=>s.Id==work.Selected?.Id);rendering=original;
    }
    private void Render() {
        rendering=true;
        try {
            shell.IsEnabled=work.Connected; shell.Visibility=work.Connected?Visibility.Visible:Visibility.Collapsed;offline.Visibility=work.Connected?Visibility.Collapsed:Visibility.Visible;offline.IsEnabled=!busy;offlineError.Text=localError??(busy?"正在连接…":"沿用 iPhone 的地址与密钥即可。");
            connection.Text=work.ConnectionLabel;heading.Text=work.Selected?.Name??"新对话";
            issue.Text=localError??work.Notice??work.Selected?.Error??"";issue.Visibility=string.IsNullOrEmpty(issue.Text)?Visibility.Collapsed:Visibility.Visible;
            RenderHistory();
            presentation.SelectedItem=work.Presentation;
            model.ItemsSource=work.Families;model.SelectedItem=work.Families.FirstOrDefault(f=>f.Id==work.Settings.Model);PopulateEfforts(work.Settings.Effort);
            permission.SelectedItem=permission.Items.Cast<Choice>().FirstOrDefault(c=>c.Id==work.Settings.ApprovalMode);
            mode.SelectedItem=mode.Items.Cast<Choice>().FirstOrDefault(c=>c.Id==work.Settings.Mode);UpdatePolicy();
            project.Text=work.Settings.Workspace.Split('/',StringSplitOptions.RemoveEmptyEntries).LastOrDefault()??"工作区";
            apply.IsEnabled=!busy&&work.Selected?.Running!=true&&work.Pending is null;
            model.IsEnabled=effort.IsEnabled=permission.IsEnabled=mode.IsEnabled=apply.IsEnabled;
            send.IsEnabled=!busy&&work.Pending is null&&work.Selected?.Running!=true;
            stop.IsEnabled=!busy&&work.Selected?.Running==true;composer.IsReadOnly=work.Pending is not null||busy;
            if((!draftTimer.IsEnabled||sessionID!=(work.Selected?.Id??"new"))&&composer.Text!=work.Draft.Text)composer.Text=work.Draft.Text;
            refs.Children.Clear();foreach(var reference in work.Draft.References){var path=reference.Path;refs.Children.Add(Theme.Button("@ "+reference.Label+"   ×",()=>Run(()=>{work.RemoveReference(path);return Task.CompletedTask;})));}
            pending.Children.Clear();if(work.Pending is {} awaiting){pending.Children.Add(Theme.Text("有一条发送结果待确认，不会自动重发。",13,Theme.Accent));var p=new StackPanel { Orientation=Orientation.Horizontal };p.Children.Add(Theme.Button("核对会话",()=>Run(()=>work.SelectAsync(awaiting.SessionID))));p.Children.Add(Theme.Button("按原编号重试",()=>Run(work.SubmitPendingAsync)));pending.Children.Add(p);}
            approvals.Children.Clear();foreach(var approval in work.Selected?.Approvals??[]){var detail=new TextBox{Text=approval.Parameters.ToString(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var panel=Theme.Stack(Theme.Text("等待审批 · "+approval.Tool,15),new Expander{Header="查看操作参数（仅批准这一次）",Foreground=Theme.Muted,Content=detail});var row=new StackPanel { Orientation=Orientation.Horizontal };row.Children.Add(Theme.Button("拒绝",()=>Run(()=>work.AnswerAsync(approval,false))));row.Children.Add(Theme.Button("允许这一次",()=>Run(()=>work.AnswerAsync(approval,true))));panel.Children.Add(row);approvals.Children.Add(Theme.Card(panel));}
            var key=(work.Selected?.Id??"new")+":"+(work.Selected?.Revision??0);
            if(signature!=key){signature=key;RenderMessages();}
            latest.Visibility=following?Visibility.Collapsed:Visibility.Visible;
        }finally{rendering=false;}
    }
    private void RenderMessages() {
        var id=work.Selected?.Id??"new";var switched=id!=sessionID;sessionID=id;
        var offset=transcript.VerticalOffset;var tail=following||switched;
        if(switched)following=true;
        messages.Children.Clear();
        if(work.Selected?.Messages?.Length>0){foreach(var message in work.Selected.Messages)messages.Children.Add(MessageView.Build(message,work.Selected.Cwd,Download,OpenFolder,presentation.SelectedItem as string??"原版"));}
        else {var welcome=Theme.Stack(Theme.Logo(66),Theme.Text("继续你的工作。",30),Theme.Text("选一个项目，上传资料，或 @ 一个文件夹。",16,Theme.Muted));welcome.Margin=new Thickness(10,70,10,40);messages.Children.Add(welcome);}
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{if(tail&&following)transcript.ScrollToEnd();else transcript.ScrollToVerticalOffset(offset);});
    }
    private void OpenFolder(string path)=>OpenFiles(false,initialAbsolute:path);
    private void OpenFiles(bool picking,bool chooseWorkspace=false,string? initialAbsolute=null) {
        if(!work.Connected||busy)return;
        FlushDraft();var browser=new FilesWindow(work,picking,chooseWorkspace,initialAbsolute){Owner=this};browser.ShowDialog();Render();
    }
    private void Upload() {if(!work.Connected)return;var dialog=new OpenFileDialog { Multiselect=true,Title="上传并引用文件" };if(dialog.ShowDialog(this)==true)UploadPaths(dialog.FileNames,true);}
    private void UploadPaths(string[] paths,bool reference)=>Run(async()=>{
        FlushDraft();if(paths.Length>8)throw new InvalidOperationException("每批最多选择 8 个文件。");
        foreach(var path in paths){if(System.IO.Directory.Exists(path))throw new InvalidOperationException("上传本地文件夹请先压缩；服务器上的文件夹可以直接 @ 引用。");await work.UploadAsync(path,reference);}
    });
    private void Download(string path,string name)=>Run(async()=>{
        var dialog=new SaveFileDialog{FileName=FileRules.SafeName(name),Title="保存 AGY 文件"};if(dialog.ShowDialog(this)!=true)return;
        await work.DownloadAsync(path,dialog.FileName);MessageBox.Show(this,"文件已保存。","AGY - PC",MessageBoxButton.OK,MessageBoxImage.Information);
    });
    private async Task LoadLimitsAsync(){var reply=await work.LimitsAsync();limits.Text=string.Join("\n\n",reply.Groups.Select(g=>g.Name+"\n"+string.Join("\n",g.Buckets.Select(b=>b.Name+"："+(b.Remaining is {} value?value.ToString("P0"):"未提供")+(DateTimeOffset.TryParse(b.Reset,out var reset)?"\n重置 "+reset.ToLocalTime().ToString("g"):"")))));}
    private void Rename(){if(history.SelectedItem is not Session s)return;var name=Dialogs.Prompt(this,"重命名会话",s.Name);if(!string.IsNullOrWhiteSpace(name))Run(()=>work.RenameAsync(s,name));}
    private void Delete(){if(history.SelectedItem is Session s&&MessageBox.Show(this,"只删除网关中的这条历史，不删除项目文件。继续？","删除会话",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)Run(()=>work.DeleteAsync(s));}
    private void ShowConnection() {
        if(busy)return;
        var dialog=new ConnectionWindow(work.SavedConnection){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.Result is {} credentials)Run(()=>work.ConnectAsync(credentials));
    }
}
