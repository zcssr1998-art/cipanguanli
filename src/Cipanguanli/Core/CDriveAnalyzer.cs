using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Cipanguanli.Core;

public sealed class CDriveAnalyzer
{
    private readonly SoftwareOwnershipService _ownership;
    private readonly CDriveHistoryService _history;

    public CDriveAnalyzer(SoftwareOwnershipService? ownership = null, CDriveHistoryService? history = null)
    {
        _ownership = ownership ?? new SoftwareOwnershipService();
        _history = history ?? new CDriveHistoryService();
    }

    public Task<CDriveReport> AnalyzeAsync(
        string root = "C:\\",
        bool includeSystemCommands = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Analyze(root, includeSystemCommands, progress, cancellationToken), cancellationToken);

    private CDriveReport Analyze(string root, bool includeSystemCommands, IProgress<string>? progress, CancellationToken ct)
    {
        root = NormalizeRoot(root);
        var findings = new List<CDriveFinding>();
        var appData = new List<CDriveAppDataEntry>();
        var virtualDisks = new List<CDriveVirtualDiskEntry>();
        var realSystemRoot = NormalizeRoot(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        var analyzingRealSystemDrive = root.Equals(realSystemRoot, StringComparison.OrdinalIgnoreCase);

        long total = 0, free = 0, used = 0;
        try
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
                used = Math.Max(0, total - free);
            }
        }
        catch { }

        progress?.Report("分析 Windows 临时文件与更新缓存…");
        AddMeasured(findings, root, @"Windows\Temp", "Windows 临时文件", "系统缓存", 92,
            "多数文件可重建；正在使用的文件会被 Windows 占用。优先走 Windows 存储/磁盘清理。",
            "打开 Windows 存储清理；不要手动强删被占用文件。", "storage-settings", canQuarantine: false, ct);
        AddMeasured(findings, root, @"Windows\SoftwareDistribution\Download", "Windows Update 下载缓存", "Windows 更新", 88,
            "已经完成安装的更新下载包常可回收；更新正在进行时不要处理。",
            "优先使用 Windows 存储/更新清理，不直接删除更新数据库。", "storage-settings", false, ct);
        AddMeasured(findings, root, @"ProgramData\Microsoft\Windows\DeliveryOptimization\Cache", "Delivery Optimization 缓存", "Windows 更新", 92,
            "Windows 更新分发缓存，可由系统重新下载。", "通过 Windows 存储清理。", "storage-settings", false, ct);
        AddMeasured(findings, root, @"ProgramData\Microsoft\Windows\WER\ReportArchive", "Windows 错误报告归档", "崩溃/日志", 88,
            "崩溃报告归档；如果当前不在排查故障，通常可以清理。", "可进入隔离区或由系统清理。", "quarantine", true, ct);
        AddMeasured(findings, root, @"ProgramData\Microsoft\Windows\WER\ReportQueue", "Windows 错误报告队列", "崩溃/日志", 85,
            "待上报的错误报告；排障期间建议保留。", "不排障时可隔离。", "quarantine", true, ct);
        AddMeasured(findings, root, @"Windows\Logs\CBS", "CBS 组件服务日志", "系统日志", 78,
            "Windows 组件服务日志。过旧日志一般价值较低，但故障排查时可能需要。", "排障期保留；否则只处理旧日志。", "open-location", false, ct, reclaimRatio: 0.5);
        AddMeasured(findings, root, @"Windows\Minidump", "Windows 小型转储", "崩溃转储", 82,
            "蓝屏/崩溃分析文件。若近期没有排查蓝屏需求，可清理。", "可隔离后观察。", "quarantine", true, ct);
        AddFile(findings, Path.Combine(root, "Windows", "MEMORY.DMP"), "MEMORY.DMP", "崩溃转储", 80,
            "完整内存转储可能很大；用于蓝屏/内核故障分析。", "不排障时可隔离。", "quarantine", true);

