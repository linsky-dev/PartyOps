# 只查询当前远程 Shell 的 Windows Job 内存限制，不修改配额或进程归属。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class PartyOpsReadOnlyJobMemory {
    [StructLayout(LayoutKind.Sequential)]
    public struct Basic {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Io {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Extended {
        public Basic BasicLimit;
        public Io IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass,
        out Extended information, uint length, IntPtr returnedLength);
    public static Dictionary<string, object> Read() {
        Extended info;
        bool ok = QueryInformationJobObject(IntPtr.Zero, 9, out info,
            (uint)Marshal.SizeOf(typeof(Extended)), IntPtr.Zero);
        int error = ok ? 0 : Marshal.GetLastWin32Error();
        Dictionary<string, object> result = new Dictionary<string, object>();
        result.Add("query_succeeded", ok);
        result.Add("win32_error", error);
        result.Add("limit_flags", info.BasicLimit.Flags);
        result.Add("job_memory_limit_enabled", (info.BasicLimit.Flags & 0x200) != 0);
        result.Add("process_memory_limit_enabled", (info.BasicLimit.Flags & 0x100) != 0);
        result.Add("job_memory_limit_bytes", info.JobMemoryLimit.ToUInt64());
        result.Add("process_memory_limit_bytes", info.ProcessMemoryLimit.ToUInt64());
        result.Add("peak_job_memory_bytes", info.PeakJobMemory.ToUInt64());
        result.Add("peak_process_memory_bytes", info.PeakProcessMemory.ToUInt64());
        result.Add("process_pointer_bytes", IntPtr.Size);
        result.Add("observation_only", true);
        return result;
    }
}
'@
$serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
[Console]::WriteLine($serializer.Serialize([PartyOpsReadOnlyJobMemory]::Read()))
