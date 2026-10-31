using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace AGY.PC.WinUI;
internal sealed class MainWindow : Window {
    private readonly Workspace work;
    private readonly Grid root=Ui.Root(),host=new();
    private readonly DesktopShell shell;
    private readonly Grid conversationBody=new();
    private readonly TextBlock quickModelLabel=new(){TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,FontSize=12};
    private readonly ScrollViewer approvalScroll=new(){MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},referenceScroll=new(){MaxHeight=96,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly Button quickModel=new(){MinHeight=36};
    private readonly TextBlock taskSummary=Ui.Caption("选择模型与任务方式");
    private UIElement welcome=null!;
    private string historySignature="",effortFamily="";
    private ModelFamily[]? catalog;
    private readonly InfoBar notice=new(){IsClosable=true};
    private readonly TextBox composer=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=64,MaxHeight=180,FontSize=14,BorderThickness=new Thickness(0),Padding=new Thickness(0,4,0,8),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),PlaceholderText="描述任务，或输入 @ 引用文件与文件夹…"};
    private readonly StackPanel messages=new(){Spacing=24,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch},references=new(){Spacing=4},approvals=new(){Spacing=8},pending=new(){Spacing=8};
    private readonly ScrollViewer transcript=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock heading=Ui.Title("新对话",20),connection=Ui.Caption("尚未连接");
    private readonly ComboBox model=new(){Header="模型",HorizontalAlignment=HorizontalAlignment.Stretch},effort=new(){Header="思考强度",HorizontalAlignment=HorizontalAlignment.Stretch},permission=new(){Header="审批策略",HorizontalAlignment=HorizontalAlignment.Stretch},mode=new(){Header="执行方式",HorizontalAlignment=HorizontalAlignment.Stretch},theme=new(){Header="界面主题",HorizontalAlignment=HorizontalAlignment.Stretch},presentation=new(){Header="输出展示",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox address=new(){Header="服务器地址",Text="https://agy.yectech.uk",PlaceholderText="https://你的服务器"};
    private readonly PasswordBox token=new(){Header="连接密钥",PasswordRevealMode=PasswordRevealMode.Peek};
    private readonly ProgressRing progress=new(){IsActive=false,Width=24,Height=24};
    private readonly ListView history=new(){IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single,MaxHeight=440};
    private readonly TextBox historySearch=new(){PlaceholderText="搜索会话"};
    private readonly Button send,stop,save,latest;
    private readonly FileBrowser files;
    private readonly BuildPane builds;
    private readonly bool uiCheck;
    private bool busy,rendering,following=true,mentionOpen;
    private string signature="",session="";
    private readonly UIElement login,chat,settings;
    public MainWindow(Workspace work,bool uiCheck) {
        StartupLog.Mark("window.constructor.enter");
        this.work=work;this.uiCheck=uiCheck;
        Title="AGY - PC";AppWindow.Resize(new Windows.Graphics.SizeInt32(1280,860));try{AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AGY.ico"));}catch(Exception e){StartupLog.Error("window.icon.optional",e);}
        try{if(Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())SystemBackdrop=new MicaBackdrop();}catch(Exception e){StartupLog.Error("window.mica.optional",e);}
        Content=root;
        root.ActualThemeChanged+=(_,_)=>{SyncTitleBar();DispatcherQueue.TryEnqueue(SyncTitleBar);};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        notice.Margin=new Thickness(16,8,16,0);root.Children.Add(notice);Grid.SetRow(host,1);root.Children.Add(host);
        login=MakeLogin();host.Children.Add(login);
        send=Ui.Action("发送",Symbol.Send,()=>Run(async()=>{FlushDraft();following=true;await work.SendAsync();}),true);
        stop=Ui.Icon(Symbol.Stop,"停止生成",()=>Run(work.StopAsync));
        save=Ui.Button("保存任务配置",()=>Run(SaveSettingsAsync),true);
        latest=Ui.Button("↓ 回到最新",()=>{following=true;transcript.ChangeView(null,transcript.ScrollableHeight,null);});latest.HorizontalAlignment=HorizontalAlignment.Center;latest.Visibility=Visibility.Collapsed;
        shell=new DesktopShell(history,historySearch,connection,()=>Run(()=>{work.NewConversation();ChatPage();return Task.CompletedTask;}),()=>Run(ChooseWorkspaceAsync));host.Children.Add(shell.View);
        chat=MakeChat();settings=MakeSettings();files=new FileBrowser(this,work,false,false);builds=new BuildPane(this,work);
        shell.Pages.Children.Add(chat);shell.Pages.Children.Add(files.View);shell.Pages.Children.Add(builds.View);shell.Pages.Children.Add(settings);
        shell.Navigated+=ShowPage;shell.Navigate("chat");
        root.SizeChanged+=(_,_)=>shell.Resize(root.ActualWidth);
        quickModel.Click+=(_,_)=>Run(TaskOptionsAsync);
        composer.TextChanged+=(_,_)=>{if(rendering||!work.Connected)return;try{FlushDraft();UpdateSendState();if(composer.Text.EndsWith('@')&&!mentionOpen)Run(MentionAsync);}catch(Exception e){Error(e);}};
        composer.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Enter&&Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)){e.Handled=true;if(send.IsEnabled)Run(async()=>{FlushDraft();following=true;await work.SendAsync();});}};
        composer.AllowDrop=true;composer.DragOver+=(_,e)=>e.AcceptedOperation=e.DataView.Contains(StandardDataFormats.StorageItems)?DataPackageOperation.Copy:DataPackageOperation.None;
        composer.Drop+=(_,e)=>Run(async()=>{var items=await e.DataView.GetStorageItemsAsync();if(items.Count>8)throw new InvalidOperationException("每批最多 8 个文件。");foreach(var item in items){if(item is not StorageFile file)throw new InvalidOperationException("本地文件夹请先压缩；服务器目录可直接 @ 引用。");await work.UploadAsync(file.Path,true);}});
        transcript.ViewChanged+=(_,_)=>{if(rendering)return;following=transcript.ScrollableHeight-transcript.VerticalOffset<64;latest.Visibility=following?Visibility.Collapsed:Visibility.Visible;};
        model.SelectionChanged+=(_,_)=>{if(!rendering)Efforts(work.Settings.Effort);};
        var historyMenu=new MenuFlyout();var rename=new MenuFlyoutItem{Text="重命名"};rename.Click+=(_,_)=>Run(RenameAsync);var delete=new MenuFlyoutItem{Text="删除会话历史"};delete.Click+=(_,_)=>Run(DeleteAsync);historyMenu.Items.Add(rename);historyMenu.Items.Add(delete);history.ContextFlyout=historyMenu;
        historySearch.TextChanged+=(_,_)=>History();history.SelectionChanged+=(_,_)=>{if(!rendering&&history.SelectedItem is Session s&&s.Id!=work.Selected?.Id)Run(async()=>{await work.SelectAsync(s.Id);ChatPage();});};
        history.ItemClick+=(_,e)=>{if(!busy&&e.ClickedItem is Session selected&&selected.Id==work.Selected?.Id)ChatPage();};
        theme.SelectionChanged+=(_,_)=>{if(rendering)return;try{work.SetTheme((theme.SelectedItem as Choice)?.Id??"Default");ApplyTheme();}catch(Exception e){Error(e);}};
        presentation.SelectionChanged+=(_,_)=>{if(rendering)return;try{work.SetPresentation(presentation.SelectedItem as string??"原版");signature="";Render();}catch(Exception e){Error(e);}};
        work.Changed+=()=>DispatcherQueue.TryEnqueue(Render);
        root.Loaded+=(_,_)=>{SyncTitleBar();if(uiCheck){_=CaptureThemesAsync();return;}if(work.SavedConnection is {} saved)Run(()=>work.ConnectAsync(saved));};
        Closed+=(_,_)=>{builds.Cancel();work.Dispose();};Render();StartupLog.Mark("window.constructor.ready");
    }
    internal sealed record Choice(string Id,string Name){public override string ToString()=>Name;}
    private UIElement MakeLogin() {
        var panel=Ui.Stack(Ui.Logo(62),Ui.Text("继续你的工作。",32),Ui.Caption("连接现有 AGY 服务器，与你的 iPhone 共用会话和文件。"),address,token);
        if(work.SavedConnection is {} credentials){address.Text=credentials.Address;token.Password=credentials.Token;}
        var connect=Ui.Button("连接 AGY",()=>Run(()=>work.ConnectAsync(Credentials.Validate(address.Text,token.Password))),true);connect.HorizontalAlignment=HorizontalAlignment.Stretch;
        panel.Children.Add(connect);panel.Children.Add(Ui.Button("网络设置 · DoH",()=>Run(NetworkSettingsAsync)));panel.Children.Add(progress);panel.Children.Add(Ui.Caption("连接密钥保存在此 Windows 用户的加密存储中。Google 账号由服务器管理。"));
        panel.MaxWidth=420;panel.HorizontalAlignment=HorizontalAlignment.Center;panel.VerticalAlignment=VerticalAlignment.Center;
        var scroll=new ScrollViewer{Content=panel,Padding=new Thickness(24,30,24,30),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return scroll;
    }
    private UIElement MakeChat() {
        var page=new Grid{RowSpacing=16,MaxWidth=1060,HorizontalAlignment=HorizontalAlignment.Stretch};
        page.RowDefinitions.Add(new(){Height=GridLength.Auto});page.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});page.RowDefinitions.Add(new(){Height=GridLength.Auto});
        heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;var top=Ui.Stack(heading,taskSummary);top.Spacing=4;top.Margin=new Thickness(2,8,0,0);page.Children.Add(top);
        transcript.Content=messages;transcript.Padding=new Thickness(0,12,8,20);conversationBody.Children.Add(transcript);
        var greeting=Ui.Stack(Ui.Logo(42),Ui.Title("今天，一起完成什么？",28),Ui.Caption("从当前项目开始，或上传你要处理的资料。"));greeting.Spacing=16;
        var suggestions=Ui.Stack(Ui.Action("梳理项目结构",Symbol.Document,()=>{work.UpdateDraft("请阅读当前项目，说明它的结构和主要入口。");composer.Text=work.Draft.Text;composer.Focus(FocusState.Programmatic);}),Ui.Action("上传参考资料",Symbol.Attach,()=>Run(UploadAsync)),Ui.Action("浏览项目文件",Symbol.Folder,()=>shell.Navigate("files")));
        suggestions.Spacing=8;foreach(var child in suggestions.Children.Cast<Button>()){child.HorizontalAlignment=HorizontalAlignment.Stretch;child.HorizontalContentAlignment=HorizontalAlignment.Left;}
        greeting.Children.Add(suggestions);greeting.MaxWidth=440;greeting.HorizontalAlignment=HorizontalAlignment.Center;greeting.VerticalAlignment=VerticalAlignment.Center;greeting.Margin=new Thickness(12,24,12,24);var emptyFrame=new Grid();emptyFrame.Children.Add(greeting);conversationBody.SizeChanged+=(_,_)=>emptyFrame.MinHeight=conversationBody.ActualHeight;welcome=new ScrollViewer{Content=emptyFrame,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};conversationBody.Children.Add(welcome);Grid.SetRow(conversationBody,1);page.Children.Add(conversationBody);
        approvalScroll.Content=approvals;referenceScroll.Content=references;
        var input=Ui.Stack(approvalScroll,pending,referenceScroll,composer);input.Spacing=8;
        var toolbar=new Grid{ColumnSpacing=8};toolbar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        quickModel.Content=quickModelLabel;conversationBody.SizeChanged+=(_,_)=>quickModel.MaxWidth=Math.Max(80,Math.Min(260,conversationBody.ActualWidth-250));quickModel.FontSize=12;quickModel.Padding=new Thickness(10,6,10,6);quickModel.MaxWidth=260;
        var actions=Ui.Inline(Ui.Icon(Symbol.Attach,"上传文件",()=>Run(UploadAsync)),Ui.Button("@",()=>Run(MentionAsync)),quickModel);toolbar.Children.Add(actions);
        var submit=Ui.Inline(stop,send);Grid.SetColumn(submit,1);toolbar.Children.Add(submit);input.Children.Add(toolbar);
        var box=Ui.Surface(input,"CardBackgroundFillColorDefaultBrush",new Thickness(16,12,16,12),16);
        var bottom=Ui.Stack(latest,box,Ui.Caption("Ctrl+Enter 发送   ·   Enter 换行   ·   支持拖入文件"));bottom.Spacing=8;bottom.MaxWidth=920;bottom.HorizontalAlignment=HorizontalAlignment.Stretch;Grid.SetRow(bottom,2);page.Children.Add(bottom);
        return page;
    }
    private UIElement MakeSettings() {
        permission.Items.Add(new Choice("review","Restriction · 逐项审批"));permission.Items.Add(new Choice("turbo","Turbo · 自动执行"));mode.Items.Add(new Choice("plan","先做计划"));mode.Items.Add(new Choice("accept-edits","直接实施"));theme.Items.Add(new Choice("Default","跟随 Windows"));theme.Items.Add(new Choice("Light","浅色"));theme.Items.Add(new Choice("Dark","深色"));presentation.ItemsSource=new[]{"原版","步骤摘要","结论优先"};
        var tasks=Ui.Stack(Ui.Title("模型与任务",17),model,effort,permission,mode,save,Ui.Button("刷新可用模型",()=>Run(work.RefreshModelsAsync)));tasks.Spacing=16;
        var appearance=Ui.Stack(Ui.Title("外观与阅读",17),theme,presentation);appearance.Spacing=16;
        var account=Ui.Stack(Ui.Title("连接与用量",17),Ui.Action("查看账号额度",Symbol.Contact,()=>Run(LimitsAsync)),Ui.Action("网络设置 · DoH",Symbol.World,()=>Run(NetworkSettingsAsync)),Ui.Action("切换服务器或连接密钥",Symbol.Switch,()=>{work.Disconnect();Render();}));account.Spacing=12;
        var project=Ui.Stack(Ui.Title("当前项目",17),Ui.Caption("选择工作文件夹；聊天和文件浏览会使用同一个项目。"),Ui.Action("选择项目",Symbol.Folder,()=>Run(ChooseWorkspaceAsync)));
        var panel=Ui.Stack(Ui.Title("配置与额度",24),Ui.Caption("任务配置保存后会用于新对话，旧对话保留各自的设置。"),Ui.Card(tasks),Ui.Card(project),Ui.Card(appearance),Ui.Card(account));panel.Spacing=20;panel.MaxWidth=760;panel.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Margin=new Thickness(0,12,0,16);
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private async Task TaskOptionsAsync() {
        var current=work.Settings;
        var models=new ComboBox{Header="模型",ItemsSource=work.Families,DisplayMemberPath="Name",HorizontalAlignment=HorizontalAlignment.Stretch};
        var thoughts=new ComboBox{Header="思考强度",HorizontalAlignment=HorizontalAlignment.Stretch};
        var policy=new ComboBox{Header="审批策略",ItemsSource=new[]{new Choice("review","Restriction · 逐项审批"),new Choice("turbo","Turbo · 自动执行")},HorizontalAlignment=HorizontalAlignment.Stretch};
        var execution=new ComboBox{Header="执行方式",ItemsSource=new[]{new Choice("plan","先做计划"),new Choice("accept-edits","直接实施")},HorizontalAlignment=HorizontalAlignment.Stretch};
        void UpdateEfforts(){if(models.SelectedItem is not ModelFamily family)return;thoughts.ItemsSource=family.Efforts.Select(e=>new Choice(e,FileRules.EffortLabel(e))).ToArray();thoughts.SelectedItem=thoughts.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==current.Effort)??thoughts.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==family.DefaultEffort);}
        models.SelectionChanged+=(_,_)=>UpdateEfforts();models.SelectedItem=work.Families.FirstOrDefault(f=>f.Id==current.Model);UpdateEfforts();
        policy.SelectedItem=policy.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==current.ApprovalMode);execution.SelectedItem=execution.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==current.Mode);
        if(await DialogAsync("模型与任务",Ui.Stack(models,thoughts,policy,execution),"保存")!=ContentDialogResult.Primary)return;
        if(models.SelectedItem is ModelFamily family&&thoughts.SelectedItem is Choice e&&policy.SelectedItem is Choice p&&execution.SelectedItem is Choice m)await work.ConfigureAsync(new(family.Id,e.Id,p.Id,m.Id,current.Workspace));
    }
    private void Efforts(string preferred){if(model.SelectedItem is not ModelFamily family)return;if(effortFamily!=family.Id){effortFamily=family.Id;effort.Items.Clear();foreach(var e in family.Efforts)effort.Items.Add(new Choice(e,FileRules.EffortLabel(e)));}effort.SelectedItem=effort.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==preferred)??effort.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==family.DefaultEffort);}
    private async Task SaveSettingsAsync(){if(model.SelectedItem is not ModelFamily f||effort.SelectedItem is not Choice e||permission.SelectedItem is not Choice p||mode.SelectedItem is not Choice m)throw new InvalidOperationException("请选择有效的模型、强度和策略。");await work.ConfigureAsync(new(f.Id,e.Id,p.Id,m.Id,work.Settings.Workspace));}
    private void ShowPage(string page){chat.Visibility=page=="chat"?Visibility.Visible:Visibility.Collapsed;files.View.Visibility=page=="files"?Visibility.Visible:Visibility.Collapsed;builds.View.Visibility=page=="build"?Visibility.Visible:Visibility.Collapsed;settings.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;if(page=="files"&&!uiCheck)Run(files.LoadAsync);}
    internal void ChatPage(){shell.Navigate("chat");}
    internal async void Run(Func<Task> action){if(busy)return;busy=true;notice.IsOpen=false;Render();try{await action();}catch(Exception error){Error(error);}finally{busy=false;Render();}}
    internal void Error(Exception error){notice.Title="操作未完成";notice.Message=error is OperationCanceledException?"操作已取消或超时；不会自动重发任务。":error.Message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private void UpdateSendState(){send.IsEnabled=work.Connected&&!busy&&work.Pending is null&&work.Selected?.Running!=true&&(!string.IsNullOrWhiteSpace(composer.Text)||work.Draft.References.Length>0);}
    private void FlushDraft(){if(work.Connected&&!rendering)work.UpdateDraft(composer.Text);}
    private void History(){var key=historySearch.Text+"/"+work.Selected?.Id+"/"+string.Join("|",work.Sessions.Select(s=>s.Id+":"+s.Name+":"+s.Modified));if(key==historySignature)return;historySignature=key;var old=rendering;rendering=true;history.ItemsSource=work.Sessions.Where(s=>s.Name.Contains(historySearch.Text,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(s=>s.Modified).ToArray();history.SelectedItem=history.Items.Cast<Session>().FirstOrDefault(s=>s.Id==work.Selected?.Id);rendering=old;}
    private void Render(){if(rendering)return;rendering=true;try{
        login.Visibility=work.Connected?Visibility.Collapsed:Visibility.Visible;shell.View.Visibility=work.Connected?Visibility.Visible:Visibility.Collapsed;progress.IsActive=busy;connection.Text=uiCheck&&work.Connected?"离线预览 · 示例数据":busy?"正在处理…":work.ConnectionLabel;
        if(!notice.IsOpen&&!string.IsNullOrWhiteSpace(work.Selected?.Error??work.Notice)){notice.Title="服务器状态";notice.Message=work.Selected?.Error??work.Notice;notice.Severity=InfoBarSeverity.Warning;notice.IsOpen=true;}
        heading.Text=work.Selected?.Name??"新对话";ToolTipService.SetToolTip(heading,heading.Text);
        shell.SetProject(work.Connected?Ui.Tail(work.Settings.Workspace):"工作区");
        var familyName=work.Families.FirstOrDefault(f=>f.Id==work.Settings.Model)?.Name??"选择模型";
        quickModelLabel.Text=familyName+" · "+FileRules.EffortLabel(work.Settings.Effort)+" ⌄";
        ToolTipService.SetToolTip(quickModel,"更改模型、思考强度、审批策略和执行方式");
        quickModel.IsEnabled=!busy&&work.Selected?.Running!=true&&work.Pending is null;
        taskSummary.Text=(work.Settings.ApprovalMode=="turbo"?"Turbo 自动执行":"Restriction 逐项审批")+"  ·  "+(work.Settings.Mode=="plan"?"先做计划":"直接实施");send.IsEnabled=!busy&&work.Pending is null&&work.Selected?.Running!=true;stop.IsEnabled=!busy&&work.Selected?.Running==true;stop.Visibility=work.Selected?.Running==true?Visibility.Visible:Visibility.Collapsed;composer.IsReadOnly=busy||work.Pending is not null;
        if(!ReferenceEquals(catalog,work.Families)){catalog=work.Families;model.ItemsSource=catalog;effortFamily="";}model.SelectedItem=work.Families.FirstOrDefault(f=>f.Id==work.Settings.Model);Efforts(work.Settings.Effort);permission.SelectedItem=permission.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Settings.ApprovalMode);mode.SelectedItem=mode.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Settings.Mode);
        save.IsEnabled=!busy&&work.Selected?.Running!=true&&work.Pending is null;model.IsEnabled=effort.IsEnabled=permission.IsEnabled=mode.IsEnabled=save.IsEnabled;
        theme.SelectedItem=theme.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Theme);presentation.SelectedItem=work.Presentation;ApplyTheme();
        if(composer.Text!=work.Draft.Text)composer.Text=work.Draft.Text;
        references.Children.Clear();foreach(var r in work.Draft.References)references.Children.Add(Ui.Button("@ "+r.Label+"  ×",()=>Run(()=>{work.RemoveReference(r.Path);return Task.CompletedTask;})));
        pending.Children.Clear();if(work.Pending is {} waiting){pending.Children.Add(Ui.Caption("发送结果待确认，不会自动重发。"));pending.Children.Add(Ui.Button("核对会话",()=>Run(()=>work.SelectAsync(waiting.SessionID))));pending.Children.Add(Ui.Button("按原编号重试",()=>Run(work.SubmitPendingAsync)));}
        approvals.Children.Clear();foreach(var approval in work.Selected?.Approvals??[]){var details=new Expander{Header="查看实际参数 · 仅批准这一次",Content=new TextBox{Text=approval.Parameters.ToString(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150}};var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};row.Children.Add(Ui.Button("拒绝",()=>Run(()=>work.AnswerAsync(approval,false))));row.Children.Add(Ui.Button("允许这一次",()=>Run(()=>work.AnswerAsync(approval,true)),true));approvals.Children.Add(Ui.Card(Ui.Stack(Ui.Text("等待审批 · "+approval.Tool,16),details,row)));}
        referenceScroll.Visibility=references.Children.Count>0?Visibility.Visible:Visibility.Collapsed;pending.Visibility=pending.Children.Count>0?Visibility.Visible:Visibility.Collapsed;approvalScroll.Visibility=approvals.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
        var key=(work.Selected?.Id??"new")+":"+(work.Selected?.Revision??0)+":"+work.Presentation;if(signature!=key){signature=key;RenderMessages();}History();history.IsEnabled=!busy;UpdateSendState();
    }finally{rendering=false;}}
    private void ApplyTheme(){root.RequestedTheme=work.Theme switch{"Light"=>ElementTheme.Light,"Dark"=>ElementTheme.Dark,_=>ElementTheme.Default};SyncTitleBar();}
    private void SyncTitleBar() {
        try {
            var bar=AppWindow.TitleBar;
            // Keep the native caption, including inactive state and system window buttons.
            if(Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Microsoft.UI.Windowing.AppWindowTitleBar","PreferredTheme"))
                bar.PreferredTheme=root.ActualTheme==ElementTheme.Dark?Microsoft.UI.Windowing.TitleBarTheme.Dark:Microsoft.UI.Windowing.TitleBarTheme.Light;
            if(new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast){bar.ResetToDefault();return;}
            if(!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())return;
            var background=(root.Background as SolidColorBrush)?.Color ?? (root.ActualTheme==ElementTheme.Dark?Microsoft.UI.Colors.Black:Microsoft.UI.Colors.White);
            var foreground=root.ActualTheme==ElementTheme.Dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
            bar.BackgroundColor=bar.InactiveBackgroundColor=bar.ButtonBackgroundColor=bar.ButtonInactiveBackgroundColor=background;
            bar.ForegroundColor=bar.ButtonForegroundColor=foreground;
            bar.InactiveForegroundColor=bar.ButtonInactiveForegroundColor=Windows.UI.Color.FromArgb(255,128,128,128);
            bar.ButtonHoverBackgroundColor=bar.ButtonPressedBackgroundColor=null;
            bar.ButtonHoverForegroundColor=bar.ButtonPressedForegroundColor=null;
        }catch(Exception e){StartupLog.Error("window.titlebar.theme",e);}
    }
    private async Task NetworkSettingsAsync() {
        var current=work.Network;
        var enable=new ToggleSwitch{Header="加密 DNS（DoH）",IsOn=current.UseDoh,OnContent="启用",OffContent="使用系统 DNS"};
        var provider=new ComboBox{Header="DoH 服务",ItemsSource=new[]{"Cloudflare","Google"},SelectedItem=current.Provider,HorizontalAlignment=HorizontalAlignment.Stretch};
        var fallback=new CheckBox{Content="DoH 失败时回退到系统 DNS",IsChecked=current.SystemFallback};
        provider.IsEnabled=fallback.IsEnabled=enable.IsOn;
        enable.Toggled+=(_,_)=>provider.IsEnabled=fallback.IsEnabled=enable.IsOn;
        var dialog=new ContentDialog{XamlRoot=root.XamlRoot,Title="网络设置",PrimaryButtonText=work.Connected?"保存并重新连接":"保存",CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary,
            Content=Ui.Stack(enable,provider,fallback,Ui.Caption("使用代理时由代理解析服务器域名。DoH 服务只接收域名查询，不接收连接密钥和聊天内容。设置保存在这台电脑上。"))};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        work.SetNetwork(new(enable.IsOn,provider.SelectedItem as string??"Cloudflare",fallback.IsChecked==true));
        if(work.Connected&&work.SavedConnection is {} saved)await work.ConnectAsync(saved);
    }
    private void RenderMessages(){var id=work.Selected?.Id??"new";if(session!=id){session=id;following=true;}var offset=transcript.VerticalOffset;var tail=following;messages.Children.Clear();
        foreach(var message in work.Selected?.Messages??[]){var panel=Ui.Stack(Ui.Caption(message.Role=="user"?"你":"AGY"));var process=Ui.Stack();foreach(var step in message.Steps??[]){if(step.Text!=message.Text)process.Children.Add(Ui.Prose(step.Text));}foreach(var tool in message.Tools??[])process.Children.Add(new Expander{Header=tool.Name+" · "+tool.State,Content=Ui.Stack(Ui.Text(tool.Parameters.ToString(),12),Ui.Text(tool.Output,12))});var text=MessageText(message.Text);if(work.Presentation=="原版"){if(process.Children.Count>0)panel.Children.Add(process);panel.Children.Add(text);}else{panel.Children.Add(text);if(process.Children.Count>0)panel.Children.Add(new Expander{Header="查看过程与工具",Content=process});}foreach(var a in message.Attachments??[]){if(a.Path is {} path)panel.Children.Add(Ui.Button((a.Mime=="inode/directory"?"@ ":"下载 ")+a.Name,()=>Run(async()=>{if(a.Mime=="inode/directory"){shell.Navigate("files");await files.ShowFolderAsync(path);}else await SaveRemoteAsync(path,a.Name);})));}var copy=Ui.Icon(Symbol.Copy,"复制消息",()=>Copy(message.Text));copy.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(copy);
            if(message.Role=="user"){var bubble=Ui.Surface(panel,"CardBackgroundFillColorDefaultBrush",new Thickness(16),14);bubble.MaxWidth=720;bubble.HorizontalAlignment=HorizontalAlignment.Right;messages.Children.Add(bubble);}else{panel.Margin=new Thickness(4,0,4,0);messages.Children.Add(panel);}}
        welcome.Visibility=messages.Children.Count==0?Visibility.Visible:Visibility.Collapsed;transcript.Visibility=messages.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
        DispatcherQueue.TryEnqueue(()=>{rendering=true;transcript.ChangeView(null,tail?transcript.ScrollableHeight:offset,null,true);rendering=false;});
    }
    private UIElement MessageText(string value){var panel=Ui.Stack();var pieces=value.Split("```");for(var i=0;i<pieces.Length;i++){var text=pieces[i];if(i%2==0){var offset=0;foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text,@"\[([^\]\n]{1,1024})\]\(([^\)\n]{1,8192})\)",System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromSeconds(1))){if(match.Index>offset)panel.Children.Add(Ui.Prose(text[offset..match.Index]));var label=match.Groups[1].Value;var link=match.Groups[2].Value;panel.Children.Add(Ui.Button(label,()=>Run(()=>OpenLinkAsync(link,label))));offset=match.Index+match.Length;}if(offset<text.Length)panel.Children.Add(Ui.Prose(text[offset..]));continue;}var newline=text.IndexOf('\n');var code=newline>=0?text[(newline+1)..]:text;var box=new TextBox{Text=code,IsReadOnly=true,TextWrapping=TextWrapping.NoWrap,FontFamily=new FontFamily("Cascadia Code"),MaxHeight=450};var wrap=new ToggleSwitch{Header="代码换行"};wrap.Toggled+=(_,_)=>box.TextWrapping=wrap.IsOn?TextWrapping.Wrap:TextWrapping.NoWrap;panel.Children.Add(Ui.Card(Ui.Stack(Ui.Caption(newline>=0?text[..newline]:"代码"),box,wrap,Ui.Button("复制代码",()=>Copy(code)))));}return panel;}
    private async Task OpenLinkAsync(string link,string label){
        if(Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)){await Windows.System.Launcher.LaunchUriAsync(uri);return;}
        string? path=null;if(link.StartsWith("file://")&&Uri.TryCreate(link,UriKind.Absolute,out var file)&&string.IsNullOrEmpty(file.Host))path=Uri.UnescapeDataString(file.AbsolutePath);
        else if(link.StartsWith("sandbox:"))path=Uri.UnescapeDataString(link[8..]);else if(link.StartsWith('/'))path=Uri.UnescapeDataString(link);else if(!link.Contains(':'))path=work.Settings.Workspace.TrimEnd('/')+"/"+Uri.UnescapeDataString(link);
        if(path is not null)await SaveRemoteAsync(path,label);
    }
    internal void Copy(string text){var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);}
    internal async Task<ContentDialogResult> DialogAsync(string title,UIElement content,string primary="完成",string? secondary=null){var dialog=new ContentDialog{XamlRoot=root.XamlRoot,Title=title,Content=content,PrimaryButtonText=primary,CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary,RequestedTheme=root.RequestedTheme};if(secondary is not null)dialog.SecondaryButtonText=secondary;return await dialog.ShowAsync();}
    internal async Task UploadAsync(){var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add("*");var picked=await picker.PickMultipleFilesAsync();if(picked.Count>8)throw new InvalidOperationException("每批最多 8 个文件。");foreach(var item in picked)await work.UploadAsync(item.Path,true);}
    internal async Task<string?> PickFolderAsync(){var picker=new FolderPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add("*");return (await picker.PickSingleFolderAsync())?.Path;}
    internal async Task SaveRemoteAsync(string path,string name){var picker=new FileSavePicker{SuggestedFileName=FileRules.SafeName(name)};WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeChoices.Add("文件",new List<string>{Path.GetExtension(name) is {Length:>0} ext?ext:".bin"});var file=await picker.PickSaveFileAsync();if(file is not null)await work.DownloadAsync(path,file.Path);}
    private async Task MentionAsync(){mentionOpen=true;try{var browser=new FileBrowser(this,work,true,false);await browser.LoadAsync();await DialogAsync("引用文件或整个文件夹",browser.View,"关闭");ChatPage();}finally{mentionOpen=false;}}
    private async Task ChooseWorkspaceAsync(){if(work.Selected is not null){if(work.Pending is not null||work.Selected.Running)throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");if(await DialogAsync("切换项目",Ui.Text("当前对话会保留。新建一个对话后，可以选择其他项目。"),"新建对话并选择")!=ContentDialogResult.Primary)return;work.NewConversation();ChatPage();}var browser=new FileBrowser(this,work,false,true);await browser.LoadAsync();await DialogAsync("选择服务器项目",browser.View,"完成");}
    private async Task RenameAsync(){if(history.SelectedItem is not Session selected)return;var input=new TextBox{Text=selected.Name,Header="会话名称"};if(await DialogAsync("重命名会话",input,"保存")==ContentDialogResult.Primary&&!string.IsNullOrWhiteSpace(input.Text))await work.RenameAsync(selected,input.Text);}
    private async Task DeleteAsync(){if(history.SelectedItem is not Session selected)return;if(await DialogAsync("删除会话历史",Ui.Text("只删除网关历史条目，不删除项目文件。"),"删除")==ContentDialogResult.Primary)await work.DeleteAsync(selected);}
    private async Task LimitsAsync(){var reply=await work.LimitsAsync();var panel=Ui.Stack();foreach(var group in reply.Groups){panel.Children.Add(Ui.Text(group.Name,18));foreach(var bucket in group.Buckets)panel.Children.Add(Ui.Text(bucket.Name+"："+(bucket.Remaining is {} fraction?fraction.ToString("P0"):"未提供")+(DateTimeOffset.TryParse(bucket.Reset,out var reset)?" · "+reset.ToLocalTime().ToString("g"):"")));}await DialogAsync("真实账号额度",new ScrollViewer{Content=panel,MaxHeight=460});}
    private async Task CaptureThemesAsync() {
        StartupLog.Mark("ui-check.begin");
        try {
            var folder=Path.Combine(AppContext.BaseDirectory,"ui-check");Directory.CreateDirectory(folder);
            async Task Capture(string name){await Task.Delay(600);root.UpdateLayout();SyncTitleBar();if(Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()&&!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast&&AppWindow.TitleBar.BackgroundColor!=(root.Background as SolidColorBrush)?.Color)throw new InvalidOperationException("标题栏主题未同步。");var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);await SaveImageAsync(bitmap,await bitmap.GetPixelsAsync(),Path.Combine(folder,name+".png"));}
            foreach(var value in new[]{ElementTheme.Light,ElementTheme.Dark}){root.RequestedTheme=value;await Capture("login-"+value.ToString().ToLowerInvariant());}
            await work.ConnectAsync(VisualPreview.Connection);
            foreach(var value in new[]{ElementTheme.Light,ElementTheme.Dark}) {
                work.SetTheme(value.ToString());work.NewConversation();shell.Navigate("chat");Render();await Capture("workspace-"+value.ToString().ToLowerInvariant());
                await work.SelectAsync(VisualPreview.SessionID);Render();await Capture("conversation-"+value.ToString().ToLowerInvariant());
                shell.Navigate("files");await files.LoadAsync();await Capture("files-"+value.ToString().ToLowerInvariant());
                shell.Navigate("build");await Capture("build-"+value.ToString().ToLowerInvariant());
                shell.Navigate("settings");await Capture("settings-"+value.ToString().ToLowerInvariant());
            }
            AppWindow.Resize(new Windows.Graphics.SizeInt32(820,760));work.NewConversation();shell.Navigate("chat");Render();await Capture("workspace-narrow");
            File.WriteAllText(Path.Combine(folder,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{framework="WinUI 3",renderedOnWindows=true,data="isolated offline examples",lightAndDarkCaptured=true,connectedPagesCaptured=new[]{"workspace","conversation","files","build","settings"},narrowWidthCaptured=true,screenshotIncludesNativeTitleBar=false}));
        } catch(Exception e){StartupLog.Error("ui-check.failed",e);Environment.ExitCode=1;File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-check-error.txt"),e.ToString());}
        finally{Application.Current.Exit();}
    }
    private static async Task SaveImageAsync(Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap bitmap,Windows.Storage.Streams.IBuffer pixels,string path){var folder=await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);var file=await folder.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.ReplaceExisting);using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels)){reader.ReadBytes(bytes);}encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();}
}
