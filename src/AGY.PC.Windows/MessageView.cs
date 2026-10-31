using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AGY.PC.Core;

namespace AGY.PC.Windows;
internal static class MessageView {
    private static readonly Regex Links=new(@"\[([^\]\n]{1,1024})\]\(([^\)\n]{1,8192})\)",RegexOptions.None,TimeSpan.FromSeconds(1));
    private static readonly Dictionary<string,bool> Expansion=[];
    private static readonly Dictionary<string,bool> Wrapping=[];
    public static UIElement Build(ChatMessage message,string cwd,Action<string,string> download,Action<string> folder,string presentation="原版") {
        var panel=new StackPanel();var title=Theme.Text(message.Role=="user"?"你":"AGY",13,Theme.Accent);panel.Children.Add(title);
        var process=new StackPanel();
        foreach(var step in message.Steps??[]){if(step.Text==message.Text||step.Text.Length==0)continue;process.Children.Add(Rich(step.Text,message.Id+"-step-"+step.Index,cwd,download,folder));}
        foreach(var tool in message.Tools??[]) {
            var key=message.Id+"-"+tool.Id;
            var body=Theme.Stack(Selectable(tool.Parameters.ToString()),Selectable(tool.Output));
            var expander=new Expander{Header=tool.Name+" · "+tool.State+(tool.IsError==true?" · 出错":""),Foreground=Theme.Muted,Content=body,Margin=new Thickness(0,5,0,5),IsExpanded=Expansion.GetValueOrDefault(key)};
            expander.Expanded+=(_,_)=>Expansion[key]=true;expander.Collapsed+=(_,_)=>Expansion[key]=false;process.Children.Add(expander);
        }
        var final=Rich(message.Text,message.Id,cwd,download,folder);
        if(presentation=="结论优先") {panel.Children.Add(final);if(process.Children.Count>0)panel.Children.Add(new Expander{Header="查看过程与工具",Foreground=Theme.Muted,Content=process});}
        else if(presentation=="步骤摘要") {if(process.Children.Count>0)panel.Children.Add(new Expander{Header=$"{process.Children.Count} 个步骤 / 工具（展开查看原文）",Foreground=Theme.Muted,Content=process});panel.Children.Add(final);}
        else {panel.Children.Add(process);panel.Children.Add(final);}
        foreach(var attachment in message.Attachments??[]) {
            if(attachment.Path is not {} path)continue;
            panel.Children.Add(Theme.Button((attachment.Mime=="inode/directory"?"📁 @ ":"↓ ")+attachment.Name,()=>{if(attachment.Mime=="inode/directory")folder(path);else download(path,attachment.Name);}));
        }
        panel.Children.Add(Theme.Button("复制消息",()=>Clipboard.SetText(message.Text??"")));
        var card=Theme.Card(panel);if(message.Role=="user")card.Background=Theme.Brush("#202D43");return card;
    }
    private static TextBox Selectable(string text)=>new(){Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0),FontFamily=new FontFamily("Cascadia Code, Consolas"),Margin=new Thickness(0,6,0,6)};
    private static UIElement Rich(string text,string key,string cwd,Action<string,string> download,Action<string> folder) {
        var panel=new StackPanel();var blocks=text.Split("```");
        for(int i=0;i<blocks.Length;i++) {
            var part=blocks[i];if(part.Length==0)continue;
            if(i%2==1) {
                var newline=part.IndexOf('\n');var language=newline>=0?part[..newline]:"code";var code=newline>=0?part[(newline+1)..]:part;
                var id=key+"-code-"+i;var box=Selectable(code.TrimEnd('\r','\n'));box.Background=Theme.Background;
                box.TextWrapping=Wrapping.GetValueOrDefault(id)?TextWrapping.Wrap:TextWrapping.NoWrap;box.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
                var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(Theme.Text(language,12,Theme.Muted));controls.Children.Add(Theme.Button("复制代码",()=>Clipboard.SetText(code)));
                controls.Children.Add(Theme.Button("切换换行",()=>{Wrapping[id]=!Wrapping.GetValueOrDefault(id);box.TextWrapping=Wrapping[id]?TextWrapping.Wrap:TextWrapping.NoWrap;}));
                panel.Children.Add(Theme.Card(Theme.Stack(controls,box),new Thickness(12)));continue;
            }
            var offset=0;
            foreach(Match match in Links.Matches(part)) {
                if(match.Index>offset)panel.Children.Add(Selectable(part[offset..match.Index]));
                var label=match.Groups[1].Value;var link=match.Groups[2].Value;
                panel.Children.Add(Theme.Button(label,()=>Open(link,label,cwd,download,folder)));offset=match.Index+match.Length;
            }
            if(offset<part.Length)panel.Children.Add(Selectable(part[offset..]));
        }
        return panel;
    }
    private static void Open(string link,string label,string cwd,Action<string,string> download,Action<string> folder) {
        if(Uri.TryCreate(link,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)){Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});return;}
        string? path=null;
        if(link.StartsWith("file://",StringComparison.OrdinalIgnoreCase)&&Uri.TryCreate(link,UriKind.Absolute,out var fileUri)&&string.IsNullOrEmpty(fileUri.Host))path=Uri.UnescapeDataString(fileUri.AbsolutePath);
        else if(link.StartsWith("sandbox:",StringComparison.OrdinalIgnoreCase))path=Uri.UnescapeDataString(link[8..]);
        else if(link.StartsWith('/'))path=Uri.UnescapeDataString(link);
        else if(!link.Contains(':'))path=cwd.TrimEnd('/')+"/"+Uri.UnescapeDataString(link);
        if(path is null)return;
        if(link.EndsWith('/'))folder(path);else download(path,FileRules.SafeName(label));
    }
}
