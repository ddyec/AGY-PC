using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AGY.PC.Windows;
internal sealed class PreviewWindow : Window {
    public PreviewWindow(string path) {
        Title=System.IO.Path.GetFileName(path);Width=920;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new Thickness(18)};Content=dock;
        var save=Theme.Button("另存到文件…",()=>{var dialog=new SaveFileDialog{FileName=System.IO.Path.GetFileName(path)};if(dialog.ShowDialog(this)==true)System.IO.File.Copy(path,dialog.FileName,true);});DockPanel.SetDock(save,Dock.Bottom);dock.Children.Add(save);
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp"}.Contains(extension)) {
            try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();dock.Children.Add(new ScrollViewer{Content=new Image{Source=image,Stretch=Stretch.Uniform},HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
            catch(Exception){dock.Children.Add(Theme.Text("Windows 无法预览这种图片。可另存后用其他应用打开。"));}
        } else if(new[]{".txt",".md",".json",".csv",".log",".py",".cs",".js",".ts",".html",".xml",".svg",".css",".yaml",".yml"}.Contains(extension)) {
            var info=new System.IO.FileInfo(path);dock.Children.Add(info.Length<=2*1024*1024?new TextBox{Text=System.IO.File.ReadAllText(path),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Cascadia Code, Consolas")}:Theme.Text("文本超过 2 MiB，请另存后打开。"));
        } else dock.Children.Add(Theme.Text("文件已下载。此类型请另存后，使用你选择的应用打开。\n预览不会自动执行程序或脚本。",18));
    }
}
