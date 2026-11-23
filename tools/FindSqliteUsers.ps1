param([Parameter(Mandatory = $true)][string]$DatabasePath)
$ErrorActionPreference = 'Stop'
$path = [IO.Path]::GetFullPath($DatabasePath)
$files = @($path, "$path-wal", "$path-shm") | Where-Object { Test-Path -LiteralPath $_ }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class RestartManagerFiles
{
    [StructLayout(LayoutKind.Sequential)]
    public struct UniqueProcess { public int ProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME StartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint handle, uint fileCount, string[] files,
        uint applicationCount, IntPtr applications, uint serviceCount, IntPtr services);
    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint handle, out uint needed, ref uint count, [Out] ProcessInfo[] processes, ref uint rebootReasons);
    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint handle);

    public static ProcessInfo[] Find(string[] files)
    {
        var key = new StringBuilder(33);
        uint handle;
        int code = RmStartSession(out handle, 0, key);
        if (code != 0) throw new InvalidOperationException("RmStartSession: " + code);
        try
        {
            code = RmRegisterResources(handle, (uint)files.Length, files, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (code != 0) throw new InvalidOperationException("RmRegisterResources: " + code);
            uint needed;
            uint count = 0;
            uint reboot = 0;
            code = RmGetList(handle, out needed, ref count, Array.Empty<ProcessInfo>(), ref reboot);
            if (code == 0) return Array.Empty<ProcessInfo>();
            if (code != 234) throw new InvalidOperationException("RmGetList: " + code);
            var result = new ProcessInfo[needed];
            count = needed;
            code = RmGetList(handle, out needed, ref count, result, ref reboot);
            if (code != 0) throw new InvalidOperationException("RmGetList: " + code);
            Array.Resize(ref result, (int)count);
            return result;
        }
        finally { RmEndSession(handle); }
    }
}
'@

[RestartManagerFiles]::Find([string[]]$files) |
    Select-Object @{Name='ProcessId';Expression={$_.Process.ProcessId}},AppName,ServiceName,ApplicationType,AppStatus
