namespace AGY.PC.Core;

public sealed class Workspace(IStateStore store) : IDisposable {
    private AppState state = store.Load();
    private GatewayClient? client;
    private CancellationTokenSource? watch;
    private int generation;
    private bool draining;
    private string scope = "";
    private ProfileState profile = new();
    private readonly Dictionary<string, Session> sessionCache = [];
    private DateTimeOffset lastProfileCheck;
    private readonly Func<Credentials,GatewayClient>? factory;
    public Workspace(IStateStore store, Func<Credentials,GatewayClient> factory) : this(store) { this.factory = factory; }
    public event Action? Changed;
    public Credentials? SavedConnection => state.Connection;
    public NetworkSettings Network => state.Network;
    public void SetNetwork(NetworkSettings settings) { settings.Validate(); state.Network=settings; Save(); }
    public bool Connected => client is not null && Status is not null;
    public GatewayStatus? Status { get; private set; }
    public ModelFamily[] Families { get; private set; } = [];
    public Session[] Sessions { get; private set; } = [];
    public Session? Selected { get; private set; }
    public TaskSettings Settings { get; private set; } = new("","","review","plan","");
    public PendingSend? Pending => profile.Pending;
    public Draft Draft => profile.Drafts.GetValueOrDefault(Selected?.Id ?? "new") ?? new("",[]);
    public IReadOnlyList<QueuedMessage> Queue => profile.Queues.GetValueOrDefault(Selected?.Id ?? "new") is { } list ? list : [];
    public IReadOnlyList<AgySkillItem> CustomSkills => profile.CustomSkills;
    public string ConnectionLabel { get; private set; } = "尚未连接";
    public string? Notice { get; private set; }
    public string Presentation => profile.Presentation;
    public string Theme => profile.Theme;
    public BuildPlan? Build => profile.Build;
    public void RememberBuild(BuildPlan plan) { profile.Build=plan;Save(); }
    public void SetTheme(string theme) { if(theme is not ("Default" or "Light" or "Dark"))return;profile.Theme=theme;Save(); }
    public void SetPresentation(string value) { if (value is not ("原版" or "步骤摘要" or "结论优先")) return; profile.Presentation=value; Save(); }
    public void SaveCustomSkill(string name, string description, string triggerText, string? filePath = null) {
        var cleanName = name.Trim();
        var cleanDesc = string.IsNullOrWhiteSpace(description) ? "用户自定义技能 / 指令" : description.Trim();
        var cleanTrigger = triggerText.Trim();
        var cleanPath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim().Replace('\\', '/');
        SaveCustomSkill(new("custom-" + Guid.NewGuid().ToString("N")[..10], cleanName, "自定义技能", cleanDesc, cleanTrigger, cleanPath, false));
    }
    public void SaveCustomSkill(AgySkillItem item) {
        if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.TriggerText))
            throw new InvalidOperationException("请填写技能或指令的名称与触发内容。");
        profile.CustomSkills.RemoveAll(s => s.Id == item.Id || s.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        profile.CustomSkills.Insert(0, item with { IsBuiltIn = false });
        Save();
        Publish();
    }
    public void RemoveCustomSkill(string id) {
        profile.CustomSkills.RemoveAll(s => s.Id == id);
        Save();
        Publish();
    }
    public void ApplySkill(AgySkillItem skill) {
        if (!string.IsNullOrWhiteSpace(skill.FilePath) && Pending is null) {
            var fullPath = skill.FilePath.StartsWith('/')
                ? skill.FilePath
                : Settings.Workspace.TrimEnd('/') + "/" + skill.FilePath.TrimStart('/');
            try { AddReference(new(fullPath, FileRules.Label("workspace", "", skill.Name, false), false)); } catch { }
        }
        var current = Draft.Text;
        var nextText = string.IsNullOrWhiteSpace(current)
            ? skill.TriggerText
            : skill.TriggerText.StartsWith('/') && !current.StartsWith('/')
                ? skill.TriggerText.TrimEnd() + " " + current
                : current.TrimEnd() + "\n" + skill.TriggerText;
        UpdateDraft(nextText);
        Publish();
    }
    public async Task<AgySkillItem[]> DiscoverWorkspaceSkillsAsync() {
        var discovered = new List<AgySkillItem>();
        if (!Connected || Status?.Capabilities.FileBrowser != true) return [..discovered];
        try {
            var rootListing = await BrowseAsync("workspace", "");
            foreach (var entry in rootListing.Entries) {
                if (!entry.Directory && entry.Name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
                    discovered.Add(new("ws-agents-md", "AGENTS.md · 项目智能体规则", "当前工作区规则", "当前工作区根目录的 AGENTS.md 上下文规则文件。", "请严格遵循项目根目录的 AGENTS.md 规则执行后续任务：\n", entry.Path, false));
                else if (!entry.Directory && entry.Name.Equals("GEMINI.md", StringComparison.OrdinalIgnoreCase))
                    discovered.Add(new("ws-gemini-md", "GEMINI.md · 项目上下文规则", "当前工作区规则", "当前工作区根目录的 GEMINI.md 规则文件。", "请读取并遵循项目根目录的 GEMINI.md 规则：\n", entry.Path, false));
                else if (!entry.Directory && entry.Name.Equals("skills.json", StringComparison.OrdinalIgnoreCase))
                    discovered.Add(new("ws-skills-json", "skills.json · 声明式技能清单", "当前工作区技能", "当前工作区的 skills.json 技能注册配置。", "请检查并加载项目的 skills.json 配置：\n", entry.Path, false));
            }
            foreach (var skillDir in new[] { ".agents/skills", ".agent/skills", "skills" }) {
                try {
                    var sub = await BrowseAsync("workspace", skillDir);
                    foreach (var folder in sub.Entries.Where(e => e.Directory)) {
                        var skillPath = folder.Path.TrimEnd('/') + "/SKILL.md";
                        discovered.Add(new("ws-skill-" + folder.Name, $"{folder.Name} (SKILL.md)", "当前工作区技能", $"位于 {skillDir}/{folder.Name}/SKILL.md 的项目专属技能。", $"请阅读并激活项目技能 `{skillDir}/{folder.Name}/SKILL.md` 来处理以下任务：\n", skillPath, false));
                    }
                } catch { }
            }
        } catch { }
        return [..discovered];
    }
    private GatewayClient Api => client ?? throw new InvalidOperationException("请先连接服务器。");
    private void Publish() => Changed?.Invoke();
    private void Save() { if (scope.Length > 0) state.Profiles[scope] = profile; store.Save(state); }
    public async Task ConnectAsync(Credentials credentials) {
        var epoch = ++generation;
        watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; Sessions = []; sessionCache.Clear();
        var candidate = factory is null ? new GatewayClient(credentials, network: state.Network) : factory(credentials);
        try {
            var status = await candidate.JsonAsync<GatewayStatus>("status");
            var models = await candidate.JsonAsync<ModelReply>("models");
            if (generation != epoch) { candidate.Dispose(); return; }
            if (status.Version != 1 || status.Roots.Length == 0 || models.Families.Length == 0 || !status.Capabilities.SeparateEffort || !status.Capabilities.NativeApprovals)
                throw new InvalidOperationException("请先部署与 AGY iPhone 1.3 配套的新版账号和审批网关。");
            client = candidate; Status = status; Families = models.Families;
            lastProfileCheck = DateTimeOffset.UtcNow;
            scope = credentials.Address.TrimEnd('/') + "#" + status.AccountProfile;
            profile = state.Profiles.GetValueOrDefault(scope) ?? new();
            var saved = profile.Settings;
            Settings = (saved ?? ServerDefaults()).Validate(Families,status.Roots,status.DefaultModel);
            Notice = null;
            if (Settings.Workspace != status.Roots[0]) {
                try { await Api.JsonAsync<FileListing>("workspace",query:new() { ["cwd"] = Settings.Workspace }); }
                catch (GatewayException error) when (error.Status is 400 or 403 or 404) { Settings = Settings with { Workspace = status.Roots[0] }; Notice = "上次的项目暂不可用，已返回工作区。"; }
            }
            state.Connection = credentials; Save();
            await ReloadAsync();
            if (profile.Pending is { } pending) await SelectAsync(pending.SessionID);
            ConnectionLabel = "已连接"; Publish();
        } catch { if (client == candidate) { client = null; Status = null; } candidate.Dispose(); throw; }
    }
    private async Task VerifyProfileAsync() {
        if (factory is null && (DateTimeOffset.UtcNow - lastProfileCheck) < TimeSpan.FromSeconds(20))
            return;
        var status = await Api.JsonAsync<GatewayStatus>("status");
        if (status.AccountProfile != Status?.AccountProfile) {
            watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; ++generation;
            throw new InvalidOperationException("服务器已切换账号，请重新连接；没有向新账号发送操作。");
        }
        lastProfileCheck = DateTimeOffset.UtcNow;
    }
    private void UpsertSession(Session snapshot) {
        sessionCache[snapshot.Id] = snapshot;
        var existing = Sessions.ToList();
        var idx = existing.FindIndex(s => s.Id == snapshot.Id);
        if (idx >= 0) existing[idx] = snapshot;
        else existing.Insert(0, snapshot);
        Sessions = [..existing];
    }
    public async Task ReloadAsync() { Sessions = (await Api.JsonAsync<SessionsReply>("sessions")).Sessions; Publish(); }
    public void UpdateDraft(string text, Reference[]? references = null) {
        profile.Drafts[Selected?.Id ?? "new"] = new(text,references ?? Draft.References);
        Save();
    }
    public void AddReference(Reference reference) {
        if (Pending is not null) throw new InvalidOperationException("请先核对待确认发送。");
        var draft = Draft;
        if (draft.References.Any(r => r.Path == reference.Path)) { if(draft.Text.EndsWith('@'))UpdateDraft(draft.Text[..^1]);Publish();return; }
        if (draft.References.Length >= 8) throw new InvalidOperationException("最多引用 8 个文件或文件夹。");
        var text = draft.Text.EndsWith('@') ? draft.Text[..^1] : draft.Text;
        UpdateDraft(text,[..draft.References,reference]); Publish();
    }
    public void RemoveReference(string path) { UpdateDraft(Draft.Text,Draft.References.Where(r => r.Path != path).ToArray()); Publish(); }
    public void NewConversation() {
        if (Pending is not null) throw new InvalidOperationException("请先核对或重试待确认发送。");
        ++generation; watch?.Cancel(); Selected = null;
        Settings = (profile.Settings ?? ServerDefaults()).Validate(Families,Status!.Roots,Status.DefaultModel);
        Notice = null; Publish();
    }
    public async Task SelectAsync(string id, bool forceRefresh = false) {
        var epoch = ++generation; watch?.Cancel();
        if (!forceRefresh && sessionCache.TryGetValue(id, out var cached)) {
            Selected = cached;
            Settings = SettingsFrom(cached);
            Publish();
        }
        var selected = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(id)}");
        if (epoch != generation) return;
        UpsertSession(selected);
        Selected = selected;
        Settings = SettingsFrom(selected);
        ConnectionLabel = selected.Approvals?.Length > 0 ? "等待审批" : selected.Running ? "AGY 正在工作" : "已就绪";
        Reconcile(selected); Publish();
        if (selected.Running || (profile.Queues.TryGetValue(selected.Id, out var q) && q.Count > 0))
            StartWatch();
    }
    private TaskSettings ServerDefaults() {
        var status=Status??throw new InvalidOperationException("请先连接。");
        var family=Families.FirstOrDefault(f=>f.Variants.Any(v=>v.Id==status.DefaultModel))??Families[0];
        var thinking=family.Variants.FirstOrDefault(v=>v.Id==status.DefaultModel)?.Effort??family.DefaultEffort;
        return new(family.Id,thinking,"review","plan",status.Roots[0]);
    }
    private TaskSettings SettingsFrom(Session session) {
        var family = Families.FirstOrDefault(f => f.Variants.Any(v => v.Id == session.Model));
        var remembered = profile.Settings;
        // If this is an unused empty session ("新会话") and user has remembered preferences, retain remembered approval/mode if session was legacy.
        var approval = session.ApprovalMode is "review" or "turbo" ? session.ApprovalMode : remembered?.ApprovalMode ?? "review";
        return new(family?.Id ?? session.BaseModel ?? session.Model,
            family?.Variants.First(v => v.Id == session.Model).Effort ?? session.Effort ?? "",
            approval,session.Mode,session.Cwd);
    }
    public string NormalizeWorkspacePath(string rawPath) {
        if (Status is null) throw new InvalidOperationException("请先连接。");
        var trimmed = rawPath.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(trimmed)) return Status.Roots[0];
        if (!trimmed.StartsWith('/')) {
            trimmed = Status.Roots[0].TrimEnd('/') + "/" + trimmed.TrimStart('/');
        }
        if (trimmed.Length > 1) trimmed = trimmed.TrimEnd('/');
        if (!Status.Roots.Any(root => FileRules.Within(trimmed, root)))
            throw new InvalidOperationException($"指定路径须位于服务器允许的根目录下（{string.Join(" 或 ", Status.Roots)}）。");
        return trimmed;
    }
    public async Task ConfigureAsync(TaskSettings next) {
        if (Pending is not null) throw new InvalidOperationException("请先核对待确认发送。");
        if (Status is null) throw new InvalidOperationException("请先连接。");
        var normalizedWorkspace = NormalizeWorkspacePath(next.Workspace);
        next = (next with { Workspace = normalizedWorkspace }).Validate(Families, Status.Roots, Status.DefaultModel);

        // Always persist user preference immediately so model & strategy choices are remembered across sessions and restarts.
        profile.Settings = next;
        Save();

        if (Selected?.Running == true) {
            Settings = next;
            Publish();
            return;
        }

        await VerifyProfileAsync();
        if (Selected is { } selected) {
            if (next.Workspace != selected.Cwd) {
                if ((selected.Messages?.Length ?? 0) == 0) {
                    Selected = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(selected.Id)}", HttpMethod.Patch, ConfigBody(next));
                    sessionCache[Selected.Id] = Selected;
                    next = SettingsFrom(Selected);
                } else {
                    // Automatically switch to a new conversation in the newly specified workspace rather than throwing an error.
                    ++generation; watch?.Cancel(); Selected = null;
                }
            } else {
                Selected = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(selected.Id)}",HttpMethod.Patch,ConfigBody(next));
                sessionCache[Selected.Id] = Selected;
                next = SettingsFrom(Selected);
            }
        }
        Settings = next; profile.Settings = next; Save(); Publish();
    }
    private static object ConfigBody(TaskSettings settings) => new { cwd = settings.Workspace, baseModel = settings.Model, effort = settings.Effort, approvalMode = settings.ApprovalMode, mode = settings.Mode };
    public async Task<string> EnsureSessionAsync() {
        if (Selected is { } selected) return selected.Id;
        if (Pending is not null) throw new InvalidOperationException("请先核对待确认发送。");
        await VerifyProfileAsync();
        if (Settings.ApprovalMode == "legacy") throw new InvalidOperationException("请先选择审批策略。");
        var draft = Draft;
        var queued = profile.Queues.GetValueOrDefault("new");
        Selected = await Api.JsonAsync<Session>("sessions",HttpMethod.Post,ConfigBody(Settings));
        UpsertSession(Selected);
        profile.Drafts[Selected.Id] = draft; profile.Drafts.Remove("new");
        if (queued is { Count: > 0 }) { profile.Queues[Selected.Id] = queued; profile.Queues.Remove("new"); }
        Save(); Publish();
        if (factory is not null) await ReloadAsync();
        else _ = ReloadAsync();
        return Selected.Id;
    }
    public async Task SendAsync() {
        if (Pending is not null || Selected?.Running == true) throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");
        var draft = Draft;
        if (string.IsNullOrWhiteSpace(draft.Text) && draft.References.Length == 0) {
            if (Queue.Count > 0) await DrainQueueAsync();
            return;
        }
        await VerifyProfileAsync();
        var sid = await EnsureSessionAsync();
        profile.Pending = new(sid,Guid.NewGuid().ToString(),draft.Text,[],draft.References);
        Save(); // A failed durable write MUST prevent the network send.
        await SubmitPendingAsync();
    }
    public async Task EnqueueAsync() {
        if (Pending is not null) throw new InvalidOperationException("请先核对待确认发送。");
        var draft = Draft;
        if (string.IsNullOrWhiteSpace(draft.Text) && draft.References.Length == 0) return;
        var key = Selected?.Id ?? "new";
        if (!profile.Queues.TryGetValue(key, out var list)) {
            list = [];
            profile.Queues[key] = list;
        }
        list.Add(new(Guid.NewGuid().ToString(), draft.Text, draft.References, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        profile.Drafts[key] = new("", []);
        Save();
        Publish();
        if (Selected?.Running != true && Pending is null) {
            await DrainQueueAsync();
        }
    }
    public void RemoveQueued(string id) {
        var key = Selected?.Id ?? "new";
        if (!profile.Queues.TryGetValue(key, out var list)) return;
        list.RemoveAll(q => q.Id == id);
        if (list.Count == 0) profile.Queues.Remove(key);
        Save();
        Publish();
    }
    public void EditQueued(string id) {
        var key = Selected?.Id ?? "new";
        if (!profile.Queues.TryGetValue(key, out var list)) return;
        var item = list.FirstOrDefault(q => q.Id == id);
        if (item is null) return;
        list.RemoveAll(q => q.Id == id);
        if (list.Count == 0) profile.Queues.Remove(key);
        var current = Draft;
        var mergedText = string.IsNullOrWhiteSpace(current.Text) ? item.Text : item.Text + "\n" + current.Text;
        var mergedRefs = item.References.Concat(current.References.Where(r => !item.References.Any(ir => ir.Path == r.Path))).Take(8).ToArray();
        profile.Drafts[key] = new(mergedText, mergedRefs);
        Save();
        Publish();
    }
    public async Task DrainQueueAsync() {
        if (draining || Pending is not null || Selected?.Running == true) return;
        var key = Selected?.Id ?? "new";
        if (!profile.Queues.TryGetValue(key, out var initial) || initial.Count == 0) return;
        draining = true;
        try {
            await VerifyProfileAsync();
            var sid = await EnsureSessionAsync();
            if (!profile.Queues.TryGetValue(sid, out var list) || list.Count == 0) return;
            var next = list[0];
            list.RemoveAt(0);
            if (list.Count == 0) profile.Queues.Remove(sid);
            profile.Pending = new(sid, next.Id, next.Text, [], next.References);
            Save();
            Publish();
            await SubmitPendingAsync();
        } finally {
            draining = false;
        }
    }
    public async Task SteerNowAsync(string? queuedId = null) {
        if (Pending is not null) throw new InvalidOperationException("请先核对待确认发送。");
        string text;
        Reference[] references;
        string requestId;
        var key = Selected?.Id ?? "new";
        var clearedDraft = false;
        var originalDraft = Draft;
        if (queuedId is not null) {
            if (!profile.Queues.TryGetValue(key, out var list) || list.FirstOrDefault(q => q.Id == queuedId) is not { } item)
                return;
            list.RemoveAll(q => q.Id == queuedId);
            if (list.Count == 0) profile.Queues.Remove(key);
            text = item.Text;
            references = item.References;
            requestId = item.Id;
            Save();
            Publish();
        } else if (!string.IsNullOrWhiteSpace(originalDraft.Text) || originalDraft.References.Length > 0) {
            text = originalDraft.Text;
            references = originalDraft.References;
            requestId = Guid.NewGuid().ToString();
            profile.Drafts[key] = new("", []);
            clearedDraft = true;
            Save();
            Publish();
        } else if (profile.Queues.TryGetValue(key, out var list) && list.Count > 0) {
            var item = list[0];
            list.RemoveAt(0);
            if (list.Count == 0) profile.Queues.Remove(key);
            text = item.Text;
            references = item.References;
            requestId = item.Id;
            Save();
            Publish();
        } else {
            return;
        }

        try {
            await VerifyProfileAsync();
            var sid = await EnsureSessionAsync();
            watch?.Cancel();
            if (Selected?.Running == true) {
                ConnectionLabel = "正在打断并立即引导…";
                Publish();
                var stopped = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(sid)}/stop", HttpMethod.Post, new { });
                for (var i = 0; i < 25 && stopped.Running; i++) {
                    await Task.Delay(120);
                    stopped = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(sid)}");
                }
                Selected = stopped;
            }
            profile.Pending = new(sid, requestId, text, [], references);
            Save();
            for (var attempt = 0; ; attempt++) {
                try {
                    await SubmitPendingAsync();
                    break;
                } catch (GatewayException ex) when (ex.Status == 409 && attempt < 20) {
                    await Task.Delay(150);
                }
            }
        } catch {
            if (clearedDraft && Pending is null) {
                profile.Drafts[Selected?.Id ?? key] = originalDraft;
                Save();
                Publish();
            }
            throw;
        }
    }
    public async Task SubmitPendingAsync() {
        var pending = Pending ?? throw new InvalidOperationException("没有待确认发送。");
        Save();
        await VerifyProfileAsync();
        var snapshot = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(pending.SessionID)}/messages",HttpMethod.Post,
            new { text = pending.Text, requestID = pending.RequestID, attachments = pending.Attachments, references = pending.References.Select(r => r.Path).ToArray() });
        UpsertSession(snapshot);
        Reconcile(snapshot);
        if (Selected?.Id == snapshot.Id) { Selected = snapshot; StartWatch(); }
        Publish();
        if (factory is not null) await ReloadAsync();
        else _ = ReloadAsync();
    }
    private void Reconcile(Session snapshot) {
        if (Pending is not { } pending || pending.SessionID != snapshot.Id || snapshot.Messages?.Any(m => m.Id == pending.RequestID) != true) return;
        var draft = profile.Drafts.GetValueOrDefault(snapshot.Id);
        if (draft is not null) profile.Drafts[snapshot.Id] = new(draft.Text == pending.Text ? "" : draft.Text,
            draft.References.Where(r => !pending.References.Any(sent => sent.Path == r.Path)).ToArray());
        profile.Pending = null; Save();
    }
    private void StartWatch() {
        watch?.Cancel(); watch = new();
        var ct = watch.Token; var epoch = generation; var selected = Selected;
        if (selected is null) return;
        _ = WatchAsync(selected.Id,epoch,ct);
    }
    private async Task WatchAsync(string sid, int epoch, CancellationToken ct) {
        int failures = 0;
        while (!ct.IsCancellationRequested && epoch == generation) {
            try {
                await foreach (var snapshot in Api.EventsAsync(sid,ct)) {
                    if (ct.IsCancellationRequested || epoch != generation || Selected?.Id != sid) return;
                    if (snapshot.Revision < Selected.Revision) continue;
                    sessionCache[snapshot.Id] = snapshot;
                    Selected = snapshot; Reconcile(snapshot);
                    ConnectionLabel = snapshot.Approvals?.Length > 0 ? "等待审批" : snapshot.Running ? "AGY 正在工作" : "已就绪";
                    Notice = null; Publish();
                    if (!snapshot.Running && (snapshot.Approvals?.Length ?? 0) == 0 && string.IsNullOrEmpty(snapshot.Error) && Pending is null && profile.Queues.TryGetValue(sid, out var ready) && ready.Count > 0)
                        break;
                }
                failures = 0;
                if (Selected?.Running != true) {
                    if (!ct.IsCancellationRequested && epoch == generation && Selected?.Id == sid && (Selected.Approvals?.Length ?? 0) == 0 && string.IsNullOrEmpty(Selected.Error) && Pending is null && profile.Queues.TryGetValue(sid, out var queued) && queued.Count > 0) {
                        await DrainQueueAsync();
                        return;
                    }
                    await ReloadAsync();
                    return;
                }
            } catch (Exception error) {
                if (ct.IsCancellationRequested || epoch != generation) return;
                failures++; ConnectionLabel = "连接中断，正在重连";
                if (failures >= 3) Notice = error is GatewayException ? error.Message : "网络中断，正在重新读取会话；不会自动重发任务。";
                Publish();
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(10,1+failures*2)),ct); } catch (OperationCanceledException) { return; }
        }
    }
    public async Task StopAsync() {
        if (Selected is not { } s) return;
        watch?.Cancel();
        await VerifyProfileAsync(); await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(s.Id)}/stop",HttpMethod.Post,new { }); await SelectAsync(s.Id);
    }
    public async Task AnswerAsync(Approval approval, bool allow) {
        if (Selected?.Id != approval.SessionID) throw new InvalidOperationException("审批不属于当前会话。");
        await VerifyProfileAsync();
        var snapshot = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(approval.SessionID)}/approvals/{GatewayClient.Id(approval.Id)}",HttpMethod.Post,new { decision = allow ? "allow" : "deny" });
        sessionCache[snapshot.Id] = snapshot;
        if (Selected?.Id == snapshot.Id && snapshot.Revision >= Selected.Revision) Selected = snapshot;
        Publish();
    }
    public async Task<FileListing> BrowseAsync(string scope = "workspace", string path = "", string? root = null) {
        if (Status?.Capabilities.FileBrowser != true) throw new InvalidOperationException("请先更新配套网关以使用文件管理。");
        return Selected is { } s && root is null
            ? await Api.JsonAsync<FileListing>($"sessions/{GatewayClient.Id(s.Id)}/entries",query:new() { ["scope"] = scope,["path"] = path })
            : scope == "workspace" ? await Api.JsonAsync<FileListing>("workspace",query:new() { ["cwd"] = root ?? Settings.Workspace,["path"] = path }) : new("","",[]);
    }
    public async Task<Attachment> UploadAsync(string local, bool reference) {
        await VerifyProfileAsync(); var sid = await EnsureSessionAsync();
        var attachment = await Api.UploadAsync(sid,local);
        if (reference && attachment.Path is { } path && Selected?.Id == sid) AddReference(new(path,"上传/"+attachment.Name,false));
        return attachment;
    }
    public async Task DownloadAsync(string remote, string destination) {
        await VerifyProfileAsync(); var sid = await EnsureSessionAsync(); await Api.DownloadAsync(sid,remote,destination);
    }
    public async Task SynchronizeProjectAsync(string destination) {
        if(Selected?.Running==true)throw new InvalidOperationException("请等待当前服务器任务结束后同步项目。");
        if(Status?.Capabilities.ProjectSnapshots!=true)throw new InvalidOperationException("服务器尚未启用项目同步，请更新配套网关。");
        await VerifyProfileAsync();var sid=await EnsureSessionAsync();
        var temporary=Path.Combine(Path.GetTempPath(),"agy-project-"+Guid.NewGuid()+".zip");
        try{await Api.DownloadAsync(sid,Settings.Workspace,temporary,snapshot:true);HybridBuild.ExtractSnapshot(temporary,destination);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public Task<LimitReply> LimitsAsync(bool forceRefresh = true) => Api.JsonAsync<LimitReply>("limits",query:forceRefresh ? new() { ["refresh"] = "1" } : null);
    public async Task RefreshModelsAsync() {
        Families = (await Api.JsonAsync<ModelReply>("models",query:new() { ["refresh"] = "1" })).Families;
        if (Selected is null) Settings = Settings.Validate(Families,Status!.Roots,Status.DefaultModel);
        Publish();
    }
    public async Task RenameAsync(Session session, string name) {
        await VerifyProfileAsync(); await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(session.Id)}",HttpMethod.Patch,new { name });
        sessionCache.Remove(session.Id);
        if (Selected?.Id == session.Id) await SelectAsync(session.Id); await ReloadAsync();
    }
    public async Task DeleteAsync(Session session) {
        if (Pending?.SessionID == session.Id) throw new InvalidOperationException("这条会话还有待确认发送，不能删除。");
        await VerifyProfileAsync(); await Api.JsonAsync<System.Text.Json.JsonElement>($"sessions/{GatewayClient.Id(session.Id)}",HttpMethod.Delete);
        sessionCache.Remove(session.Id);
        profile.Drafts.Remove(session.Id); profile.Queues.Remove(session.Id); Save(); if (Selected?.Id == session.Id) NewConversation(); await ReloadAsync();
    }
    public void Disconnect() { watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; sessionCache.Clear(); ++generation; state.Connection = null; Save(); scope = ""; profile = new(); Publish(); }
    public void Dispose() { watch?.Cancel(); watch?.Dispose(); client?.Dispose(); }
}
