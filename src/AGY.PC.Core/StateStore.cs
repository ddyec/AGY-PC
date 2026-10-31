using System.Text.Json;

namespace AGY.PC.Core;
public interface IStateStore { AppState Load(); void Save(AppState state); }
public sealed class ProtectedStateStore(string path, Func<byte[],byte[]> protect, Func<byte[],byte[]> unprotect) : IStateStore {
    public AppState Load() {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<AppState>(unprotect(File.ReadAllBytes(path)),Wire.Json) ?? throw new InvalidDataException(); }
        catch (Exception error) { throw new InvalidDataException("无法读取此 Windows 用户保存的连接记录。请保留原文件，不要删除待确认发送记录。",error); }
    }
    public void Save(AppState state) {
        var bytes = protect(JsonSerializer.SerializeToUtf8Bytes(state,Wire.Json));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var file = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporary,path,true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
