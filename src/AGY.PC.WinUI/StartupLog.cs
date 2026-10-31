using System.Runtime.InteropServices;
using System.Text;

namespace AGY.PC.WinUI;
internal static class StartupLog {
    private static readonly object Gate = new();
    private static string? file;
    [ThreadStatic] private static bool writing;
    public static void Initialize() {
        var folder = Environment.GetEnvironmentVariable("AGY_PC_DIAGNOSTICS_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY-PC", "Diagnostics", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId);
        try { Directory.CreateDirectory(folder); file = Path.Combine(folder, "startup.log"); }
        catch { file = null; }
        Mark("managed.entry", RuntimeInformation.OSDescription + " | " + RuntimeInformation.OSArchitecture + " | " + RuntimeInformation.FrameworkDescription);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Error("appdomain.unhandled", e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => {
            if (e.Exception is COMException || e.Exception.GetType().Name == "XamlParseException") Error("winrt.first-chance", e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Error("task.unobserved", e.Exception);
    }
    public static void Mark(string stage, string detail = "") => Write(stage + (detail.Length > 0 ? " | " + detail : ""));
    public static void Error(string stage, Exception? error) => Write(stage + " | HRESULT=0x" + unchecked((uint)(error?.HResult ?? 0)).ToString("X8") + Environment.NewLine + error);
    private static void Write(string text) {
        if (writing || file is null) return;
        writing = true;
        try { lock (Gate) { if (!File.Exists(file) || new FileInfo(file).Length < 8 * 1024 * 1024) File.AppendAllText(file, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine, Encoding.UTF8); } }
        catch { /* Logging must not trigger another startup failure. */ }
        finally { writing = false; }
    }
}
