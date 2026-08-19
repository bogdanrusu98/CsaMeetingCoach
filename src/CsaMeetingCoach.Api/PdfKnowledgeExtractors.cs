using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CsaMeetingCoach.Api;

public interface IPdfKnowledgeExtractor
{
    Task<string> ExtractAsync(string path, CancellationToken cancellationToken);
}

public sealed class InProcessPdfKnowledgeExtractor : IPdfKnowledgeExtractor
{
    public async Task<string> ExtractAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () => PdfTextExtractionEngine.Extract(path, cancellationToken),
                cancellationToken);
        }
        catch (PdfExtractionRejectedException exception)
        {
            throw new KnowledgeRejectedException(exception.Message, exception);
        }
    }
}

public sealed class IsolatedPdfKnowledgeExtractor(string executablePath)
    : IPdfKnowledgeExtractor
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);
    private const ulong MaximumProcessMemoryBytes = 256UL * 1024 * 1024;

    public async Task<string> ExtractAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(executablePath))
        {
            throw new KnowledgeExtractionUnavailableException(
                "PDF extraction is unavailable on this host.");
        }

        var outputPath = string.Concat(
            path,
            ".",
            Guid.NewGuid().ToString("N"),
            ".txt");
        var eventName = string.Concat(
            "Local\\CsaMeetingCoach.Pdf.",
            Guid.NewGuid().ToString("N"));
        using var startEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            eventName);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            }
        };
        process.StartInfo.ArgumentList.Add(path);
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add(eventName);

        WindowsProcessMemoryLimit.SafeJobHandle? job = null;
        var processStarted = false;
        try
        {
            if (!process.Start())
            {
                throw new KnowledgeExtractionUnavailableException(
                    "The PDF extraction process could not start.");
            }
            processStarted = true;

            job = WindowsProcessMemoryLimit.Create(
                process,
                MaximumProcessMemoryBytes);
            startEvent.Set();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(ExtractionTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                await TerminateAsync(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new KnowledgeRejectedException(
                    "PDF extraction exceeded the 30-second processing limit.");
            }

            if (process.ExitCode != 0)
            {
                throw process.ExitCode == 2
                    ? new KnowledgeRejectedException(
                        "The PDF is malformed, protected, image-only, or exceeds safe extraction limits.")
                    : new KnowledgeExtractionUnavailableException(
                        "The isolated PDF extractor failed.");
            }
            if (!File.Exists(outputPath)
                || new FileInfo(outputPath).Length > 1_000_000)
            {
                throw new KnowledgeExtractionUnavailableException(
                    "The isolated PDF extractor returned an invalid result.");
            }

            return await File.ReadAllTextAsync(outputPath, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new KnowledgeExtractionUnavailableException(
                "The isolated PDF extractor could not be secured.",
                exception);
        }
        finally
        {
            startEvent.Set();
            if (processStarted && !process.HasExited)
            {
                await TerminateAsync(process);
            }

            job?.Dispose();
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (IOException)
            {
                // Session quarantine cleanup retries any transient file lock.
            }
        }
    }

    private static async Task TerminateAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}

internal static class WindowsProcessMemoryLimit
{
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    public static SafeJobHandle Create(Process process, ulong maximumMemoryBytes)
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags =
                    JobObjectLimitProcessMemory | JobObjectLimitKillOnJobClose
            },
            ProcessMemoryLimit = (UIntPtr)maximumMemoryBytes
        };
        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        if (!SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformationClass,
                ref information,
                (uint)length)
            || !AssignProcessToJobObject(handle, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        int informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeJobHandle job,
        IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

public sealed class KnowledgeExtractionUnavailableException : InvalidOperationException
{
    public KnowledgeExtractionUnavailableException(string message) : base(message)
    {
    }

    public KnowledgeExtractionUnavailableException(
        string message,
        Exception innerException) : base(message, innerException)
    {
    }
}
