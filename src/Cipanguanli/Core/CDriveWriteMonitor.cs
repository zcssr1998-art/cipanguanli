using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Cipanguanli.Core;

public sealed class CDriveWriteMonitor
{
    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    public Task MonitorAsync(
        IProgress<IReadOnlyList<CDriveWriteActivityEntry>> progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Monitor(progress, cancellationToken), cancellationToken);
    }

    private static void Monitor(IProgress<IReadOnlyList<CDriveWriteActivityEntry>> progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("写入追踪仅支持 Windows。");
        if (!IsElevated) throw new InvalidOperationException("实时写入来源追踪需要管理员权限，因为它使用 Windows Kernel ETW File I/O 事件。");

        var aggregate = new ConcurrentDictionary<int, MutableEntry>();
        var sessionName = $"Cipanguanli-CDriveWrite-{Environment.ProcessId}-{Guid.NewGuid():N}";
        using var session = new TraceEventSession(sessionName);
        session.StopOnDispose = true;

        session.Source.Kernel.FileIOWrite += data =>
        {
            try
            {
                var fileName = data.FileName ?? string.Empty;
                if (!IsSystemDrivePath(fileName)) return;
                var pid = data.ProcessID;
                if (pid < 0) return;
                var name = string.IsNullOrWhiteSpace(data.ProcessName) ? $"PID {pid}" : data.ProcessName;
                var bytes = Math.Max(0L, data.IoSize);
                aggregate.AddOrUpdate(pid,
                    _ => new MutableEntry(name, bytes, fileName),
                    (_, old) =>
                    {
                        old.ProcessName = name;
                        old.Bytes += bytes;
                        old.LastPath = fileName;
                        return old;
                    });
            }
            catch { }
        };

        session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO);
        using var timer = new System.Threading.Timer(_ =>
        {
            try
            {
                var snapshot = aggregate
                    .Select(kv => new CDriveWriteActivityEntry
                    {
                        ProcessId = kv.Key,
                        ProcessName = kv.Value.ProcessName,
                        BytesWritten = kv.Value.Bytes,
                        LastPath = kv.Value.LastPath
                    })
                    .OrderByDescending(x => x.BytesWritten)
                    .Take(100)
                    .ToArray();
                progress.Report(snapshot);
            }
            catch { }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        using var registration = ct.Register(() =>
        {
            try { session.Stop(noThrow: true); } catch { }
        });

        try { session.Source.Process(); }
        catch when (ct.IsCancellationRequested) { }
    }

    internal static bool IsSystemDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        return path.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MutableEntry
    {
        public MutableEntry(string processName, long bytes, string lastPath)
        {
            ProcessName = processName;
            Bytes = bytes;
            LastPath = lastPath;
        }

        public string ProcessName { get; set; }
        public long Bytes { get; set; }
        public string LastPath { get; set; }
    }
}
