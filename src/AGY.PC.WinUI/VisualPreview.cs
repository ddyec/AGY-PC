using System.Net;
using System.Text;
using System.Text.Json;
using AGY.PC.Core;

namespace AGY.PC.WinUI;

// Used only by --ui-check with an in-memory state store. Never connects to a
// server or reads user credentials. All displayed content is labelled example data.
internal sealed class VisualPreview : HttpMessageHandler {
    internal const string SessionID="11111111-1111-4111-8111-111111111111";
    internal static readonly Credentials Connection=new("https://preview.invalid",new string('p',48));
    private static readonly Session example=new(SessionID,"项目入口梳理 · 示例","/preview/project","preview-medium","plan",1,false,"","",1,
        [new("example-user","user","帮我梳理这个项目，并说明接下来从哪里开始。",[],[],[]),
         new("example-assistant","assistant","这是离线界面示例。\n\nsrc/ 保存应用代码，docs/ 保存项目说明。建议先阅读 README，再查看程序入口。\n\n```text\nproject/\n  src/\n  docs/\n  README.md\n```\n\n你可以继续 @ 一个文件夹，让 AGY 阅读相关内容。",[],[],[])],"preview","medium","review",[]);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if(request.RequestUri?.Host!="preview.invalid" || request.Method!=HttpMethod.Get)throw new InvalidOperationException("离线界面预览不允许联网或修改任务。");
        var path=request.RequestUri.AbsolutePath;object result;
        if(path.EndsWith("/status"))result=new GatewayStatus("离线界面预览",1,["/preview/project"],"preview-medium","offline-preview",new(true,true,true,true,true));
        else if(path.EndsWith("/models"))result=new ModelReply([new("preview-medium","示例模型")],[new("preview","示例模型",[new("preview-medium","medium")],["medium"],"medium")]);
        else if(path.EndsWith("/sessions"))result=new SessionsReply([example,example with{Id="22222222-2222-4222-8222-222222222222",Name="文档整理 · 示例",Modified=0}]);
        else if(path.EndsWith("/workspace"))result=new FileListing("/preview/project","",[new("src","/preview/project/src",true,0,1),new("docs","/preview/project/docs",true,0,1),new("README.md","/preview/project/README.md",false,2048,1),new("设计说明.pdf","/preview/project/设计说明.pdf",false,409600,1)]);
        else if(path.EndsWith("/events"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: "+JsonSerializer.Serialize(example,Wire.Json)+"\n\n",Encoding.UTF8,"text/event-stream")});
        else result=example;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result,Wire.Json),Encoding.UTF8,"application/json")});
    }
}
