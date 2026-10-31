using AGY.PC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AGY.PC.WinUI;
internal static class Program {
    private static DesktopApp? app;
    [STAThread] public static void Main() {
        StartupLog.Initialize();
        try {
            StartupLog.Mark("com-wrappers.initialize");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            StartupLog.Mark("application.start");
            Application.Start(_ => {
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                StartupLog.Mark("application.construct");
                app = new DesktopApp();
            });
            GC.KeepAlive(app);
        } catch (Exception error) {
            StartupLog.Error("program.fatal", error);
            Environment.ExitCode = 1;
        }
    }
}
