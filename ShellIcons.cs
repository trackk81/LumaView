using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumaView {
sealed class ShellIcons : IDisposable {
    public readonly ImageList Images=new ImageList {ImageSize=new Size(20,20),ColorDepth=ColorDepth.Depth32Bit};
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct FileInfo {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="SHGetFileInfoW")]
    static extern IntPtr GetFileInfo(string path,uint attributes,out FileInfo info,uint size,uint flags);
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW")]
    static extern IntPtr GetPidlInfo(IntPtr pidl,uint attributes,out FileInfo info,uint size,uint flags);
    [DllImport("shell32.dll")] static extern int SHGetSpecialFolderLocation(IntPtr owner,int folder,out IntPtr pidl);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    const uint IconFlag=0x100,SmallIcon=1,PidlFlag=8,UseFileAttributes=0x10;
    int? folderIcon;
    internal int Folder(){
        if(folderIcon.HasValue)return folderIcon.Value;
        FileInfo info;
        GetFileInfo("folder",0x10,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),IconFlag|SmallIcon|UseFileAttributes);
        folderIcon=Add(info);
        return folderIcon.Value;
    }
    public int ForPath(string path) {
        FileInfo info;
        var result=GetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),IconFlag|SmallIcon);
        if(result==IntPtr.Zero || info.Icon==IntPtr.Zero) {
            if(info.Icon!=IntPtr.Zero)DestroyIcon(info.Icon);
            GetFileInfo("folder",0x10,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),IconFlag|SmallIcon|UseFileAttributes);
        }
        return Add(info);
    }
    public int Computer() {
        IntPtr pidl;
        if(SHGetSpecialFolderLocation(IntPtr.Zero,17,out pidl)!=0)return ForPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        try{FileInfo info;GetPidlInfo(pidl,0,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),IconFlag|SmallIcon|PidlFlag);return Add(info);}
        finally{Marshal.FreeCoTaskMem(pidl);}
    }
    int Add(FileInfo info) {
        if(info.Icon==IntPtr.Zero)return -1;
        try {
            string key=info.Index.ToString();int existing=Images.Images.IndexOfKey(key);if(existing>=0)return existing;
            using(var icon=Icon.FromHandle(info.Icon))using(var bitmap=icon.ToBitmap()){
                Images.Images.Add(key,bitmap);
                // ImageList defers copying until it owns a native handle.
                var handle=Images.Handle;
            }
            return Images.Images.Count-1;
        }finally{DestroyIcon(info.Icon);}
    }
    public void Dispose(){Images.Dispose();}
}
}
