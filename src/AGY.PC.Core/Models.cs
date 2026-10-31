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
public sealed record AgySkillItem(string Id, string Name, string Category, string Description, string TriggerText, string? FilePath = null, bool IsBuiltIn = true);
public sealed record TaskSettings(string Model, string Effort, string ApprovalMode, string Mode, string Workspace) {
    public TaskSettings Validate(ModelFamily[] families, string[] roots, string defaultModel) {
        var family = families.FirstOrDefault(f => f.Id == Model)
            ?? families.FirstOrDefault(f => f.Variants.Any(v => v.Id == defaultModel)) ?? families.First();
        var validWorkspace = !string.IsNullOrWhiteSpace(Workspace) && roots.Any(r => FileRules.Within(Workspace, r)) ? Workspace : roots.First();
        return this with {
            Model = family.Id,
            Effort = family.Efforts.Contains(Effort) ? Effort : family.DefaultEffort,
            ApprovalMode = ApprovalMode is "review" or "turbo" ? ApprovalMode : "review",
            Mode = Mode is "plan" or "accept-edits" ? Mode : "plan",
            Workspace = validWorkspace
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
    public List<AgySkillItem> CustomSkills { get; set; } = [];
}
public static class AgyCatalog {
    public static readonly AgySkillItem[] OfficialSlashCommands = [
        new("slash-plan", "/plan · 先做计划", "官方斜杠工作流", "在动手修改前先进行代码库调研，输出分步实施方案。", "/plan "),
        new("slash-boost", "/boost · 深度思考与验证", "官方斜杠工作流", "开启深度推理、多视角方案对比与严格验证，适合复杂架构或疑难杂症。", "/boost "),
        new("slash-goal", "/goal · 目标自治推进", "官方斜杠工作流", "面向长时复杂目标，持续自主执行并验证，直到完整达成目标才停止。", "/goal "),
        new("slash-teamwork", "/teamwork · 多代理协同", "官方斜杠工作流", "启动多个自治子代理（Subagents）并行分工，协同处理大型项目。", "/teamwork "),
        new("slash-grill", "/grill-me · 交互式对齐", "官方斜杠工作流", "通过结构化提问与你对齐设计取舍与边界条件，消除需求歧义。", "/grill-me "),
        new("slash-learn", "/learn · 沉淀项目经验", "官方斜杠工作流", "将本次调试经验、排错规律或工程规范沉淀为可复用的技能与规则。", "/learn "),
        new("slash-browser", "/browser · 浏览器与网页交互", "官方斜杠工作流", "使用浏览器检索在线文档、抓取网页或交互测试 Web 应用。", "/browser "),
        new("slash-schedule", "/schedule · 定时与周期任务", "官方斜杠工作流", "设置一次性延时任务或 Cron 周期性自动检查任务。", "/schedule "),
        new("slash-compact", "/compact · 压缩上下文", "官方斜杠工作流", "智能总结并压缩当前长对话历史，释放模型上下文窗口。", "/compact")
    ];

    public static readonly AgySkillItem[] BuiltinSkills = [
        new("skill-agy-customizations", "agy-customizations · 扩展与技能体系", "官方内置 Skill (SKILL.md)", "指导创建与配置 Antigravity 的 SKILL.md 技能、AGENTS.md / GEMINI.md 规则、Plugins、Hooks 与 MCP 服务。", "请激活并遵循内置技能 `agy-customizations`，协助我配置或编写项目的 Skills、Rules、Hooks 或 MCP 扩展：\n"),
        new("skill-antigravity-guide", "antigravity-guide · Antigravity 官方指南", "官方内置 Skill (SKILL.md)", "提供 Antigravity CLI (agy)、Antigravity 2.0、IDE、Python SDK、斜杠命令与权限沙箱的完整指南。", "请激活并遵循内置技能 `antigravity-guide`，解答或执行以下关于 Antigravity 的任务：\n"),
        new("skill-create-skill-md", "创建项目 SKILL.md 技能包", "技能工程模板", "在当前项目的 .agents/skills/<name>/SKILL.md 下创建可按需加载的专业工作流技能。", "请在当前项目工作区的 `.agents/skills/` 目录下创建一个符合 Antigravity 规范的 `SKILL.md` 技能文件（包含 YAML frontmatter 的 `name` 与 `description`，以及清晰的执行步骤说明）。技能主题为：\n"),
        new("skill-create-agents-md", "编写项目 AGENTS.md / GEMINI.md 规则", "规则工程模板", "在项目根目录创建或更新层级化上下文规则文件，约束代码风格与安全边界。", "请在当前项目工作区创建或完善 `AGENTS.md`（或 `GEMINI.md`）项目规则文件，明确本项目的架构约定、构建测试命令与代码规范：\n")
    ];
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
