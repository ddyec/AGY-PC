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
    private readonly NavigationView nav=new(){PaneTitle="AGY - PC",IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsSettingsVisible=false,OpenPaneLength=220};
    private readonly InfoBar notice=new(){IsClosable=true};
    private readonly TextBox composer=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=92,MaxHeight=220,PlaceholderText="输入消息，或 @ 引用文件和整个文件夹"};
    private readonly StackPanel messages=new(){Spacing=12},references=new(){Spacing=4},approvals=new(){Spacing=8},pending=new(){Spacing=8};
    private readonly ScrollViewer transcript=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock heading=Ui.Text("新对话",28),connection=Ui.Caption("尚未连接");
    private readonly ComboBox model=new(){Header="模型",HorizontalAlignment=HorizontalAlignment.Stretch},effort=new(){Header="思考强度",HorizontalAlignment=HorizontalAlignment.Stretch},permission=new(){Header="审批策略",HorizontalAlignment=HorizontalAlignment.Stretch},mode=new(){Header="执行方式",HorizontalAlignment=HorizontalAlignment.Stretch},theme=new(){Header="界面主题",HorizontalAlignment=HorizontalAlignment.Stretch},presentation=new(){Header="输出展示",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox address=new(){Header="服务器地址",Text="https://agy.yectech.uk",PlaceholderText="https://你的服务器"};
    private readonly PasswordBox token=new(){Header="连接密钥",PasswordRevealMode=PasswordRevealMode.Peek};
    private readonly ProgressRing progress=new(){IsActive=false,Width=24,Height=24};
    private readonly ListView history=new(){SelectionMode=ListViewSelectionMode.Single,DisplayMemberPath="Name",MaxHeight=440};
    private readonly TextBox historySearch=new(){PlaceholderText="搜索会话"};
    private readonly Button send,stop,save,latest;
    private readonly FileBrowser files;
    private readonly BuildPane builds;
    private readonly bool uiCheck;
    private bool busy,rendering,following=true,mentionOpen;
    private string signature="",session="";
    private readonly UIElement login,chat,settings;
    public MainWindow(Workspace work,bool uiCheck) {
        this.work=work;this.uiCheck=uiCheck;
        Title="AGY - PC";AppWindow.Resize(new Windows.Graphics.SizeInt32(1280,860));AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AGY.ico"));
        if(Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())SystemBackdrop=new MicaBackdrop();
        Content=root;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,Margin=new Thickness(20,14,20,10)};brand.Children.Add(Ui.Logo(26));brand.Children.Add(Ui.Text("AGY - PC",18));brand.Children.Add(connection);root.Children.Add(brand);
        Grid.SetRow(notice,1);notice.Margin=new Thickness(20,0,20,8);root.Children.Add(notice);
        Grid.SetRow(host,2);root.Children.Add(host);
        login=MakeLogin();host.Children.Add(login);
        nav.MenuItems.Add(new NavigationViewItem{Content="对话",Icon=new SymbolIcon(Symbol.Message),Tag="chat"});nav.MenuItems.Add(new NavigationViewItem{Content="文件与产物",Icon=new SymbolIcon(Symbol.Folder),Tag="files"});nav.MenuItems.Add(new NavigationViewItem{Content="混合构建",Icon=new SymbolIcon(Symbol.Repair),Tag="build"});nav.MenuItems.Add(new NavigationViewItem{Content="配置与额度",Icon=new SymbolIcon(Symbol.Setting),Tag="settings"});
        nav.PaneCustomContent=Ui.Stack(Ui.Caption("最近会话"),historySearch,history);
        var surface=new Grid{Padding=new Thickness(22,8,22,18)};surface.RowDefinitions.Add(new(){Height=GridLength.Auto});surface.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var pages=new Grid();Grid.SetRow(pages,1);surface.Children.Add(pages);nav.Content=surface;host.Children.Add(nav);
        chat=MakeChat();settings=MakeSettings();files=new FileBrowser(this,work,false,false);builds=new BuildPane(this,work);
        pages.Children.Add(chat);pages.Children.Add(files.View);pages.Children.Add(builds.View);pages.Children.Add(settings);
        send=Ui.Button("发送",()=>Run(async()=>{FlushDraft();following=true;await work.SendAsync();}),true);stop=Ui.Button("停止",()=>Run(work.StopAsync));save=Ui.Button("保存配置",()=>Run(SaveSettingsAsync),true);latest=Ui.Button("↓ 回到最新",()=>{following=true;transcript.ChangeView(null,transcript.ScrollableHeight,null);});
        ((StackPanel)((Grid)chat).Children[2]).Children.Add(latest);
        var compose=(StackPanel)((Grid)chat).Children[2];
        compose.Children.Add(new ScrollViewer{Content=approvals,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});compose.Children.Add(pending);compose.Children.Add(references);compose.Children.Add(composer);
        var actions=new Grid{ColumnSpacing=12};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var left=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};left.Children.Add(Ui.Button("上传",()=>Run(UploadAsync)));left.Children.Add(Ui.Button("@ 文件 / 文件夹",()=>Run(MentionAsync)));var right=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};right.Children.Add(stop);right.Children.Add(send);Grid.SetColumn(right,1);actions.Children.Add(left);actions.Children.Add(right);compose.Children.Add(actions);compose.Children.Add(Ui.Caption("Ctrl+Enter 发送 · Enter 换行"));
        ((StackPanel)((ScrollViewer)settings).Content).Children.Insert(7,save);
        nav.SelectionChanged+=(_,_)=>ShowPage((nav.SelectedItem as NavigationViewItem)?.Tag as string??"chat");nav.SelectedItem=nav.MenuItems[0];
        composer.TextChanged+=(_,_)=>{if(rendering||!work.Connected)return;try{FlushDraft();if(composer.Text.EndsWith('@')&&!mentionOpen)Run(MentionAsync);}catch(Exception e){Error(e);}};
        composer.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Enter&&Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)){e.Handled=true;if(send.IsEnabled)Run(async()=>{FlushDraft();following=true;await work.SendAsync();});}};
        composer.AllowDrop=true;composer.DragOver+=(_,e)=>e.AcceptedOperation=e.DataView.Contains(StandardDataFormats.StorageItems)?DataPackageOperation.Copy:DataPackageOperation.None;
        composer.Drop+=(_,e)=>Run(async()=>{var items=await e.DataView.GetStorageItemsAsync();if(items.Count>8)throw new InvalidOperationException("每批最多 8 个文件。");foreach(var item in items){if(item is not StorageFile file)throw new InvalidOperationException("本地文件夹请先压缩；服务器目录可直接 @ 引用。");await work.UploadAsync(file.Path,true);}});
        transcript.ViewChanged+=(_,_)=>{if(rendering)return;following=transcript.ScrollableHeight-transcript.VerticalOffset<64;latest.Visibility=following?Visibility.Collapsed:Visibility.Visible;};
        model.SelectionChanged+=(_,_)=>{if(!rendering)Efforts(work.Settings.Effort);};
        var historyMenu=new MenuFlyout();var rename=new MenuFlyoutItem{Text="重命名"};rename.Click+=(_,_)=>Run(RenameAsync);var delete=new MenuFlyoutItem{Text="删除会话历史"};delete.Click+=(_,_)=>Run(DeleteAsync);historyMenu.Items.Add(rename);historyMenu.Items.Add(delete);history.ContextFlyout=historyMenu;
        historySearch.TextChanged+=(_,_)=>History();history.SelectionChanged+=(_,_)=>{if(!rendering&&history.SelectedItem is Session s&&s.Id!=work.Selected?.Id)Run(()=>work.SelectAsync(s.Id));};
        theme.SelectionChanged+=(_,_)=>{if(rendering)return;try{work.SetTheme((theme.SelectedItem as Choice)?.Id??"Default");ApplyTheme();}catch(Exception e){Error(e);}};
        presentation.SelectionChanged+=(_,_)=>{if(rendering)return;try{work.SetPresentation(presentation.SelectedItem as string??"原版");signature="";Render();}catch(Exception e){Error(e);}};
        work.Changed+=()=>DispatcherQueue.TryEnqueue(Render);
        root.Loaded+=(_,_)=>{if(uiCheck){_=CaptureThemesAsync();return;}if(work.SavedConnection is {} saved)Run(()=>work.ConnectAsync(saved));};
        Closed+=(_,_)=>{builds.Cancel();work.Dispose();};Render();
    }
    internal sealed record Choice(string Id,string Name){public override string ToString()=>Name;}
    private UIElement MakeLogin() {
        var panel=Ui.Stack(Ui.Logo(62),Ui.Text("继续你的工作。",32),Ui.Caption("连接现有 AGY 服务器，与你的 iPhone 共用会话和文件。"),address,token);
        if(work.SavedConnection is {} credentials){address.Text=credentials.Address;token.Password=credentials.Token;}
        var connect=Ui.Button("连接 AGY",()=>Run(()=>work.ConnectAsync(Credentials.Validate(address.Text,token.Password))),true);connect.HorizontalAlignment=HorizontalAlignment.Stretch;
        panel.Children.Add(connect);panel.Children.Add(progress);panel.Children.Add(Ui.Caption("连接密钥保存在此 Windows 用户的加密存储中。Google 账号由服务器管理。"));
        panel.MaxWidth=420;panel.HorizontalAlignment=HorizontalAlignment.Center;panel.VerticalAlignment=VerticalAlignment.Center;
        var scroll=new ScrollViewer{Content=panel,Padding=new Thickness(24,30,24,30),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return scroll;
    }
    private UIElement MakeChat(){var page=new Grid{RowSpacing=12};page.RowDefinitions.Add(new(){Height=GridLength.Auto});page.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});page.RowDefinitions.Add(new(){Height=GridLength.Auto});var top=new Grid();top.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});top.Children.Add(heading);var button=Ui.Button("新建对话",()=>Run(()=>{work.NewConversation();return Task.CompletedTask;}));Grid.SetColumn(button,1);top.Children.Add(button);page.Children.Add(top);transcript.Content=messages;Grid.SetRow(transcript,1);page.Children.Add(transcript);var compose=new StackPanel{Spacing=8};Grid.SetRow(compose,2);page.Children.Add(compose);return page;}
    private UIElement MakeSettings(){permission.Items.Add(new Choice("review","Restriction · 逐项审批"));permission.Items.Add(new Choice("turbo","Turbo · 自动执行"));mode.Items.Add(new Choice("plan","先做计划"));mode.Items.Add(new Choice("accept-edits","直接实施"));theme.Items.Add(new Choice("Default","跟随 Windows"));theme.Items.Add(new Choice("Light","浅色"));theme.Items.Add(new Choice("Dark","深色"));presentation.ItemsSource=new[]{"原版","步骤摘要","结论优先"};
        var panel=Ui.Stack(Ui.Text("配置与额度",28),Ui.Caption("新对话沿用主动保存的设置；打开旧对话不会改写默认值。"),model,effort,permission,Ui.Caption("Restriction 每次工具操作都等待单次允许或拒绝。Turbo 无逐项确认，仍遵守 AGY 明确禁止的规则。"),mode,Ui.Button("刷新模型",()=>Run(work.RefreshModelsAsync)),Ui.Button("选择项目文件夹",()=>Run(ChooseWorkspaceAsync)),theme,presentation,Ui.Button("刷新真实额度",()=>Run(LimitsAsync)),Ui.Button("重新连接",()=>{work.Disconnect();Render();}),Ui.Caption("AGY - PC 2.0 · WinUI 3 / Windows App SDK"));
        panel.MaxWidth=740;panel.HorizontalAlignment=HorizontalAlignment.Stretch;return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8)};}
    private void Efforts(string preferred){effort.Items.Clear();if(model.SelectedItem is not ModelFamily family)return;foreach(var e in family.Efforts)effort.Items.Add(new Choice(e,FileRules.EffortLabel(e)));effort.SelectedItem=effort.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==preferred)??effort.Items.Cast<Choice>().FirstOrDefault(e=>e.Id==family.DefaultEffort);}
    private async Task SaveSettingsAsync(){if(model.SelectedItem is not ModelFamily f||effort.SelectedItem is not Choice e||permission.SelectedItem is not Choice p||mode.SelectedItem is not Choice m)throw new InvalidOperationException("请选择有效的模型、强度和策略。");await work.ConfigureAsync(new(f.Id,e.Id,p.Id,m.Id,work.Settings.Workspace));}
    private void ShowPage(string page){chat.Visibility=page=="chat"?Visibility.Visible:Visibility.Collapsed;files.View.Visibility=page=="files"?Visibility.Visible:Visibility.Collapsed;builds.View.Visibility=page=="build"?Visibility.Visible:Visibility.Collapsed;settings.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;if(page=="files")Run(files.LoadAsync);}
    internal void ChatPage(){nav.SelectedItem=nav.MenuItems[0];}
    internal async void Run(Func<Task> action){if(busy)return;busy=true;notice.IsOpen=false;Render();try{await action();}catch(Exception error){Error(error);}finally{busy=false;Render();}}
    internal void Error(Exception error){notice.Title="操作未完成";notice.Message=error is OperationCanceledException?"操作已取消或超时；不会自动重发任务。":error.Message;notice.Severity=InfoBarSeverity.Error;notice.IsOpen=true;}
    private void FlushDraft(){if(work.Connected&&!rendering)work.UpdateDraft(composer.Text);}
    private void History(){var old=rendering;rendering=true;history.ItemsSource=work.Sessions.Where(s=>s.Name.Contains(historySearch.Text,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(s=>s.Modified).ToArray();history.SelectedItem=history.Items.Cast<Session>().FirstOrDefault(s=>s.Id==work.Selected?.Id);rendering=old;}
    private void Render(){if(rendering)return;rendering=true;try{
        login.Visibility=work.Connected?Visibility.Collapsed:Visibility.Visible;nav.Visibility=work.Connected?Visibility.Visible:Visibility.Collapsed;progress.IsActive=busy;connection.Text=busy?"正在处理…":work.ConnectionLabel;
        if(!notice.IsOpen&&!string.IsNullOrWhiteSpace(work.Selected?.Error??work.Notice)){notice.Title="服务器状态";notice.Message=work.Selected?.Error??work.Notice;notice.Severity=InfoBarSeverity.Warning;notice.IsOpen=true;}
        heading.Text=work.Selected?.Name??"新对话";send.IsEnabled=!busy&&work.Pending is null&&work.Selected?.Running!=true;stop.IsEnabled=!busy&&work.Selected?.Running==true;composer.IsReadOnly=busy||work.Pending is not null;
        model.ItemsSource=work.Families;model.SelectedItem=work.Families.FirstOrDefault(f=>f.Id==work.Settings.Model);Efforts(work.Settings.Effort);permission.SelectedItem=permission.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Settings.ApprovalMode);mode.SelectedItem=mode.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Settings.Mode);
        save.IsEnabled=!busy&&work.Selected?.Running!=true&&work.Pending is null;model.IsEnabled=effort.IsEnabled=permission.IsEnabled=mode.IsEnabled=save.IsEnabled;
        theme.SelectedItem=theme.Items.Cast<Choice>().FirstOrDefault(x=>x.Id==work.Theme);presentation.SelectedItem=work.Presentation;ApplyTheme();
        if(composer.Text!=work.Draft.Text)composer.Text=work.Draft.Text;
        references.Children.Clear();foreach(var r in work.Draft.References)references.Children.Add(Ui.Button("@ "+r.Label+"  ×",()=>Run(()=>{work.RemoveReference(r.Path);return Task.CompletedTask;})));
        pending.Children.Clear();if(work.Pending is {} waiting){pending.Children.Add(Ui.Caption("发送结果待确认，不会自动重发。"));pending.Children.Add(Ui.Button("核对会话",()=>Run(()=>work.SelectAsync(waiting.SessionID))));pending.Children.Add(Ui.Button("按原编号重试",()=>Run(work.SubmitPendingAsync)));}
        approvals.Children.Clear();foreach(var approval in work.Selected?.Approvals??[]){var details=new Expander{Header="查看实际参数 · 仅批准这一次",Content=new TextBox{Text=approval.Parameters.ToString(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=150}};var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};row.Children.Add(Ui.Button("拒绝",()=>Run(()=>work.AnswerAsync(approval,false))));row.Children.Add(Ui.Button("允许这一次",()=>Run(()=>work.AnswerAsync(approval,true)),true));approvals.Children.Add(Ui.Card(Ui.Stack(Ui.Text("等待审批 · "+approval.Tool,16),details,row)));}
        var key=(work.Selected?.Id??"new")+":"+(work.Selected?.Revision??0)+":"+work.Presentation;if(signature!=key){signature=key;RenderMessages();}History();
    }finally{rendering=false;}}
    private void ApplyTheme(){root.RequestedTheme=work.Theme switch{"Light"=>ElementTheme.Light,"Dark"=>ElementTheme.Dark,_=>ElementTheme.Default};}
    private void RenderMessages(){var id=work.Selected?.Id??"new";if(session!=id){session=id;following=true;}var offset=transcript.VerticalOffset;var tail=following;messages.Children.Clear();
        foreach(var message in work.Selected?.Messages??[]){var panel=Ui.Stack(Ui.Caption(message.Role=="user"?"你":"AGY"));var process=Ui.Stack();foreach(var step in message.Steps??[]){if(step.Text!=message.Text)process.Children.Add(Ui.Text(step.Text));}foreach(var tool in message.Tools??[])process.Children.Add(new Expander{Header=tool.Name+" · "+tool.State,Content=Ui.Stack(Ui.Text(tool.Parameters.ToString(),12),Ui.Text(tool.Output,12))});var text=MessageText(message.Text);if(work.Presentation=="原版"){panel.Children.Add(process);panel.Children.Add(text);}else{panel.Children.Add(text);if(process.Children.Count>0)panel.Children.Add(new Expander{Header="查看过程与工具",Content=process});}foreach(var a in message.Attachments??[]){if(a.Path is {} path)panel.Children.Add(Ui.Button((a.Mime=="inode/directory"?"@ ":"下载 ")+a.Name,()=>Run(async()=>{if(a.Mime=="inode/directory"){nav.SelectedItem=nav.MenuItems[1];await files.ShowFolderAsync(path);}else await SaveRemoteAsync(path,a.Name);})));}panel.Children.Add(Ui.Button("复制消息",()=>Copy(message.Text)));messages.Children.Add(Ui.Card(panel));}
        if(messages.Children.Count==0)messages.Children.Add(Ui.Card(Ui.Stack(Ui.Logo(52),Ui.Text("从一个项目开始。",28),Ui.Caption("上传资料，@ 一个文件夹，或在混合构建面板选择执行位置。"))));
        DispatcherQueue.TryEnqueue(()=>{rendering=true;transcript.ChangeView(null,tail?transcript.ScrollableHeight:offset,null,true);rendering=false;});
    }
    private UIElement MessageText(string value){var panel=Ui.Stack();var pieces=value.Split("```");for(var i=0;i<pieces.Length;i++){var text=pieces[i];if(i%2==0){var offset=0;foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text,@"\[([^\]\n]{1,1024})\]\(([^\)\n]{1,8192})\)",System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromSeconds(1))){if(match.Index>offset)panel.Children.Add(Ui.Text(text[offset..match.Index]));var label=match.Groups[1].Value;var link=match.Groups[2].Value;panel.Children.Add(Ui.Button(label,()=>Run(()=>OpenLinkAsync(link,label))));offset=match.Index+match.Length;}if(offset<text.Length)panel.Children.Add(Ui.Text(text[offset..]));continue;}var newline=text.IndexOf('\n');var code=newline>=0?text[(newline+1)..]:text;var box=new TextBox{Text=code,IsReadOnly=true,TextWrapping=TextWrapping.NoWrap,FontFamily=new FontFamily("Cascadia Code"),MaxHeight=450};var wrap=new ToggleSwitch{Header="代码换行"};wrap.Toggled+=(_,_)=>box.TextWrapping=wrap.IsOn?TextWrapping.Wrap:TextWrapping.NoWrap;panel.Children.Add(Ui.Card(Ui.Stack(Ui.Caption(newline>=0?text[..newline]:"代码"),box,wrap,Ui.Button("复制代码",()=>Copy(code)))));}return panel;}
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
    private async Task ChooseWorkspaceAsync(){if(work.Selected is not null)throw new InvalidOperationException("请先新建对话，再选择项目目录。");var browser=new FileBrowser(this,work,false,true);await browser.LoadAsync();await DialogAsync("选择服务器项目",browser.View,"完成");}
    private async Task RenameAsync(){if(history.SelectedItem is not Session selected)return;var input=new TextBox{Text=selected.Name,Header="会话名称"};if(await DialogAsync("重命名会话",input,"保存")==ContentDialogResult.Primary&&!string.IsNullOrWhiteSpace(input.Text))await work.RenameAsync(selected,input.Text);}
    private async Task DeleteAsync(){if(history.SelectedItem is not Session selected)return;if(await DialogAsync("删除会话历史",Ui.Text("只删除网关历史条目，不删除项目文件。"),"删除")==ContentDialogResult.Primary)await work.DeleteAsync(selected);}
    private async Task LimitsAsync(){var reply=await work.LimitsAsync();var panel=Ui.Stack();foreach(var group in reply.Groups){panel.Children.Add(Ui.Text(group.Name,18));foreach(var bucket in group.Buckets)panel.Children.Add(Ui.Text(bucket.Name+"："+(bucket.Remaining is {} fraction?fraction.ToString("P0"):"未提供")+(DateTimeOffset.TryParse(bucket.Reset,out var reset)?" · "+reset.ToLocalTime().ToString("g"):"")));}await DialogAsync("真实账号额度",new ScrollViewer{Content=panel,MaxHeight=460});}
    private async Task CaptureThemesAsync(){try{var folder=Path.Combine(AppContext.BaseDirectory,"ui-check");Directory.CreateDirectory(folder);foreach(var value in new[]{ElementTheme.Light,ElementTheme.Dark}){root.RequestedTheme=value;await Task.Delay(1500);var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);var pixels=await bitmap.GetPixelsAsync();await SaveImageAsync(bitmap,pixels,Path.Combine(folder,"login-"+value.ToString().ToLowerInvariant()+".png"));}File.WriteAllText(Path.Combine(folder,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{framework="WinUI 3",renderedOnWindows=true,width=root.ActualWidth,height=root.ActualHeight,lightAndDarkCaptured=true}));}catch(Exception e){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-check-error.txt"),e.ToString());}finally{Application.Current.Exit();}}
    private static async Task SaveImageAsync(Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap bitmap,Windows.Storage.Streams.IBuffer pixels,string path){var folder=await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);var file=await folder.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.ReplaceExisting);using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels)){reader.ReadBytes(bytes);}encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();}
}
