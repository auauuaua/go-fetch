using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CardPlayer.Services;

/// <summary>
/// Wraps a Windows Job Object so all child processes launched by a
/// "launcher" style executable (Chrome, Electron apps, etc.) are
/// automatically killed when the job handle is closed.
/// </summary>
internal sealed class JobObject : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int    JobObjectInfoClass,
        ref    JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        uint   cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long  PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint  LimitFlags, MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint  ActiveProcessLimit, Affinity, PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    private const int  JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    private IntPtr _handle;
    private bool   _disposed;

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        // Kill all processes in the job when this handle is closed
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(_handle, JobObjectExtendedLimitInformation,
            ref info, (uint)Marshal.SizeOf(info));
    }

    /// <summary>Assigns a process to this job. Call immediately after Process.Start().</summary>
    public bool Assign(Process process)
    {
        if (_disposed || _handle == IntPtr.Zero) return false;
        return AssignProcessToJobObject(_handle, process.Handle);
    }

    /// <summary>
    /// Terminates all processes in the job immediately.
    /// Prefer this over CloseMainWindow for launcher-style apps.
    /// </summary>
    public void Terminate()
    {
        if (_disposed || _handle == IntPtr.Zero) return;
        TerminateJobObject(_handle, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            // Closing the handle with KILL_ON_JOB_CLOSE set kills all processes
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
