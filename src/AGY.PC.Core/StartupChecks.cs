namespace AGY.PC.Core;

public static class StartupChecks {
    public static readonly string[] RequiredFiles = [
        "AGY-PC.exe", "AGY-PC.dll", "AGY-PC.runtimeconfig.json", "AGY-PC.deps.json", "AGY.PC.Core.dll",
        "AGY-PC.pri", "App.xbf", "Microsoft.UI.Xaml.dll", "Microsoft.UI.Xaml.Controls.pri",
        "Assets/Mark.png", "Assets/AGY.ico"
    ];
    public static string[] MissingFiles(string folder) => RequiredFiles.Where(relative => {
        var file = new FileInfo(Path.Combine(folder, relative));
        return !file.Exists || file.Length == 0;
    }).ToArray();
    public static string ExitDescription(int code) => $"{code} (0x{unchecked((uint)code):X8})" +
        (unchecked((uint)code) == 0xC000027B ? " · WinUI 启动异常，需查看原始异常日志" : "");
}
