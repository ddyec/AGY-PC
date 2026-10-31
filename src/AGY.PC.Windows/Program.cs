using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using AGY.PC.Core;

namespace AGY.PC.Windows;
public static class Program {
    [STAThread] public static void Main() {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Theme.Install(app);
        app.DispatcherUnhandledException += (_,e) => { MessageBox.Show(e.Exception.Message,"AGY - PC",MessageBoxButton.OK,MessageBoxImage.Error); e.Handled=true; };
        try {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AGY-PC","state.bin");
            var workspace = new Workspace(new ProtectedStateStore(path,Dpapi.Protect,Dpapi.Unprotect));
            app.Run(new MainWindow(workspace));
        } catch (Exception error) { MessageBox.Show(error.Message,"AGY - PC",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
}
internal static class Dpapi {
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] bytes) => Transform(bytes,true);
    public static byte[] Unprotect(byte[] bytes) => Transform(bytes,false);
    private static byte[] Transform(byte[] bytes, bool protect) {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            Blob output;
            var ok = protect ? CryptProtectData(ref input,"AGY-PC",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data,result,0,result.Length); return result; }
            finally { LocalFree(output.Data); }
        } finally {
            Marshal.Copy(new byte[bytes.Length],0,input.Data,bytes.Length); Marshal.FreeHGlobal(input.Data);
        }
    }
}
