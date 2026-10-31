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
            var sw=System.Diagnostics.Stopwatch.StartNew();
            var credentials=Credentials.Validate("https://agy.yectech.uk",File.ReadAllText(args[1]).Trim());
            using var api=new GatewayClient(credentials, network: new(true, "Custom", false, "https://edge.1molchuan.top/linuxdo"));
            var t0=sw.ElapsedMilliseconds;
            var status=await api.JsonAsync<GatewayStatus>("status");
            var t1=sw.ElapsedMilliseconds;
            var models=await api.JsonAsync<ModelReply>("models");
            var t2=sw.ElapsedMilliseconds;
            var sessions=await api.JsonAsync<SessionsReply>("sessions");
            var t3=sw.ElapsedMilliseconds;
            var bigSession=await api.JsonAsync<Session>("sessions/889032f1-1b14-4ed7-b9bf-868aecc0f73c");
            var t4=sw.ElapsedMilliseconds;
            var files=await api.JsonAsync<FileListing>("workspace");
            var t5=sw.ElapsedMilliseconds;
            var limits=await api.JsonAsync<LimitReply>("limits");
            var t6=sw.ElapsedMilliseconds;
            Console.WriteLine($"Live timings (via https://edge.1molchuan.top/linuxdo with SystemFallback=false): status={t1-t0}ms; models={t2-t1}ms; sessions={t3-t2}ms; bigSession({bigSession.Messages?.Length} msgs)={t4-t3}ms; workspace={t5-t4}ms; limits={t6-t5}ms");
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
        Test("DoH preferences persist before login; old state receives safe defaults",()=>{
            var store=new MemoryStore();using(var workspace=new Workspace(store))workspace.SetNetwork(new(false,"Google",false));
            using var restored=new Workspace(store);Assert(restored.Network==new NetworkSettings(false,"Google",false),"network persisted before profile");
            var old=JsonSerializer.Deserialize<AppState>("{}",Wire.Json)!;Assert(old.Network.UseDoh&&old.Network.SystemFallback,"migration defaults");
            Throws<InvalidOperationException>(()=>new NetworkSettings(Provider:"untrusted").Validate());
        });
        await Test("DoH queries A and AAAA without credentials, caches and preserves Unicode domains",async()=>{
            var fake=new DnsFake();using var resolver=new DohResolver(new("https://dns.example.test/query"),fake);
            var first=await resolver.ResolveAsync("例子.test");var second=await resolver.ResolveAsync("例子.test");
            Assert(first.Length==2&&second.Length==2&&fake.Calls==2,"dual stack cached");Assert(fake.SafeRequests&&fake.LastQuery!.Contains("xn--"),"no auth, IDN encoded");
        });
        await Test("DoH accepts one available family but refuses NXDOMAIN, empty and malformed data",async()=>{
            var fake=new DnsFake{FailV6=true};using(var resolver=new DohResolver(new("https://dns.example.test/query"),fake))Assert((await resolver.ResolveAsync("example.test")).Length==1,"IPv4 works without IPv6");
            foreach(var body in new[]{"{\"Status\":3}","{\"Status\":0,\"Answer\":[]}","invalid","{\"Status\":0,\"TC\":true}","{\"Status\":0,\"Answer\":[{\"type\":1,\"TTL\":60,\"data\":\"not-an-IP\"}]}",new string('x',65537)}){
                using var resolver=new DohResolver(new("https://dns.example.test/query"),new DnsFake{Body=body});await Throws<IOException>(()=>resolver.ResolveAsync("example.test"));
            }
        });
        await Test("DoH zero TTL does not cache; cancellation does not trigger fallback",async()=>{
            var fake=new DnsFake{Ttl=0};using var resolver=new DohResolver(new("https://dns.example.test/query"),fake);await resolver.ResolveAsync("example.test");await resolver.ResolveAsync("example.test");Assert(fake.Calls==4,"TTL zero queried again");
            using var cancel=new CancellationTokenSource();cancel.Cancel();await Throws<OperationCanceledException>(()=>resolver.ResolveAsync("other.test",cancel.Token));
        });
        Test("Proxy routing retains proxy DNS, including bypass rules",()=>{
            var destination=new Uri("https://gateway.test");Assert(!NetworkTransport.UsesProxy(destination,new WebProxy()),"direct");
            Assert(NetworkTransport.UsesProxy(destination,new WebProxy("http://proxy.test:8080")),"proxy owns lookup");
            var bypass=new WebProxy("http://proxy.test:8080"){BypassList=["gateway\\.test"]};Assert(!NetworkTransport.UsesProxy(destination,bypass),"proxy bypass direct");
        });
        await Test("Real DoH socket connection retains HTTP host and keeps bearer out of DNS",async()=>{
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();
            try{
                var port=((IPEndPoint)listener.LocalEndpoint).Port;var uri=new Uri($"http://gateway.test:{port}/v1/status");var fake=new DnsFake{Loopback=true};
                using var client=new HttpClient(NetworkTransport.Create(uri,new(SystemFallback:false),fake,new WebProxy())){Timeout=TimeSpan.FromSeconds(5)};
                var server=Task.Run(async()=>{using var connection=await listener.AcceptTcpClientAsync();using var stream=connection.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);var lines=new List<string>();string? line;while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))lines.Add(line);await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"));return string.Join("\n",lines);});
                using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.Authorization=new("Bearer","gateway-only-secret");using var response=await client.SendAsync(request);
                var headers=await server;Assert(response.IsSuccessStatusCode&&headers.Contains($"Host: gateway.test:{port}")&&headers.Contains("Bearer gateway-only-secret"),"original host and auth reach gateway");Assert(fake.Calls==2&&fake.SafeRequests,"DoH isolated");
            }finally{listener.Stop();}
        });
        await Test("DoH HTTPS retains original SNI and refuses an untrusted server certificate",async()=>{
            using var rsa=System.Security.Cryptography.RSA.Create(2048);
            var certificateRequest=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=gateway.test",rsa,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var names=new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();names.AddDnsName("gateway.test");certificateRequest.CertificateExtensions.Add(names.Build());
            using var certificate=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();string? sni=null;
            try{
                var port=((IPEndPoint)listener.LocalEndpoint).Port;var uri=new Uri($"https://gateway.test:{port}/v1/status");
                var server=Task.Run(async()=>{using var connection=await listener.AcceptTcpClientAsync();using var ssl=new System.Net.Security.SslStream(connection.GetStream());try{await ssl.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions{ServerCertificateSelectionCallback=(_,name)=>{sni=name;return certificate;}});}catch(Exception e)when(e is System.Security.Authentication.AuthenticationException or IOException){} });
                using var client=new HttpClient(NetworkTransport.Create(uri,new(SystemFallback:false),new DnsFake{Loopback=true},new WebProxy())){Timeout=TimeSpan.FromSeconds(5)};
                await Throws<HttpRequestException>(()=>client.GetAsync(uri));await server.WaitAsync(TimeSpan.FromSeconds(5));Assert(sni=="gateway.test","TLS SNI remains original domain; no trust bypass");
            }finally{listener.Stop();}
        });
        await Test("HTTPS proxy receives the original CONNECT hostname without a DoH lookup",async()=>{
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();
            try{
                var port=((IPEndPoint)listener.LocalEndpoint).Port;var uri=new Uri("https://gateway.test/v1/status");var dns=new DnsFake();
                var server=Task.Run(async()=>{using var connection=await listener.AcceptTcpClientAsync();using var stream=connection.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);var lines=new List<string>();string? line;while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))lines.Add(line);await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));return string.Join("\n",lines);});
                using var client=new HttpClient(NetworkTransport.Create(uri,new(SystemFallback:false),dns,new WebProxy($"http://127.0.0.1:{port}"))){Timeout=TimeSpan.FromSeconds(5)};
                await Throws<HttpRequestException>(()=>client.GetAsync(uri));var headers=await server.WaitAsync(TimeSpan.FromSeconds(5));Assert(headers.StartsWith("CONNECT gateway.test:443 ")&&dns.Calls==0,"proxy owns destination DNS");
            }finally{listener.Stop();}
        });
        Test("Quota limits deserialize remaining_fraction and reset_time and localize titles",()=>{
            var json="{\"fetchedAt\":1730000000,\"source\":\"agy_usage_json\",\"groups\":[{\"name\":\"Gemini Models\",\"description\":\"Quota usage is shared across all Gemini models\",\"buckets\":[{\"id\":\"gemini-weekly\",\"name\":\"Weekly Limit Remaining\",\"window\":\"weekly\",\"remaining_fraction\":0.9655538,\"reset_time\":\"2026-11-05T12:40:20Z\"},{\"id\":\"gemini-5h\",\"name\":\"Five Hour Limit Remaining\",\"window\":\"5h\",\"remaining_fraction\":1.0,\"reset_time\":\"2026-10-31T17:40:20Z\"}]}]}";
            var reply=JsonSerializer.Deserialize<LimitReply>(json,Wire.Json)!;
            var group=reply.Groups[0];
            Assert(group.DisplayName=="Gemini 系列模型"&&group.Buckets.Length==2,"group parsed");
            Assert(group.Buckets[0].EffectiveRemaining is >0.96 and <0.97 && group.Buckets[0].EffectiveReset=="2026-11-05T12:40:20Z" && group.Buckets[0].DisplayTitle=="每周可用额度","weekly bucket");
            Assert(group.Buckets[1].EffectiveRemaining==1.0 && group.Buckets[1].DisplayTitle=="5 小时可用额度","5h bucket");
        });
        Test("Display text cleaning strips U+FFFC object replacement characters",()=>{
            var s=new Session(Sid,"http://github.com/ddyec/AGY-PC \uFFFC\uFFFC修一下","/work","gemini-low","plan",0,false,"","",1,[],"gemini","low","review",[]);
            Assert(!s.DisplayName.Contains('\uFFFC')&&s.ToString()=="http://github.com/ddyec/AGY-PC 修一下","cleaned session title");
        });
        await Test("Conversation queue holds follow-up messages while running and drains in order",async()=>{
            var store=new MemoryStore();var fake=new Fake{StartRunning=true,RunOnSubmit=true};
            using var work=new Workspace(store,_=>new(Creds,fake));
            await work.ConnectAsync(Creds);await work.SelectAsync(Sid);
            Assert(work.Selected!.Running,"initially running");
            work.UpdateDraft("第一条排队指令");await work.EnqueueAsync();
            work.UpdateDraft("第二条排队指令");await work.EnqueueAsync();
            Assert(work.Queue.Count==2&&work.Draft.Text==""&&fake.Submissions==0,"queued while running");
            var secondId=work.Queue[1].Id;work.EditQueued(secondId);
            Assert(work.Queue.Count==1&&work.Draft.Text=="第二条排队指令","edit pops back to draft");
            await work.EnqueueAsync();Assert(work.Queue.Count==2,"re-queued");
            fake.SetRunning(false);await work.SelectAsync(Sid);
            await work.DrainQueueAsync();
            Assert(fake.Submissions==1&&work.Queue.Count==1,"first queued drained");
        });
        await Test("Immediate steer stops active run and dispatches guiding message immediately",async()=>{
            var store=new MemoryStore();var fake=new Fake{StartRunning=true};
            using var work=new Workspace(store,_=>new(Creds,fake));
            await work.ConnectAsync(Creds);await work.SelectAsync(Sid);
            Assert(work.Selected!.Running,"running before steer");
            work.UpdateDraft("立即改变方向：直接修改 UI");
            await work.SteerNowAsync();
            Assert(fake.Stops==1&&fake.Submissions==1&&work.Pending is null&&work.Draft.Text=="","stopped and steered in one step");
        });
        await Test("Free specification of custom model ID, workspace path, custom DoH URL, FlClash proxy and custom skills persist across restarts",async()=>{
            var store=new MemoryStore();var fake=new Fake();
            using(var work=new Workspace(store,_=>new(Creds,fake))){
                work.SetNetwork(new(true,"Custom",true,"https://edge.1molchuan.top/linuxdo","http://127.0.0.1:7890"));
                await work.ConnectAsync(Creds);
                await work.ConfigureAsync(new("claude-sonnet-4-5-custom","xhigh","turbo","accept-edits","subproject/app"));
                Assert(work.Settings.Model=="claude-sonnet-4-5-custom"&&work.Settings.Effort=="xhigh"&&work.Settings.Workspace=="/work/subproject/app","custom model and relative workspace normalized");
                work.SaveCustomSkill("/review-ui","UI 审查技能","请审查当前 UI 设计：",".agents/skills/ui/SKILL.md");
                Assert(work.CustomSkills.Count==1,"custom skill saved");
                work.ApplySkill(work.CustomSkills[0]);
                Assert(work.Draft.Text.Contains("请审查当前 UI 设计：")&&work.Draft.References.Any(r=>r.Path=="/work/subproject/app/.agents/skills/ui/SKILL.md"),"skill applied with reference");
            }
            using(var work=new Workspace(store,_=>new(Creds,fake))){
                Assert(work.Network.CustomUrl=="https://edge.1molchuan.top/linuxdo"&&work.Network.ProxyUrl=="http://127.0.0.1:7890","custom DoH URL and FlClash proxy persisted");
                work.SetNetwork(new(false,"Cloudflare",true,null,"direct"));
                Assert(work.Network.ProxyUrl=="direct","direct proxy override accepted");
                await work.ConnectAsync(Creds);
                Assert(work.Settings.Model=="claude-sonnet-4-5-custom"&&work.Settings.Effort=="xhigh"&&work.Settings.ApprovalMode=="turbo"&&work.CustomSkills.Count==1,"custom model and skills remembered across restart");
            }
        });
        await Test("RFC 8484 wireformat DoH query and compressed response parsing work for standard browser DoH endpoints",async()=>{
            var query=DohResolver.BuildDnsWireQuery("agy.yectech.uk",1);
            Assert(query.Length>16&&query[2]==1&&query[5]==1,"valid RFC 8484 wire query packet");
            var responsePacket=new byte[]{
                0x00,0x00,0x81,0x80,0x00,0x01,0x00,0x02,0x00,0x00,0x00,0x00,
                0x03,(byte)'a',(byte)'g',(byte)'y',0x07,(byte)'y',(byte)'e',(byte)'c',(byte)'t',(byte)'e',(byte)'c',(byte)'h',0x02,(byte)'u',(byte)'k',0x00,
                0x00,0x01,0x00,0x01,
                0xC0,0x0C,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0x48,0x00,0x04,162,159,150,47,
                0xC0,0x0C,0x00,0x01,0x00,0x01,0x00,0x00,0x00,0x3C,0x00,0x04,162,159,139,152
            };
            var parsed=DohResolver.ParseDnsWireResponse(responsePacket,1);
            Assert(parsed.Success&&parsed.Addresses.Length==2&&parsed.Addresses[0].ToString()=="162.159.150.47"&&parsed.Ttl==60,"parsed compressed wireformat A records");
            await Task.CompletedTask;
        });
        Console.WriteLine($"{passed} meaningful checks passed.");
    }
    sealed class DnsFake:HttpMessageHandler {
        public int Calls,Ttl=60;public bool FailV6,Loopback;public bool SafeRequests=true;public string? Body,LastQuery;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            ct.ThrowIfCancellationRequested();Interlocked.Increment(ref Calls);LastQuery=request.RequestUri!.Query;
            SafeRequests &= request.Headers.Authorization is null && !request.Headers.Contains("Cookie") && request.Headers.Accept.ToString()=="application/dns-json";
            var type=request.RequestUri.Query.EndsWith("type=28")?28:1;
            if(FailV6&&type==28)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var ip=type==1?(Loopback?"127.0.0.1":"192.0.2.1"):"2001:db8::1";
            var body=Body??JsonSerializer.Serialize(new{Status=0,Answer=new[]{new{type,TTL=Ttl,data=ip}}});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/dns-json")});
        }
    }
    sealed class MemoryStore:IStateStore {
        private byte[]? data;
        public bool Fail;
        public AppState Load()=>data is null?new():JsonSerializer.Deserialize<AppState>(data,Wire.Json)!;
        public void Save(AppState value){if(Fail)throw new IOException("disk full");data=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);}
    }
    sealed class Fake:HttpMessageHandler {
        public static readonly byte[] Binary=[0,255,1,127,0];
        public int Error,Calls,Submissions,Stops;public string Profile="A";public bool LoseBeforeAdmission,LoseAfterAdmission,OldTurbo,StartRunning,RunOnSubmit;
        public byte[]? UploadBytes;public string? LastAuthorization,LastBody,LastRequestID;public string[] LastReferences=[];
        private Session snapshot=new(Sid,"old history","/work","gemini-low","plan",0,false,"","",1,[],"gemini","low","review",[]);
        public void SetRunning(bool running)=>snapshot=snapshot with{Running=running,Revision=snapshot.Revision+1};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            if(StartRunning){StartRunning=false;snapshot=snapshot with{Running=true};}
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
            else if(path.EndsWith("/stop")){Stops++;snapshot=snapshot with{Running=false,Revision=snapshot.Revision+1};value=snapshot;}
            else if(path.EndsWith("/events")){var content=new StringContent("data: "+JsonSerializer.Serialize(snapshot,Wire.Json)+"\n\n",Encoding.UTF8,"text/event-stream");return new(HttpStatusCode.OK){Content=content};}
            else if(path.EndsWith("/messages")){
                LastBody=await request.Content!.ReadAsStringAsync(ct);using var doc=JsonDocument.Parse(LastBody);LastRequestID=doc.RootElement.GetProperty("requestID").GetString();LastReferences=doc.RootElement.TryGetProperty("references",out var refs)?refs.Deserialize<string[]>()!:[];
                if(LoseBeforeAdmission)throw new HttpRequestException("disconnected before admission");
                if(snapshot.Messages?.Any(m=>m.Id==LastRequestID)!=true){Submissions++;snapshot=snapshot with{Running=RunOnSubmit,Revision=snapshot.Revision+1,Messages=[new(LastRequestID!,"user",doc.RootElement.GetProperty("text").GetString()??"",[],[],[])]};}
                if(LoseAfterAdmission){LoseAfterAdmission=false;throw new HttpRequestException("lost response");}value=snapshot;
            }else if(path.EndsWith("/sessions")){value=request.Method==HttpMethod.Post?snapshot:new SessionsReply([snapshot]);}
            else value=OldTurbo?snapshot with{ApprovalMode="turbo",Mode="accept-edits"}:snapshot;
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};
        }
        protected override void Dispose(bool disposing){/* Fixtures can simulate the same server across client restarts. */}
    }
}