        progress?.Report("检查 Windows.old、系统组件、Installer、休眠与分页文件…");
        AddMeasured(findings, root, "Windows.old", "Windows.old 旧系统", "系统回退", 72,
            "旧 Windows 版本。删除后通常无法再回退到之前的 Windows 版本。",
            "确认不需要系统回退后，通过 Windows 存储清理。", "storage-settings", false, ct);
        AddMeasured(findings, root, @"Windows\Installer", "Windows Installer 缓存", "系统关键缓存", 5,
            "MSI/MSP 安装缓存被软件修复、更新、卸载使用。手动删除可能导致程序无法维护。",
            "禁止直接删除；只做占用诊断。", "apps-settings", false, ct, reclaimRatio: 0, isProtected: true);
        AddMeasured(findings, root, @"ProgramData\Package Cache", "安装器 Package Cache", "安装/修复缓存", 28,
            "Visual Studio、VC++ 等安装器可能依赖这里进行修复或卸载。",
            "不要整目录删除；优先通过对应安装器管理。", "apps-settings", false, ct, reclaimRatio: 0, isProtected: true);
        AddMeasured(findings, root, @"Windows\System32\DriverStore\FileRepository", "DriverStore 驱动仓库", "驱动", 15,
            "Windows 当前与历史驱动包仓库。直接删 FileRepository 可能破坏设备驱动。",
            "只通过 PnPUtil/设备管理器处理明确不用的旧驱动。", "driver-manager", false, ct, reclaimRatio: 0, isProtected: true);
        AddMeasured(findings, root, @"Program Files\WindowsApps", "Microsoft Store / WindowsApps", "应用安装", 10,
            "受保护的 Store 应用目录。不要夺权后直接删。",
            "通过 设置 → 应用 迁移或卸载。", "apps-settings", false, ct, reclaimRatio: 0, isProtected: true);

        AddFile(findings, Path.Combine(root, "hiberfil.sys"), "hiberfil.sys 休眠文件", "系统保留", 58,
            "用于休眠和快速启动；大小通常与内存配置有关。关闭/缩减会影响休眠或快速启动。",
            "可选择关闭休眠或改为 reduced；需要管理员权限。", "hibernate-options", false);
        AddFile(findings, Path.Combine(root, "pagefile.sys"), "pagefile.sys 分页文件", "系统关键文件", 5,
            "Windows 提交内存与崩溃转储的重要组成部分。仅凭体积关闭可能导致内存不足或程序异常。",
            "默认不要关闭；如确有需要，在系统高级设置中调整/迁移。", "pagefile-settings", false, reclaimable: false, isProtected: true);
        AddFile(findings, Path.Combine(root, "swapfile.sys"), "swapfile.sys", "系统关键文件", 5,
            "Windows 系统交换文件。", "不要手动删除。", "pagefile-settings", false, reclaimable: false, isProtected: true);

        AddMeasured(findings, root, "$Recycle.Bin", "回收站", "用户可回收", 90,
            "已删除但仍占磁盘的文件。清空后不可从回收站恢复。", "确认无误后清空回收站。", "empty-recycle-bin", false, ct);

        if (analyzingRealSystemDrive)
        {
            progress?.Report("分析用户缓存、Adobe、NVIDIA、浏览器、开发与 AI 缓存…");
            AddUserSpecificFindings(findings, ct);
            AnalyzeAppData(appData, root, progress, ct);
            DetectVirtualDisks(virtualDisks, ct);
        }

        if (includeSystemCommands && analyzingRealSystemDrive)
        {
            progress?.Report("调用 DISM / VSS 做系统级只读诊断…");
            AddComponentStoreFinding(findings, root, ct);
            AddShadowStorageFinding(findings, root, ct);
            AddReservedStorageFinding(findings, ct);
        }
        else
        {
            AddMeasured(findings, root, @"Windows\WinSxS", "WinSxS 组件存储（表面大小）", "系统组件", 15,
                "WinSxS 含大量硬链接，普通目录大小会重复计数；真实占用必须以 DISM 为准。",
                "真实 C 盘分析时会调用 DISM /AnalyzeComponentStore。", "component-cleanup", false, ct, reclaimRatio: 0, isProtected: true);
        }

        findings = RemoveNestedDoubleCounting(findings)
            .OrderByDescending(x => x.SizeBytes)
            .ToList();
        appData = appData.OrderByDescending(x => x.SizeBytes).Take(80).ToList();
        virtualDisks = virtualDisks.OrderByDescending(x => x.SizeBytes).ToList();

