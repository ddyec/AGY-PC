using System.Windows;
using System.Windows.Controls;
using AGY.PC.Core;

namespace AGY.PC.Windows;
internal sealed class ConnectionWindow : Window {
    public Credentials? Result { get; private set; }
    public ConnectionWindow(Credentials? saved) {
        Title="连接 AGY - PC";Width=520;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var address=new TextBox { Text=saved?.Address??"https://agy.yectech.uk" };
        var token=new PasswordBox { Password=saved?.Token??"" };var error=Theme.Text("",13,Theme.Brush("#F9B38F"));
        var connect=Theme.Button("连接 AGY",()=>{try{Result=Credentials.Validate(address.Text,token.Password);DialogResult=true;}catch(Exception e){error.Text=e.Message;}});
        connect.IsDefault=true;connect.Background=Theme.Accent;connect.Foreground=Theme.Background;
        var panel=Theme.Stack(Theme.Logo(64),Theme.Text("AGY，继续你的工作。",25),Theme.Text("使用与 iPhone 相同的服务器地址和连接密钥。",13,Theme.Muted),Theme.Text("服务器地址"),address,Theme.Text("连接密钥"),token,error,connect,Theme.Text("连接信息仅保存在此 Windows 用户的加密存储中。这里不需要 Google 密码。",12,Theme.Muted));panel.Margin=new Thickness(30);Content=panel;
    }
}
internal static class Dialogs {
    public static string? Prompt(Window owner,string title,string text) {
        var window=new Window{Owner=owner,Title=title,Width=420,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var input=new TextBox{Text=text};var panel=Theme.Stack(input,Theme.Button("保存",()=>window.DialogResult=true));panel.Margin=new Thickness(20);window.Content=panel;
        return window.ShowDialog()==true?input.Text:null;
    }
}
