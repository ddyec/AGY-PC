using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace AGY.PC.WinUI;
internal static class Program {
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_=> {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            var app = new DesktopApp();
        });
    }
}
internal sealed class DesktopApp : Application {
    private MainWindow? window;
    public DesktopApp() { Resources.MergedDictionaries.Add(new XamlControlsResources()); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","state.bin");
        try {
            var workspace=new Workspace(new ProtectedStateStore(path,Dpapi.Protect,Dpapi.Unprotect));
            window=new MainWindow(workspace,Environment.GetCommandLineArgs().Contains("--ui-check"));window.Activate();
        } catch(Exception error) {
            var text=new TextBox{Text=error.Message,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(30)};
            var recovery=new Window{Title="AGY - PC · 状态读取失败",Content=text};recovery.Activate();
        }
    }
}