        var historyEntries = findings.Select(x => (x.Name, x.Path, x.SizeBytes))
            .Concat(appData.Select(x => (x.Name, x.Path, x.SizeBytes)))
            .Concat(virtualDisks.Select(x => (x.Name, x.Path, x.SizeBytes)))
            .ToArray();
        var growth = _history.CompareAndSave(historyEntries);

        return new CDriveReport
        {
            Root = root,
            TotalBytes = total,
            FreeBytes = free,
            UsedBytes = used,
            Findings = findings,
            AppDataEntries = appData,
            VirtualDisks = virtualDisks,
            Growth = growth,
            CapturedUtc = DateTime.UtcNow
        };
    }

    private void AddUserSpecificFindings(List<CDriveFinding> findings, CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        AddMeasuredAbsolute(findings, Path.Combine(local, "Temp"), "当前用户 Temp", "临时文件", 94,
            "应用临时文件集中区。关闭应用后，绝大多数可重建。", "优先通过隔离/系统清理。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "D3DSCache"), "DirectX Shader Cache", "游戏/显卡缓存", 94,
            "DirectX 着色器缓存；删除后游戏/应用首次运行会重新编译，可能短暂卡顿。", "可隔离清理。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "NVIDIA", "DXCache"), "NVIDIA DXCache", "游戏/显卡缓存", 94,
            "NVIDIA DirectX shader 缓存，可重建。", "关闭游戏后可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "NVIDIA", "GLCache"), "NVIDIA GLCache", "游戏/显卡缓存", 94,
            "NVIDIA OpenGL shader 缓存，可重建。", "关闭游戏后可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "NV_Cache"), "NVIDIA NV_Cache", "游戏/显卡缓存", 92,
            "显卡驱动/游戏缓存，可重建。", "关闭游戏后可隔离。", "quarantine", true, ct);

        AddMeasuredAbsolute(findings, Path.Combine(roaming, "Adobe", "Common", "Media Cache Files"), "Adobe Media Cache Files", "Adobe 缓存", 92,
            "Premiere/After Effects 媒体缓存；删除后项目会重新生成缓存。", "关闭 Adobe 软件后可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(roaming, "Adobe", "Common", "Media Cache"), "Adobe Media Cache", "Adobe 缓存", 92,
            "Adobe 媒体数据库/缓存；删除后可能重新索引。", "关闭 Adobe 软件后可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "Adobe", "CameraRaw", "Cache"), "Adobe Camera Raw Cache", "Adobe 缓存", 92,
            "Camera Raw 预览缓存，可重建。", "关闭 Adobe 软件后可隔离。", "quarantine", true, ct);

        AddMeasuredAbsolute(findings, Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome Cache", "浏览器缓存", 94,
            "网页资源缓存；会自动重新下载。", "关闭 Chrome 后可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), "Edge Cache", "浏览器缓存", 94,
            "网页资源缓存；会自动重新下载。", "关闭 Edge 后可隔离。", "quarantine", true, ct);

        AddMeasuredAbsolute(findings, Path.Combine(local, "npm-cache"), "npm cache", "开发缓存", 90,
            "Node.js npm 下载缓存，可重新下载。", "可隔离；之后安装包可能重新下载。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".nuget", "packages"), "NuGet packages", "开发缓存", 78,
            "NuGet 全局包缓存。清理后项目恢复依赖会重新下载。", "空间紧张时可清，但会增加后续构建时间。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".gradle", "caches"), "Gradle caches", "开发缓存", 85,
            "Gradle 构建与依赖缓存，可重建。", "可隔离；后续构建会重新下载/编译。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "pip", "Cache"), "pip cache", "开发缓存", 90,
            "Python pip 包下载缓存。", "可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".cache", "pip"), "pip cache (.cache)", "开发缓存", 90,
            "Python pip 缓存。", "可隔离。", "quarantine", true, ct);
        AddMeasuredAbsolute(findings, Path.Combine(local, "pnpm", "store"), "pnpm store", "开发缓存", 75,
            "pnpm 内容寻址仓库。部分项目可能依赖硬链接；优先用 pnpm 自己的 prune。", "建议先用 pnpm store prune。", "open-location", false, ct, reclaimRatio: 0.6);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".cargo", "registry", "cache"), "Cargo registry cache", "开发缓存", 85,
            "Rust crates 下载缓存，可重新下载。", "可隔离。", "quarantine", true, ct);

        AddMeasuredAbsolute(findings, Path.Combine(profile, ".cache", "huggingface"), "Hugging Face cache", "AI 模型/缓存", 68,
            "可能包含数十 GB 模型权重。删除后模型需要重新下载，不等同普通缓存。", "优先迁移到其他盘或只删明确不用的模型。", "open-location", false, ct, reclaimRatio: 0.8);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".cache", "torch"), "Torch cache", "AI 模型/缓存", 72,
            "PyTorch Hub/扩展缓存，可能包含模型与编译产物。", "确认可重新下载/重建后再处理。", "open-location", false, ct, reclaimRatio: 0.7);
        AddMeasuredAbsolute(findings, Path.Combine(profile, ".ollama", "models"), "Ollama models", "AI 模型", 35,
            "这是本地模型本体，不是普通缓存。删除会让对应模型不可用。", "优先迁移模型目录，不直接清空。", "open-location", false, ct, reclaimRatio: 0);
        AddMeasuredAbsolute(findings, Path.Combine(local, "Ollama", "models"), "Ollama models (LocalAppData)", "AI 模型", 35,
            "这是本地模型本体，不是普通缓存。", "优先迁移。", "open-location", false, ct, reclaimRatio: 0);

        AddMeasuredAbsolute(findings, Path.Combine(docs, "WeChat Files"), "微信文件", "聊天/媒体", 42,
            "可能包含聊天图片、视频、文档和备份。删除会影响本地历史文件访问。", "按账号/日期/媒体类型人工筛选，不整目录删除。", "open-location", false, ct, reclaimRatio: 0);
        AddMeasuredAbsolute(findings, Path.Combine(docs, "Tencent Files"), "QQ / Tencent Files", "聊天/媒体", 42,
            "可能包含 QQ 聊天文件、图片、视频和接收文件。", "按账号和媒体类型人工筛选。", "open-location", false, ct, reclaimRatio: 0);

        AddMeasuredAbsolute(findings, Path.Combine(profile, "Downloads"), "下载目录", "用户文件", 35,
            "常见大文件集中地，但内容属于用户数据。", "按文件用途筛选；不要一键清空。", "open-location", false, ct, reclaimRatio: 0);

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
            AddMeasuredAbsolute(findings, oneDrive, "OneDrive 本地数据", "云盘", 45,
                "云盘本地副本。直接删除可能触发同步删除。", "优先用 OneDrive“释放空间/按需文件”。", "open-location", false, ct, reclaimRatio: 0);
    }

    private void AnalyzeAppData(List<CDriveAppDataEntry> output, string systemRoot, IProgress<string>? progress, CancellationToken ct)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var appRoot in roots)
        {
            DirectoryInfo[] dirs;
            try { dirs = new DirectoryInfo(appRoot).GetDirectories(); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                var size = MeasurePath(dir.FullName, ct);
                if (size < 100L * 1024 * 1024) continue;
                var product = FindLikelyProduct(dir.Name);
                var install = product?.InstallLocation ?? string.Empty;
                var installDrive = SafeDrive(install);
                var outside = !string.IsNullOrWhiteSpace(installDrive) && !installDrive.Equals(SafeDrive(systemRoot), StringComparison.OrdinalIgnoreCase);
                var owner = product?.DisplayName ?? GuessCommonOwner(dir.Name);
                var note = outside
                    ? $"程序主体位于 {installDrive}，但这个 AppData 目录仍在系统盘占用 {SizeFormatter.Format(size)}。"
                    : owner == "未识别"
                        ? "未匹配到明确安装项；可能是应用数据、缓存或卸载残留。"
                        : "应用数据位于系统盘；其中可能混合配置、账号数据与可重建缓存。";
                output.Add(new CDriveAppDataEntry
                {
                    Name = dir.Name,
                    Path = dir.FullName,
                    SizeBytes = size,
                    Owner = owner,
                    InstallLocation = install,
                    InstallDrive = installDrive,
                    InstalledOutsideSystemDrive = outside,
                    Note = note
                });
                progress?.Report($"AppData：{dir.Name} · {SizeFormatter.Format(size)}");
            }
        }
    }

    private InstalledProduct? FindLikelyProduct(string folderName)
    {
        var folder = NormalizeToken(folderName);
        if (folder.Length < 4) return null;
        InstalledProduct? best = null;
        var bestScore = 0;
        foreach (var product in _ownership.Products)
        {
            var name = NormalizeToken(product.DisplayName);
            var publisher = NormalizeToken(product.Publisher);
            var score = 0;
            if (name.Equals(folder, StringComparison.OrdinalIgnoreCase)) score = 100;
            else if (name.Contains(folder, StringComparison.OrdinalIgnoreCase) && folder.Length >= 5) score = 80;
            else if (folder.Contains(name, StringComparison.OrdinalIgnoreCase) && name.Length >= 5) score = 75;
            else if (publisher.Contains(folder, StringComparison.OrdinalIgnoreCase) && folder.Length >= 5) score = 60;
            if (score <= bestScore) continue;
            best = product;
            bestScore = score;
        }
        return bestScore >= 60 ? best : null;
    }

    private static string GuessCommonOwner(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("adobe")) return "Adobe";
        if (n.Contains("nvidia")) return "NVIDIA";
        if (n.Contains("discord")) return "Discord";
        if (n.Contains("docker")) return "Docker Desktop";
        if (n.Contains("telegram")) return "Telegram";
        if (n.Contains("wechat") || n.Contains("tencent")) return "腾讯 / 微信 / QQ";
        if (n.Contains("google")) return "Google / Chrome";
        if (n.Contains("mozilla")) return "Mozilla / Firefox";
        return "未识别";
    }

    private void DetectVirtualDisks(List<CDriveVirtualDiskEntry> output, CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddVhdxUnder(candidates, Path.Combine(local, "Docker"), ct);
        AddVhdxUnder(candidates, Path.Combine(local, "wsl"), ct);
        AddVhdxUnder(candidates, Path.Combine(profile, ".docker"), ct);

        var packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
        {
            try
            {
                foreach (var package in Directory.EnumerateDirectories(packages))
                {
                    ct.ThrowIfCancellationRequested();
                    var localState = Path.Combine(package, "LocalState");
                    var ext4 = Path.Combine(localState, "ext4.vhdx");
                    if (File.Exists(ext4)) candidates.Add(ext4);
                }
            }
            catch { }
        }

        foreach (var path in candidates)
        {
            try
            {
                var size = new FileInfo(path).Length;
                var lower = path.ToLowerInvariant();
                var kind = lower.Contains("docker") ? "Docker" : "WSL";
                output.Add(new CDriveVirtualDiskEntry
                {
                    Kind = kind,
                    Name = Path.GetFileName(path),
                    Path = path,
                    SizeBytes = size,
                    Detail = kind == "Docker"
                        ? "Docker Desktop 的虚拟磁盘可能包含镜像、容器、卷与 Build Cache；Linux 内删除数据后，VHDX 也未必立即缩小。"
                        : "WSL2 Linux 文件系统虚拟磁盘；Linux 内删文件后，宿主 VHDX 可能不会自动收缩。",
                    RecommendedAction = kind == "Docker"
                        ? "先看 docker system df；清理未使用对象后，再考虑通过 Docker Desktop 迁移磁盘镜像。"
                        : "先在发行版内清理，再 wsl --shutdown；高级用户可做 VHDX 压缩或迁移。"
                });
            }
            catch { }
        }
    }

    private static void AddVhdxUnder(HashSet<string> target, string root, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return;
        var stack = new Stack<string>();
        stack.Push(root);
        var visited = 0;
        while (stack.Count > 0 && visited < 5000)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            visited++;
            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*.vhdx", SearchOption.TopDirectoryOnly)) target.Add(file);
                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if ((info.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(dir);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void AddComponentStoreFinding(List<CDriveFinding> findings, string root, CancellationToken ct)
    {
        var winsxs = Path.Combine(root, "Windows", "WinSxS");
        if (!Directory.Exists(winsxs)) return;
        var output = RunCommand("dism.exe", new[] { "/Online", "/Cleanup-Image", "/AnalyzeComponentStore", "/English" }, ct, 45_000);
        var actual = ParseLabeledSize(output, "Actual Size of Component Store");
        var shared = ParseLabeledSize(output, "Shared with Windows");
        var backups = ParseLabeledSize(output, "Backups and Disabled Features");
        var cache = ParseLabeledSize(output, "Cache and Temporary Data");
        var recommended = output.Contains("Component Store Cleanup Recommended : Yes", StringComparison.OrdinalIgnoreCase);
        if (actual <= 0) actual = MeasurePath(winsxs, ct);
        var estimate = Math.Max(0, backups + cache);
        findings.Add(new CDriveFinding
        {
            Id = "winsxs",
            Name = "WinSxS 组件存储",
            Category = "系统组件",
            Path = winsxs,
            SizeBytes = actual,
            ReclaimableBytes = recommended ? estimate : 0,
            SafetyScore = 82,
            Risk = "官方清理可控 / 手动删除高危",
            Detail = $"DISM 实际组件存储 {SizeFormatter.Format(actual)}；与 Windows 共享 {SizeFormatter.Format(shared)}；备份/禁用功能 {SizeFormatter.Format(backups)}；缓存 {SizeFormatter.Format(cache)}。普通资源管理器会因硬链接重复计数。",
            RecommendedAction = recommended ? "DISM 建议执行 Component Cleanup。" : "DISM 当前未建议组件清理；不要手动删除 WinSxS。",
            ActionKey = "component-cleanup",
            CanQuarantine = false,
            IsProtected = false
        });
    }

    private static void AddShadowStorageFinding(List<CDriveFinding> findings, string root, CancellationToken ct)
    {
        var drive = SafeDrive(root);
        if (string.IsNullOrWhiteSpace(drive)) return;
        var output = RunCommand("vssadmin.exe", new[] { "list", "shadowstorage", $"/for={drive}\\" }, ct, 12_000);
        if (string.IsNullOrWhiteSpace(output)) return;
        var used = ParseVssUsedSize(output);
        findings.Add(new CDriveFinding
        {
            Id = "vss",
            Name = "系统还原点 / VSS 卷影副本",
            Category = "系统保护",
            Path = @"System Volume Information",
            SizeBytes = used,
            ReclaimableBytes = 0,
            SafetyScore = 35,
            Risk = "中-高",
            Detail = used > 0
                ? $"当前卷影副本已使用约 {SizeFormatter.Format(used)}。删除恢复点会降低系统回滚能力。"
                : "检测到系统卷影存储配置；本机输出格式无法可靠解析具体占用。",
            RecommendedAction = "打开系统保护设置查看/调整最大占用；不要直接操作 System Volume Information。",
            ActionKey = "restore-settings",
            CanQuarantine = false,
            IsProtected = true
        });
    }

    private static void AddReservedStorageFinding(List<CDriveFinding> findings, CancellationToken ct)
    {
        var output = RunCommand("dism.exe", new[] { "/Online", "/Get-ReservedStorageState", "/English" }, ct, 12_000);
        if (string.IsNullOrWhiteSpace(output)) return;
        var enabled = output.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
        findings.Add(new CDriveFinding
        {
            Id = "reserved-storage",
            Name = "Windows Reserved Storage",
            Category = "系统保留",
            Path = "Windows Reserved Storage",
            SizeBytes = 0,
            ReclaimableBytes = 0,
            SafetyScore = 10,
            Risk = "系统管理",
            Detail = enabled ? "Reserved Storage 当前启用，由 Windows 为更新、临时文件和系统维护预留。" : "Reserved Storage 当前未启用或状态无法确认。",
            RecommendedAction = "默认交给 Windows 管理；不把它当成普通清理空间。",
            ActionKey = "storage-settings",
            CanQuarantine = false,
            IsProtected = true
        });
    }

    private static void AddMeasured(
        List<CDriveFinding> target, string root, string relativePath, string name, string category, int safety,
        string detail, string action, string actionKey, bool canQuarantine, CancellationToken ct,
        double reclaimRatio = 1, bool isProtected = false)
        => AddMeasuredAbsolute(target, Path.Combine(root, relativePath), name, category, safety, detail, action, actionKey, canQuarantine, ct, reclaimRatio, isProtected);

    private static void AddMeasuredAbsolute(
        List<CDriveFinding> target, string path, string name, string category, int safety,
        string detail, string action, string actionKey, bool canQuarantine, CancellationToken ct,
        double reclaimRatio = 1, bool isProtected = false)
    {
        if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !File.Exists(path))) return;
        var size = MeasurePath(path, ct);
        if (size <= 0) return;
        var reclaimable = isProtected ? 0 : (long)(size * Math.Clamp(reclaimRatio, 0, 1));
        target.Add(new CDriveFinding
        {
            Id = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            Category = category,
            Path = path,
            SizeBytes = size,
            ReclaimableBytes = reclaimable,
            SafetyScore = safety,
            Risk = RiskFromSafety(safety, isProtected),
            Detail = detail,
            RecommendedAction = action,
            ActionKey = actionKey,
            CanQuarantine = canQuarantine && !isProtected,
            IsProtected = isProtected
        });
    }

    private static void AddFile(
        List<CDriveFinding> target, string path, string name, string category, int safety,
        string detail, string action, string actionKey, bool canQuarantine, bool reclaimable = true, bool isProtected = false)
    {
        if (!File.Exists(path)) return;
        try
        {
            var size = new FileInfo(path).Length;
            target.Add(new CDriveFinding
            {
                Id = name.ToLowerInvariant().Replace(' ', '-'),
                Name = name,
                Category = category,
                Path = path,
                SizeBytes = size,
                ReclaimableBytes = reclaimable && !isProtected ? size : 0,
                SafetyScore = safety,
                Risk = RiskFromSafety(safety, isProtected),
                Detail = detail,
                RecommendedAction = action,
                ActionKey = actionKey,
                CanQuarantine = canQuarantine && !isProtected,
                IsProtected = isProtected
            });
        }
        catch { }
    }

    internal static long MeasurePath(string path, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
        }
        catch { return 0; }

        long bytes = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (entry is FileInfo file) bytes += file.Length;
                        else if (entry is DirectoryInfo dir) stack.Push(dir.FullName);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return bytes;
    }

    private static IReadOnlyList<CDriveFinding> RemoveNestedDoubleCounting(IEnumerable<CDriveFinding> input)
    {
        // Keep distinct semantic findings. Only remove exact duplicate paths; nested entries are intentionally retained
        // because e.g. Adobe cache inside AppData is useful even when AppData is also aggregated on another tab.
        return input
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.SafetyScore).ThenByDescending(x => x.SizeBytes).First())
            .ToArray();
    }

    private static string RunCommand(string fileName, IEnumerable<string> args, CancellationToken ct, int timeoutMs)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            return stdout.GetAwaiter().GetResult() + Environment.NewLine + stderr.GetAwaiter().GetResult();
        }
        catch { return string.Empty; }
    }

    internal static long ParseLabeledSize(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;
            return ParseHumanSize(line);
        }
        return 0;
    }

    internal static long ParseHumanSize(string text)
    {
        var match = Regex.Match(text, @"([\d\.,]+)\s*(B|KB|MB|GB|TB)", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;
        var raw = match.Groups[1].Value.Replace(",", "");
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return value > 0 ? (long)(value * multiplier) : 0;
    }

    private static long ParseVssUsedSize(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("used shadow copy storage space") || line.Contains("已用", StringComparison.OrdinalIgnoreCase) || line.Contains("使用的卷影副本存储空间", StringComparison.OrdinalIgnoreCase))
            {
                var value = ParseHumanSize(line);
                if (value > 0) return value;
            }
        }
        return 0;
    }

    private static string RiskFromSafety(int score, bool protectedPath)
    {
        if (protectedPath) return "保护/禁止直接删除";
        if (score >= 90) return "低";
        if (score >= 80) return "低-中";
        if (score >= 65) return "中";
        if (score >= 45) return "中-高";
        return "高";
    }

    private static string NormalizeRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var pathRoot = Path.GetPathRoot(full);
        return string.IsNullOrWhiteSpace(pathRoot) ? full : pathRoot;
    }

    private static string SafeDrive(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return (Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty).TrimEnd('\\', '/'); }
        catch { return string.Empty; }
    }

    private static string NormalizeToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
