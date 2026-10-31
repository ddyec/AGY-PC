using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AGY.PC.WinUI;
public sealed partial class DesktopApp : Application {
    private MainWindow? window;
    private Window? recovery;
    private readonly bool uiCheck = Environment.GetCommandLineArgs().Contains("--ui-check");
    public DesktopApp() {
        UnhandledException += (_, e) => {
            StartupLog.Mark("xaml.unhandled.message", e.Message);
            StartupLog.Error("xaml.unhandled", e.Exception);
            // Preserve fail-fast semantics; do not conceal unknown native errors.
        };
        StartupLog.Mark("xaml.initialize-component");
        InitializeComponent();
        StartupLog.Mark("xaml.resources.ready");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        try {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY-PC", "state.bin");
            IStateStore store = uiCheck ? new ProbeStore() : new ProtectedStateStore(path, Dpapi.Protect, Dpapi.Unprotect);
            var workspace = uiCheck ? new Workspace(store,c=>new GatewayClient(c,new VisualPreview())) : new Workspace(store);
            StartupLog.Mark("workspace.ready", uiCheck ? "isolated visual check; no stored credentials or network" : "current-user state");
            StartupLog.Mark("window.construct");
            window = new MainWindow(workspace, uiCheck);
            StartupLog.Mark("window.activate");
            window.Activate();
            StartupLog.Mark("window.activated");
        } catch (Exception error) {
            StartupLog.Error("application.launch.failed", error);
            if (uiCheck) { Environment.ExitCode = 1; Exit(); return; }
            recovery = new Window { Title = "AGY - PC · 启动未完成", Content = new TextBox { Text = error.Message + "\n详细信息已写入启动日志。", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30) } };
            recovery.Activate();
        }
    }
    private sealed class ProbeStore : IStateStore {
        public AppState Load() => new();
        public void Save(AppState state) { }
    }
}
