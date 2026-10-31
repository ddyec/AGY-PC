using System.Net;
using System.Text;
using System.Text.Json;
using AGY.PC.Core;

static class Program {
    static int passed;
    static readonly string Sid="11111111-1111-4111-8111-111111111111";
    static readonly Credentials Creds=Credentials.Validate("https://example.test",new string('k',48));
    static void Assert(bool value,string description){if(!value)throw new Exception(description);}
    static void Test(string name,Action action){action();Console.WriteLine("PASS "+name);passed++;}
    static async Task Test(string name,Func<Task> action){await action();Console.WriteLine("PASS "+name);passed++;}
    static async Task Throws<T>(Func<Task> action) where T:Exception{try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static async Task Main(string[] args) {
        if(args.Length==2 && args[0]=="--live-readonly-key-file") {
            var credentials=Credentials.Validate("https://agy.yectech.uk",File.ReadAllText(args[1]).Trim());
            using var api=new GatewayClient(credentials);
            var status=await api.JsonAsync<GatewayStatus>("status");
            var models=await api.JsonAsync<ModelReply>("models");
            var limits=await api.JsonAsync<LimitReply>("limits");
            var files=await api.JsonAsync<FileListing>("workspace");
            Console.WriteLine($"Live read-only: status OK; models={models.Models.Length}; families={models.Families.Length}; quotaGroups={limits.Groups.Length}; fileBrowser={status.Capabilities.FileBrowser}; entries={files.Entries.Length}");
            return;
        }
        Test("HTTPS credentials reject alternate origins/paths and header injection",()=>{
            foreach(var url in new[]{"http://example.test","https://name:pass@example.test","https://example.test/v1","https://example.test/?key=x"})Throws<InvalidOperationException>(()=>Credentials.Validate(url,new string('k',48)));
            Throws<InvalidOperationException>(()=>Credentials.Validate("https://example.test",new string('k',48)+"\n"));Assert(Creds.Address=="https://example.test","canonical origin");
        });
        Test("Windows filenames and relative whole-folder labels",()=>{
            Assert(FileRules.SafeName("../../CON.txt")=="_CON.txt","reserved file");Assert(FileRules.SafeName("中文 合同.pdf")=="中文 合同.pdf","Unicode");
            Assert(FileRules.Label("workspace","项目","src",true)=="项目/src/","folder");Assert(FileRules.Label("uploads","","图片.png",false)=="上传/图片.png","upload label");
            Assert(!FileRules.Within("/work-other","/work")&&!FileRules.Within("/work/../etc","/work"),"boundaries");
        });
        Test("Model/effort and workspace defaults validate against live catalog",()=>{
            var family=new ModelFamily("gemini","Gemini",[new("gemini-low","low"),new("gemini-high","high")],["low","high"],"low");
            var settings=new TaskSettings("gemini","high","turbo","accept-edits","/work/project");Assert(settings.Validate([family],["/work"],"gemini-low")==settings,"retained");
            var fallback=(settings with{Model="removed",Effort="max",Workspace="/work-other",ApprovalMode="bad"}).Validate([family],["/work"],"gemini-low");Assert(fallback.Model=="gemini"&&fallback.Effort=="low"&&fallback.Workspace=="/work"&&fallback.ApprovalMode=="review","fallback");
        });
        Test("Durable encrypted envelope roundtrip preserves pending send and account scopes",()=>{
            var folder=Path.Combine(Path.GetTempPath(),"agy-pc-test-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try{
                byte[] Protect(byte[] bytes)=>bytes.Select(b=>(byte)(b^0xA5)).ToArray();var path=Path.Combine(folder,"state.bin");var store=new ProtectedStateStore(path,Protect,Protect);
                var state=new AppState{Connection=Creds};state.Profiles["origin#A"]=new(){Pending=new(Sid,Guid.NewGuid().ToString(),"不要重复发送",[],[new("/work/目录","目录/",true)])};store.Save(state);
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(Creds.Token),"not plaintext");var restored=store.Load();Assert(restored.Profiles["origin#A"].Pending?.Text=="不要重复发送"&&!restored.Profiles.ContainsKey("origin#B"),"scoped pending");
                var before=File.ReadAllBytes(path);var failing=new ProtectedStateStore(path,_=>throw new IOException("failure"),Protect);Throws<IOException>(()=>failing.Save(new()));Assert(before.SequenceEqual(File.ReadAllBytes(path)),"failed save preserves old");File.WriteAllText(path,"broken");Throws<InvalidDataException>(()=>store.Load());
            }finally{Directory.Delete(folder,true);}
        });
        await Test("SSE preserves UTF-8, CRLF, comments and multi-data boundaries",async()=>{
            var data=Encoding.UTF8.GetBytes(": ping\r\ndata: 中文\r\ndata: second\r\n\r\ndata: third\n\n");var results=new List<string>();await foreach(var value in Sse.ReadAsync(new MemoryStream(data)))results.Add(value);Assert(results.SequenceEqual(new[]{"中文\nsecond","third"}),"SSE framing");
        });
        await Test("SSE rejects incomplete event rather than dispatching partial snapshot",async()=>{
            var results=new List<string>();await foreach(var value in Sse.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("data: partial"))))results.Add(value);Assert(results.Count==0,"partial");
        });
        await Test("Gateway request authorization and JSON reference escaping",async()=>{
            var handler=new Fake();using var api=new GatewayClient(Creds,handler);await api.JsonAsync<GatewayStatus>("status");Assert(handler.LastAuthorization=="Bearer "+Creds.Token,"auth");
            await api.JsonAsync<Session>($"sessions/{Sid}/messages",HttpMethod.Post,new{text="inspect",requestID=Guid.NewGuid().ToString(),references=new[]{"/work/中文 空格\nquote\""}});Assert(handler.LastBody!.Contains("references"),"refs");
        });
        await Test("502 and redirects are errors, not HTML JSON or alternate-origin retries",async()=>{
            var fake=new Fake{Error=502};using var api=new GatewayClient(Creds,fake);await Throws<GatewayException>(()=>api.JsonAsync<GatewayStatus>("status"));fake.Error=302;await Throws<GatewayException>(()=>api.JsonAsync<GatewayStatus>("status"));Assert(fake.Calls==2,"no redirect retry");
        });
        await Test("Binary download is exact; failure leaves original destination",async()=>{
            var fake=new Fake();using var api=new GatewayClient(Creds,fake);var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".bin");
            try{await api.DownloadAsync(Sid,"/work/中文.bin",path);Assert(File.ReadAllBytes(path).SequenceEqual(Fake.Binary),"binary");fake.Error=403;await Throws<GatewayException>(()=>api.DownloadAsync(Sid,"/etc/passwd",path));Assert(File.ReadAllBytes(path).SequenceEqual(Fake.Binary),"preserve original");}finally{File.Delete(path);}
        });
        await Test("Upload preserves original filename and bytes; oversized files never leave the client",async()=>{
            var folder=Path.Combine(Path.GetTempPath(),"agy-upload-"+Guid.NewGuid());Directory.CreateDirectory(folder);var local=Path.Combine(folder,"中文 空格.png");
            try{File.WriteAllBytes(local,Fake.Binary);var fake=new Fake();using var api=new GatewayClient(Creds,fake);var attachment=await api.UploadAsync(Sid,local);Assert(attachment.Name=="中文 空格.png"&&fake.UploadBytes!.SequenceEqual(Fake.Binary),"upload exact");
                using(var file=new FileStream(local,FileMode.Open,FileAccess.Write)){file.SetLength(FileRules.MaxFile+1);}var calls=fake.Calls;await Throws<InvalidOperationException>(()=>api.UploadAsync(Sid,local));Assert(fake.Calls==calls,"oversize not sent");
            }finally{Directory.Delete(folder,true);}
        });
        await Test("Approval validates session ownership and sends one explicit decision",async()=>{
            var fake=new Fake();using var work=new Workspace(new MemoryStore(),_=>new(Creds,fake));await work.ConnectAsync(Creds);await work.SelectAsync(Sid);
            var request=new Approval(Guid.NewGuid().ToString(),Sid,"read_file",JsonSerializer.SerializeToElement(new{path="/work/file"}),0,9999999999);
            await Throws<InvalidOperationException>(()=>work.AnswerAsync(request with{SessionID=Guid.NewGuid().ToString()},true));await work.AnswerAsync(request,false);Assert(fake.LastBody!.Contains("deny"),"explicit deny");
        });
        await Test("Settings survive restart; another server/account does not inherit Turbo",async()=>{
            var store=new MemoryStore();var handler=new Fake();using(var work=new Workspace(store,_=>new(Creds,handler))){await work.ConnectAsync(Creds);await work.ConfigureAsync(new("gemini","high","turbo","accept-edits","/work/project"));work.SetPresentation("结论优先");}
            using(var work=new Workspace(store,_=>new(Creds,new Fake()))){await work.ConnectAsync(Creds);Assert(work.Settings.ApprovalMode=="turbo"&&work.Settings.Effort=="high"&&work.Settings.Workspace=="/work/project"&&work.Presentation=="结论优先","remembered");}
            using(var work=new Workspace(store,_=>new(Creds,new Fake{Profile="B"}))){await work.ConnectAsync(Creds);Assert(work.Settings.ApprovalMode=="review","new account safe default");}
        });
        await Test("Selecting old history never rewrites new-conversation preferences",async()=>{
            var store=new MemoryStore();using var work=new Workspace(store,_=>new(Creds,new Fake()));await work.ConnectAsync(Creds);await work.ConfigureAsync(new("gemini","high","turbo","accept-edits","/work/project"));await work.SelectAsync(Sid);Assert(work.Settings.ApprovalMode=="review","old session");work.NewConversation();Assert(work.Settings.ApprovalMode=="turbo"&&work.Settings.Effort=="high","new conversation prefs");
        });
        await Test("Unmodified defaults never inherit permissions from a historical session",async()=>{
            using var work=new Workspace(new MemoryStore(),_=>new(Creds,new Fake{OldTurbo=true}));await work.ConnectAsync(Creds);await work.SelectAsync(Sid);Assert(work.Settings.ApprovalMode=="turbo","historical config");work.NewConversation();Assert(work.Settings.ApprovalMode=="review"&&work.Settings.Mode=="plan","fresh defaults");
        });
        await Test("Whole-folder reference persists as one path, not expanded contents",async()=>{
            var store=new MemoryStore();var fake=new Fake();using var work=new Workspace(store,_=>new(Creds,fake));await work.ConnectAsync(Creds);work.AddReference(new("/work/project","project/",true));await work.SendAsync();Assert(fake.Submissions==1&&fake.LastReferences.SequenceEqual(new[]{"/work/project"}),"folder path");Assert(work.Pending is null&&work.Draft.References.Length==0,"reconciled");
        });
        await Test("Lost response after admission reconciles on restart without resending",async()=>{
            var store=new MemoryStore();var fake=new Fake{LoseAfterAdmission=true};using(var work=new Workspace(store,_=>new(Creds,fake))){await work.ConnectAsync(Creds);work.UpdateDraft("do once");await Throws<HttpRequestException>(work.SendAsync);Assert(work.Pending is not null,"pending retained");}
            using(var work=new Workspace(store,_=>new(Creds,fake))){await work.ConnectAsync(Creds);Assert(work.Pending is null&&fake.Submissions==1,"no automatic resubmit");}
        });
        await Test("Unadmitted send retries same request ID only on explicit action",async()=>{
            var store=new MemoryStore();var fake=new Fake{LoseBeforeAdmission=true};using var work=new Workspace(store,_=>new(Creds,fake));await work.ConnectAsync(Creds);work.UpdateDraft("do once");await Throws<HttpRequestException>(work.SendAsync);var id=work.Pending!.RequestID;fake.LoseBeforeAdmission=false;await work.SubmitPendingAsync();Assert(fake.LastRequestID==id&&fake.Submissions==1&&work.Pending is null,"same ID");
        });
        await Test("Persistence failure prevents sending even on retry",async()=>{
            var store=new MemoryStore();var fake=new Fake();using var work=new Workspace(store,_=>new(Creds,fake));await work.ConnectAsync(Creds);work.UpdateDraft("don't lose ID");await work.EnsureSessionAsync();store.Fail=true;await Throws<IOException>(work.SendAsync);await Throws<IOException>(work.SubmitPendingAsync);Assert(fake.Submissions==0,"nothing sent");
        });
        await Test("Profile changes block mutation and never forward pending to new account",async()=>{
            var fake=new Fake();using var work=new Workspace(new MemoryStore(),_=>new(Creds,fake));await work.ConnectAsync(Creds);work.UpdateDraft("secret task");fake.Profile="different";await Throws<InvalidOperationException>(work.SendAsync);Assert(fake.Submissions==0&&!work.Connected,"blocked");
        });
        Test("Project snapshots extract into fresh folders and preserve Unicode bytes",()=>{
            var directory=Path.Combine(Path.GetTempPath(),"agy-snapshot-"+Guid.NewGuid());Directory.CreateDirectory(directory);
            try{var zip=Path.Combine(directory,"source.zip");using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create)){var item=archive.CreateEntry("项目/中文 空格.bin");using var file=item.Open();file.Write(Fake.Binary);}
                var target=Path.Combine(directory,"target");HybridBuild.ExtractSnapshot(zip,target);Assert(File.ReadAllBytes(Path.Combine(target,"项目/中文 空格.bin")).SequenceEqual(Fake.Binary),"snapshot bytes");Throws<InvalidOperationException>(()=>HybridBuild.ExtractSnapshot(zip,target));
            }finally{Directory.Delete(directory,true);}
        });
        Test("Snapshot extraction refuses traversal, symlinks, case conflicts and device files",()=>{
            foreach(var names in new[]{new[]{"../escape"},new[]{"CON.txt"},new[]{"a.txt","A.txt"},new[]{"link"}}){
                var directory=Path.Combine(Path.GetTempPath(),"agy-hostile-"+Guid.NewGuid());Directory.CreateDirectory(directory);
                try{var zip=Path.Combine(directory,"source.zip");using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create)){foreach(var name in names){var entry=archive.CreateEntry(name);if(name=="link")entry.ExternalAttributes=unchecked((int)0xA1FF0000);using var file=entry.Open();file.WriteByte(1);}}
                    Throws<InvalidDataException>(()=>HybridBuild.ExtractSnapshot(zip,Path.Combine(directory,"target")));Assert(!File.Exists(Path.Combine(directory,"escape")),"no escape");
                }finally{Directory.Delete(directory,true);}
            }
        });
        Test("Hybrid targets cannot silently execute Windows or server commands on ARM Linux",()=>{
            Throws<InvalidOperationException>(()=>HybridBuild.Prepare(new(BuildTarget.Server,"/work","npm run build")));
            if(!OperatingSystem.IsWindows())Throws<PlatformNotSupportedException>(()=>HybridBuild.Prepare(new(BuildTarget.Windows,"/work","dotnet build")));
            else{var plan=HybridBuild.Prepare(new(BuildTarget.WslLinuxX64,"/home/user/project","make","Ubuntu"));Assert(plan.FileName=="wsl.exe"&&plan.ArgumentList.Last().Contains("x86_64")&&plan.ArgumentList.Contains("Ubuntu"),"WSL target and architecture guard");}
        });
        Test("Startup preflight detects missing PRI, XBF and framework resources",()=>{
            var folder=Path.Combine(Path.GetTempPath(),"agy-startup-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            try{Assert(StartupChecks.MissingFiles(folder).Contains("App.xbf"),"XBF absent");
                foreach(var relative in StartupChecks.RequiredFiles){var path=Path.Combine(folder,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,[1]);}
                Assert(StartupChecks.MissingFiles(folder).Length==0,"complete payload");File.Delete(Path.Combine(folder,"AGY-PC.pri"));Assert(StartupChecks.MissingFiles(folder).SequenceEqual(new[]{"AGY-PC.pri"}),"PRI specifically detected");
            }finally{Directory.Delete(folder,true);}
        });
        Test("Native Windows crash exit codes retain their original hexadecimal identity",()=>{
            var result=StartupChecks.ExitDescription(-1073741189);Assert(result.Contains("0xC000027B")&&result.Contains("原始异常"),"stowed exception is not misdiagnosed as compilation");
        });
        Console.WriteLine($"{passed} meaningful checks passed.");
    }
    sealed class MemoryStore:IStateStore {
        private byte[]? data;
        public bool Fail;
        public AppState Load()=>data is null?new():JsonSerializer.Deserialize<AppState>(data,Wire.Json)!;
        public void Save(AppState value){if(Fail)throw new IOException("disk full");data=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);}
    }
    sealed class Fake:HttpMessageHandler {
        public static readonly byte[] Binary=[0,255,1,127,0];
        public int Error,Calls,Submissions;public string Profile="A";public bool LoseBeforeAdmission,LoseAfterAdmission,OldTurbo;
        public byte[]? UploadBytes;public string? LastAuthorization,LastBody,LastRequestID;public string[] LastReferences=[];
        private Session snapshot=new(Sid,"old history","/work","gemini-low","plan",0,false,"","",1,[],"gemini","low","review",[]);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            Calls++;LastAuthorization=request.Headers.Authorization?.ToString();var path=request.RequestUri!.AbsolutePath;
            if(Error!=0)return new((HttpStatusCode)Error){Content=new StringContent("failure")};
            object value;
            if(path.EndsWith("/status"))value=new GatewayStatus("AGY",1,["/work"],"gemini-low",Profile,new(true,true,true,true,true));
            else if(path.EndsWith("/models"))value=new ModelReply([new("gemini-low","Gemini (Low)")],[new("gemini","Gemini",[new("gemini-low","low"),new("gemini-high","high")],["low","high"],"low")]);
            else if(path.EndsWith("/attachments")){
                UploadBytes=await request.Content!.ReadAsByteArrayAsync(ct);var name=Uri.UnescapeDataString(request.RequestUri.Query.Split("name=")[1]);value=new Attachment(Guid.NewGuid().ToString(),name,"image/png",UploadBytes.Length,"/work/.agy-uploads/"+Sid+"/file");
            }
            else if(path.Contains("/approvals/")){LastBody=await request.Content!.ReadAsStringAsync(ct);value=snapshot;}
            else if(path.EndsWith("/workspace"))value=new FileListing("/work/project","",[]);
            else if(path.EndsWith("/files"))return new(HttpStatusCode.OK){Content=new ByteArrayContent(Binary)};
            else if(path.EndsWith("/events")){var content=new StringContent("data: "+JsonSerializer.Serialize(snapshot,Wire.Json)+"\n\n",Encoding.UTF8,"text/event-stream");return new(HttpStatusCode.OK){Content=content};}
            else if(path.EndsWith("/messages")){
                LastBody=await request.Content!.ReadAsStringAsync(ct);using var doc=JsonDocument.Parse(LastBody);LastRequestID=doc.RootElement.GetProperty("requestID").GetString();LastReferences=doc.RootElement.TryGetProperty("references",out var refs)?refs.Deserialize<string[]>()!:[];
                if(LoseBeforeAdmission)throw new HttpRequestException("disconnected before admission");
                if(snapshot.Messages?.Any(m=>m.Id==LastRequestID)!=true){Submissions++;snapshot=snapshot with{Revision=snapshot.Revision+1,Messages=[new(LastRequestID!,"user",doc.RootElement.GetProperty("text").GetString()??"",[],[],[])]};}
                if(LoseAfterAdmission){LoseAfterAdmission=false;throw new HttpRequestException("lost response");}value=snapshot;
            }else if(path.EndsWith("/sessions")){value=request.Method==HttpMethod.Post?snapshot:new SessionsReply([snapshot]);}
            else value=OldTurbo?snapshot with{ApprovalMode="turbo",Mode="accept-edits"}:snapshot;
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};
        }
        protected override void Dispose(bool disposing){/* Fixtures can simulate the same server across client restarts. */}
    }
}
