namespace AGY.PC.Core;

public sealed class Workspace(IStateStore store) : IDisposable {
    private AppState state = store.Load();
    private GatewayClient? client;
    private CancellationTokenSource? watch;
    private int generation;
    private string scope = "";
    private ProfileState profile = new();
    private readonly Func<Credentials,GatewayClient> factory = c => new(c);
    public Workspace(IStateStore store, Func<Credentials,GatewayClient> factory) : this(store) { this.factory = factory; }
    public event Action? Changed;
    public Credentials? SavedConnection => state.Connection;
    public bool Connected => client is not null && Status is not null;
    public GatewayStatus? Status { get; private set; }
    public ModelFamily[] Families { get; private set; } = [];
    public Session[] Sessions { get; private set; } = [];
    public Session? Selected { get; private set; }
    public TaskSettings Settings { get; private set; } = new("","","review","plan","");
    public PendingSend? Pending => profile.Pending;
    public Draft Draft => profile.Drafts.GetValueOrDefault(Selected?.Id ?? "new") ?? new("",[]);
    public string ConnectionLabel { get; private set; } = "尚未连接";
    public string? Notice { get; private set; }
    public string Presentation => profile.Presentation;
    public string Theme => profile.Theme;
    public BuildPlan? Build => profile.Build;
    public void RememberBuild(BuildPlan plan) { profile.Build=plan;Save(); }
    public void SetTheme(string theme) { if(theme is not ("Default" or "Light" or "Dark"))return;profile.Theme=theme;Save(); }
    public void SetPresentation(string value) { if (value is not ("原版" or "步骤摘要" or "结论优先")) return; profile.Presentation=value; Save(); }
    private GatewayClient Api => client ?? throw new InvalidOperationException("请先连接服务器。");
    private void Publish() => Changed?.Invoke();
    private void Save() { if (scope.Length > 0) state.Profiles[scope] = profile; store.Save(state); }
    public async Task ConnectAsync(Credentials credentials) {
        var epoch = ++generation;
        watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; Sessions = [];
        var candidate = factory(credentials);
        try {
            var status = await candidate.JsonAsync<GatewayStatus>("status");
            var models = await candidate.JsonAsync<ModelReply>("models");
            if (generation != epoch) { candidate.Dispose(); return; }
            if (status.Version != 1 || status.Roots.Length == 0 || models.Families.Length == 0 || !status.Capabilities.SeparateEffort || !status.Capabilities.NativeApprovals)
                throw new InvalidOperationException("请先部署与 AGY iPhone 1.3 配套的新版账号和审批网关。");
            client = candidate; Status = status; Families = models.Families;
            scope = credentials.Address.TrimEnd('/') + "#" + status.AccountProfile;
            profile = state.Profiles.GetValueOrDefault(scope) ?? new();
            Settings = (profile.Settings ?? ServerDefaults())
                .Validate(Families,status.Roots,status.DefaultModel);
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
        var status = await Api.JsonAsync<GatewayStatus>("status");
        if (status.AccountProfile != Status?.AccountProfile) {
            watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; ++generation;
            throw new InvalidOperationException("服务器已切换账号，请重新连接；没有向新账号发送操作。");
        }
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
    public async Task SelectAsync(string id) {
        var epoch = ++generation; watch?.Cancel();
        var selected = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(id)}");
        if (epoch != generation) return;
        Selected = selected;
        Settings = SettingsFrom(selected);
        Reconcile(selected); Publish(); StartWatch();
    }
    private TaskSettings ServerDefaults() {
        var status=Status??throw new InvalidOperationException("请先连接。");
        var family=Families.FirstOrDefault(f=>f.Variants.Any(v=>v.Id==status.DefaultModel))??Families[0];
        var thinking=family.Variants.FirstOrDefault(v=>v.Id==status.DefaultModel)?.Effort??family.DefaultEffort;
        return new(family.Id,thinking,"review","plan",status.Roots[0]);
    }
    private TaskSettings SettingsFrom(Session session) {
        var family = Families.FirstOrDefault(f => f.Variants.Any(v => v.Id == session.Model));
        return new(family?.Id ?? session.BaseModel ?? session.Model,
            family?.Variants.First(v => v.Id == session.Model).Effort ?? session.Effort ?? "",
            session.ApprovalMode ?? "legacy",session.Mode,session.Cwd);
    }
    public async Task ConfigureAsync(TaskSettings next) {
        if (Selected?.Running == true || Pending is not null) throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");
        if (Status is null) throw new InvalidOperationException("请先连接。");
        if (!Families.Any(f => f.Id == next.Model && f.Efforts.Contains(next.Effort)) || next.ApprovalMode is not ("review" or "turbo") || next.Mode is not ("plan" or "accept-edits"))
            throw new InvalidOperationException("请选择可用模型、思考强度和审批策略。");
        if (!Status.Roots.Any(root => FileRules.Within(next.Workspace,root))) throw new InvalidOperationException("工作区不在允许范围。");
        await VerifyProfileAsync();
        if (Selected is { } selected) {
            if (next.Workspace != selected.Cwd) throw new InvalidOperationException("已有会话的工作区固定，请新建会话再选择项目。");
            Selected = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(selected.Id)}",HttpMethod.Patch,ConfigBody(next));
            next = SettingsFrom(Selected);
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
        Selected = await Api.JsonAsync<Session>("sessions",HttpMethod.Post,ConfigBody(Settings));
        profile.Drafts[Selected.Id] = draft; profile.Drafts.Remove("new"); Save(); Publish();
        await ReloadAsync(); return Selected.Id;
    }
    public async Task SendAsync() {
        if (Pending is not null || Selected?.Running == true) throw new InvalidOperationException("请先结束当前任务或核对待确认发送。");
        var draft = Draft;
        if (string.IsNullOrWhiteSpace(draft.Text) && draft.References.Length == 0) return;
        await VerifyProfileAsync();
        var sid = await EnsureSessionAsync();
        profile.Pending = new(sid,Guid.NewGuid().ToString(),draft.Text,[],draft.References);
        Save(); // A failed durable write MUST prevent the network send.
        await SubmitPendingAsync();
    }
    public async Task SubmitPendingAsync() {
        var pending = Pending ?? throw new InvalidOperationException("没有待确认发送。");
        Save();
        await VerifyProfileAsync();
        var snapshot = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(pending.SessionID)}/messages",HttpMethod.Post,
            new { text = pending.Text, requestID = pending.RequestID, attachments = pending.Attachments, references = pending.References.Select(r => r.Path).ToArray() });
        Reconcile(snapshot);
        if (Selected?.Id == snapshot.Id) { Selected = snapshot; StartWatch(); }
        Publish(); await ReloadAsync();
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
                    Selected = snapshot; Reconcile(snapshot);
                    ConnectionLabel = snapshot.Approvals?.Length > 0 ? "等待审批" : snapshot.Running ? "AGY 正在工作" : "已就绪";
                    Notice = null; Publish();
                }
                failures = 0;
                if (Selected?.Running != true) { await ReloadAsync(); return; }
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
        await VerifyProfileAsync(); await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(s.Id)}/stop",HttpMethod.Post,new { }); await SelectAsync(s.Id);
    }
    public async Task AnswerAsync(Approval approval, bool allow) {
        if (Selected?.Id != approval.SessionID) throw new InvalidOperationException("审批不属于当前会话。");
        await VerifyProfileAsync();
        var snapshot = await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(approval.SessionID)}/approvals/{GatewayClient.Id(approval.Id)}",HttpMethod.Post,new { decision = allow ? "allow" : "deny" });
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
    public Task<LimitReply> LimitsAsync() => Api.JsonAsync<LimitReply>("limits",query:new() { ["refresh"] = "1" });
    public async Task RefreshModelsAsync() {
        Families = (await Api.JsonAsync<ModelReply>("models",query:new() { ["refresh"] = "1" })).Families;
        if (Selected is null) Settings = Settings.Validate(Families,Status!.Roots,Status.DefaultModel);
        Publish();
    }
    public async Task RenameAsync(Session session, string name) {
        await VerifyProfileAsync(); await Api.JsonAsync<Session>($"sessions/{GatewayClient.Id(session.Id)}",HttpMethod.Patch,new { name });
        if (Selected?.Id == session.Id) await SelectAsync(session.Id); await ReloadAsync();
    }
    public async Task DeleteAsync(Session session) {
        if (Pending?.SessionID == session.Id) throw new InvalidOperationException("这条会话还有待确认发送，不能删除。");
        await VerifyProfileAsync(); await Api.JsonAsync<System.Text.Json.JsonElement>($"sessions/{GatewayClient.Id(session.Id)}",HttpMethod.Delete);
        profile.Drafts.Remove(session.Id); Save(); if (Selected?.Id == session.Id) NewConversation(); await ReloadAsync();
    }
    public void Disconnect() { watch?.Cancel(); client?.Dispose(); client = null; Status = null; Selected = null; ++generation; state.Connection = null; Save(); scope = ""; profile = new(); Publish(); }
    public void Dispose() { watch?.Cancel(); watch?.Dispose(); client?.Dispose(); }
}
