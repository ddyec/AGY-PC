using System.Text.Json;
using System.Text.Json.Serialization;

namespace AGY.PC.Core;

public static class Wire {
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
}
public sealed record Credentials(string Address, string Token) {
    public static Credentials Validate(string address, string token) {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" || string.IsNullOrEmpty(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || url.AbsolutePath != "/" || token.Trim().Length < 32 || token.Any(char.IsControl))
            throw new InvalidOperationException("请输入 HTTPS 服务器地址和完整连接密钥。");
        return new(url.GetLeftPart(UriPartial.Authority), token.Trim());
    }
}
public sealed record Capabilities(bool SeparateEffort, bool NativeApprovals, bool Artifacts, bool FileBrowser, bool DirectoryMentions, bool ProjectSnapshots=false);
public sealed record GatewayStatus(string Name, int Version, string[] Roots, string DefaultModel, string AccountProfile, Capabilities Capabilities);
public sealed record ModelVariant(string Id, string Effort);
public sealed record ModelFamily(string Id, string Name, ModelVariant[] Variants, string[] Efforts, string DefaultEffort) { public override string ToString() => Name; }
public sealed record ModelInfo(string Id, string Name);
public sealed record ModelReply(ModelInfo[] Models, ModelFamily[] Families);
public sealed record SessionsReply(Session[] Sessions);
public sealed record Attachment(string Id, string Name, string Mime, long Size, string? Path);
public sealed record ToolCall(string Id, string Name, string State, JsonElement Parameters, string Output, bool? IsError);
public sealed record ResponseStep(int Index, string Text, string? Thinking = null);
public sealed record ChatMessage(string Id, string Role, string Text, Attachment[]? Attachments, ToolCall[]? Tools, ResponseStep[]? Steps);
public sealed record Approval(string Id, string SessionID, string Tool, JsonElement Parameters, double CreatedAt, double ExpiresAt);
public sealed record Session(string Id, string Name, string Cwd, string Model, string Mode, double Modified, bool Running,
    string ConversationID, string Error, int Revision, ChatMessage[]? Messages, string? BaseModel, string? Effort, string? ApprovalMode, Approval[]? Approvals) {
    public string DisplayName => FileRules.CleanText(Name) is { Length: > 0 } cleaned ? cleaned : "新会话";
    public override string ToString() => DisplayName;
}
public sealed record FileEntry(string Name, string Path, bool Directory, long Size, double Modified) {
    public string Label => (Directory ? "📁  " : "📄  ") + Name;
}
public sealed record FileListing(string Root, string Path, FileEntry[] Entries);
public sealed record LimitReply(double FetchedAt, string Source, LimitGroup[] Groups, string? Description = null);
public sealed record LimitGroup(string Name, string? Description, LimitBucket[] Buckets) {
    public string DisplayName => Name switch {
        "Gemini Models" => "Gemini 系列模型",
        "Claude and GPT models" => "Claude 与 GPT 系列模型",
        "Tab Models" => "Tab 代码补全模型",
        _ => Name
    };
    public string? DisplayDescription => Description switch {
        "Quota usage is shared across all Gemini models" => "所有 Gemini 模型共享此额度池",
        "Quota usage is shared across all Claude and GPT models" => "所有 Claude 与 GPT 模型共享此额度池",
        "Quota usage is shared across all Tab models" => "所有 Tab 补全模型共享此额度池",
        _ => Description
    };
}
public sealed record LimitBucket(
    string Id,
    string Name,
    [property: JsonPropertyName("remaining_fraction")] double? Remaining,
    [property: JsonPropertyName("reset_time")] string? Reset,
    string? Window,
    string? Description = null) {
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
    public double? EffectiveRemaining => Remaining ?? (Extra is not null && (Extra.TryGetValue("remaining", out var r) || Extra.TryGetValue("remainingFraction", out r)) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null);
    public string? EffectiveReset => !string.IsNullOrWhiteSpace(Reset) ? Reset : (Extra is not null && (Extra.TryGetValue("reset", out var s) || Extra.TryGetValue("resetTime", out s)) && s.ValueKind == JsonValueKind.String ? s.GetString() : null);
    public string DisplayTitle => Window switch {
        "5h" => "5 小时可用额度",
        "weekly" => "每周可用额度",
        _ => Name switch {
            "Weekly Limit Remaining" => "每周可用额度",
            "Five Hour Limit Remaining" => "5 小时可用额度",
            _ => Name
        }
    };
}
public sealed record Reference(string Path, string Label, bool Directory);
public sealed record PendingSend(string SessionID, string RequestID, string Text, string[] Attachments, Reference[] References);
public sealed record Draft(string Text, Reference[] References);
public sealed record QueuedMessage(string Id, string Text, Reference[] References, double CreatedAt);
public sealed record TaskSettings(string Model, string Effort, string ApprovalMode, string Mode, string Workspace) {
    public TaskSettings Validate(ModelFamily[] families, string[] roots, string defaultModel) {
        var family = families.FirstOrDefault(f => f.Id == Model)
            ?? families.FirstOrDefault(f => f.Variants.Any(v => v.Id == defaultModel)) ?? families.First();
        return this with {
            Model = family.Id, Effort = family.Efforts.Contains(Effort) ? Effort : family.DefaultEffort,
            ApprovalMode = ApprovalMode is "review" or "turbo" ? ApprovalMode : "review",
            Mode = Mode is "plan" or "accept-edits" ? Mode : "plan",
            Workspace = roots.Any(r => FileRules.Within(Workspace, r)) ? Workspace : roots.First()
        };
    }
}
public sealed class ProfileState {
    public TaskSettings? Settings { get; set; }
    public string Presentation { get; set; } = "原版";
    public string Theme { get; set; } = "Default";
    public BuildPlan? Build { get; set; }
    public PendingSend? Pending { get; set; }
    public Dictionary<string, Draft> Drafts { get; set; } = [];
    public Dictionary<string, List<QueuedMessage>> Queues { get; set; } = [];
}
public sealed class AppState {
    public NetworkSettings Network { get; set; } = new();
    public Credentials? Connection { get; set; }
    public Dictionary<string, ProfileState> Profiles { get; set; } = [];
}
public static class FileRules {
    public const long MaxFile = 25L * 1024 * 1024;
    public static string CleanText(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("\uFFFC", "").Replace("\uFFFD", "").Trim();
    public static string PresentationLabel(string value) => value switch {
        "步骤摘要" => "步骤摘要 · 思考过程",
        "结论优先" => "结论优先 · 折叠过程",
        _ => "原版 · 全部内容"
    };
    public static bool Within(string path, string root) => path.StartsWith('/') && !path.Split('/').Contains("..")
        && (path.TrimEnd('/') == root.TrimEnd('/') || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal));
    public static string Label(string scope, string folder, string name, bool directory) =>
        (scope == "uploads" ? "上传/" : scope == "artifacts" ? "产物/" : "") +
        string.Join('/', new[] { folder.Trim('/'), name }.Where(s => s.Length > 0)) + (directory ? "/" : "");
    public static string SafeName(string value) {
        var name = value.Replace('\\','/').Split('/').LastOrDefault() ?? "download";
        name = new string(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).TrimEnd(' ','.');
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        var stem = name.Split('.')[0];
        if (new[] {"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name.Length > 180 ? name[..180] : name;
    }
    public static string EffortLabel(string value) => value switch { "low" => "低", "medium" => "中", "high" => "高", "xhigh" => "更高", "max" => "最高", "" => "由模型决定", _ => value };
}
