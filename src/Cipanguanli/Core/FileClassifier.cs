using System.IO;

namespace Cipanguanli.Core;

public static class FileClassifier
{
    private sealed record PathRule(string[] Tokens, string Category, string Risk, string Note, bool CleanupCandidate = false);

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v", ".ts", ".m2ts", ".webm", ".rmvb", ".mpeg", ".mpg" };
    private static readonly HashSet<string> ThreeDExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".ma", ".mb", ".max", ".blend", ".ztl", ".zpr", ".spp", ".spsm", ".fbx", ".obj", ".abc", ".usd", ".usda", ".usdc", ".vdb", ".hip", ".hiplc", ".c4d", ".3ds", ".dae", ".gltf", ".glb", ".uasset", ".umap" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz" };
    private static readonly HashSet<string> AiModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".safetensors", ".ckpt", ".gguf", ".pth", ".pt", ".onnx", ".bin" };
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".iso", ".img", ".msi", ".msix", ".appx" };
    private static readonly HashSet<string> VmExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".vhd", ".vhdx", ".vmdk", ".qcow", ".qcow2", ".vdi" };
    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb" };
    private static readonly HashSet<string> GamePackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".pak", ".ucas", ".utoc", ".bundle", ".arc" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp", ".exr", ".hdr", ".psd", ".psb" };

    private static readonly PathRule[] SpecificRules =
    [
        new(["\\tencent video\\", "\\qqlive\\", "\\qldownload\\", "\\腾讯视频\\"], "腾讯视频缓存/下载", "低-中", "腾讯视频离线视频或缓存；确认不再观看后通常可清理。", true),
        new(["\\iqiyi\\", "\\qiyi\\", "\\爱奇艺\\"], "爱奇艺缓存/下载", "低-中", "爱奇艺离线视频或缓存；确认不再观看后通常可清理。", true),
        new(["\\youku\\", "\\优酷\\"], "优酷缓存/下载", "低-中", "优酷离线视频或缓存；确认不再观看后通常可清理。", true),
        new(["\\wechat files\\", "\\wechatfiles\\", "\\微信文件\\", "\\weixin\\"], "微信文件", "中", "可能是微信接收的视频、图片、文档或缓存；建议按日期和聊天内容核对。"),
        new(["\\telegram desktop\\", "\\tdesktop\\"], "Telegram 数据/缓存", "中", "Telegram 下载或缓存目录；应用内清理通常比直接删目录更稳妥。", true),
        new(["\\discord\\cache\\", "\\discord\\code cache\\", "\\discord\\gpucache\\"], "Discord 缓存", "低", "Discord 可重建缓存；关闭 Discord 后通常可清理。", true),
        new(["\\maya\\", "\\zbrush\\", "\\substance\\", "\\substance painter\\", "\\marvelous\\", "\\houdini\\", "\\unreal\\", "\\unity\\", "\\metahuman\\", "\\xgen\\", "\\mari\\"], "3D/游戏美术工程", "中-高", "3D、材质、角色、动画或游戏引擎工程；优先归档/迁移，不建议直接删除。"),
        new(["\\deriveddatacache\\"], "Unreal 可重建缓存", "低-中", "Unreal Derived Data Cache，可重建但会增加下次着色器/资产处理时间。", true),
        new(["\\library\\shadercache\\", "\\library\\bee\\", "\\library\\artifacts\\"], "Unity 可重建工程缓存", "中", "Unity 工程生成缓存；通常可重建，但首次重新导入项目会耗时。", true),
        new(["\\steam\\", "\\steamapps\\"], "Steam 游戏文件", "中", "Steam 游戏安装或资源目录；建议通过 Steam 卸载或移动游戏库。"),
        new(["\\epic games\\", "\\epicgames\\"], "Epic 游戏文件", "中", "Epic 游戏安装或资源目录；建议通过启动器卸载或移动。"),
        new(["\\google\\chrome\\user data\\", "\\microsoft\\edge\\user data\\", "\\mozilla\\firefox\\profiles\\"], "浏览器用户数据/缓存", "中", "浏览器目录里既有缓存也有用户配置；只清理明确的 Cache/Code Cache/GPUCache 子目录，不要整目录删除。", true),
        new(["\\nvidia\\dxcache\\", "\\nvidia\\glcache\\", "\\nv_cache\\", "\\d3dscache\\"], "显卡/着色器缓存", "低", "显卡或 DirectX 着色器缓存，可重建；清理后首次启动游戏可能重新编译。", true),
        new(["\\adobe\\common\\media cache", "\\adobe\\common\\media cache files", "\\cameraraw\\cache"], "Adobe 媒体/Camera Raw 缓存", "低-中", "Adobe 可重建缓存，通常适合从应用设置或关闭应用后清理。", true),
        new(["\\npm-cache\\", "\\pip\\cache\\", "\\gradle\\caches\\", "\\nuget\\packages\\", "\\yarn\\cache\\"], "开发依赖缓存", "低-中", "npm/pip/Gradle/NuGet/Yarn 缓存可重新下载；清理会换取磁盘空间但增加后续安装时间。", true),
        new(["\\huggingface\\hub\\", "\\huggingface\\transformers\\", "\\torch\\hub\\checkpoints\\"], "AI 模型缓存", "中", "模型缓存可能很大；删除后通常需要重新下载，建议优先检查重复版本。", true),
        new(["\\softwaredistribution\\download\\", "\\deliveryoptimization\\"], "Windows 更新下载缓存", "中", "Windows 更新下载缓存；优先使用 Windows“临时文件/磁盘清理”入口，不建议粗暴删除整个系统目录。", true),
        new(["\\downloads\\", "\\下载\\"], "下载目录", "取决于内容", "下载目录中的大文件，通常值得优先人工检查。", true),
        new(["\\onedrive\\", "\\dropbox\\", "\\google drive\\"], "云盘同步文件", "高", "可能是云盘本地副本；删除前确认同步策略，避免同步删除云端内容。")
    ];

    private static readonly PathRule[] GenericCacheRules =
    [
        new(["\\appdata\\local\\temp\\", "\\windows\\temp\\"], "临时文件", "低-中", "系统或用户临时目录；关闭相关程序后通常有清理价值。", true),
        new(["\\cache\\", "\\caches\\", "\\code cache\\", "\\gpucache\\"], "应用缓存", "低-中", "路径像应用缓存；通常可重建，但应先关闭对应应用并避免删除其配置根目录。", true)
    ];

    public static bool IsVideoExtension(string extension) => VideoExtensions.Contains(extension);
    public static bool IsThreeDExtension(string extension) => ThreeDExtensions.Contains(extension);
    public static bool IsArchiveExtension(string extension) => ArchiveExtensions.Contains(extension);
    public static bool IsAiModelExtension(string extension) => AiModelExtensions.Contains(extension);

    public static FileAnnotation ClassifyFile(string path)
    {
        var normalized = Normalize(path);
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);

        if (name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("memory.dmp", StringComparison.OrdinalIgnoreCase))
            return new("Windows 系统关键文件", "禁止手动删除", "虚拟内存、休眠或崩溃转储等系统文件；不要直接删除。可通过 Windows 设置调整对应功能。");

        var inProtectedArea = IsProtected(normalized);
        var rule = Match(SpecificRules, normalized);
        if (rule is not null) return new(rule.Category, inProtectedArea ? "高" : rule.Risk, rule.Note);

        // Content semantics beat generic Temp/Cache path names.
        if (ThreeDExtensions.Contains(extension)) return new("3D/游戏美术资产", "中-高", "模型、场景、材质、缓存或引擎资源；可能有工程依赖，建议先确认项目再归档或删除。");
        if (AiModelExtensions.Contains(extension)) return new("AI 模型/权重", "中", "常见 AI 模型权重或推理文件，体积通常较大；确认模型是否仍在使用后再删除或迁移。");
        if (VideoExtensions.Contains(extension)) return new("视频文件", "低", "常见视频媒体文件；确认内容不再需要后，删除可直接释放大量空间。");
        if (VmExtensions.Contains(extension)) return new("虚拟机磁盘", "高", "可能包含完整虚拟机系统和数据；先确认对应虚拟机，不要直接删除。");
        if (ArchiveExtensions.Contains(extension)) return new("压缩包/归档", "低-中", "若已经解压且不需要留作备份，通常是很好的清理候选。");
        if (InstallerExtensions.Contains(extension)) return new("安装包/磁盘镜像", "低-中", "安装程序或磁盘镜像；软件已安装且无需重装时可考虑清理。");
        if (GamePackageExtensions.Contains(extension)) return new("游戏资源包", "中-高", "大型游戏资源文件；建议通过游戏平台/启动器卸载，不要单独删除资源包。");
        if (ImageExtensions.Contains(extension)) return new("图像/贴图文件", "中", "大型图像、贴图或工程素材；3D/设计项目可能依赖，删除前先核对。");
        if (DatabaseExtensions.Contains(extension)) return new("数据库文件", "高", "数据库可能属于应用程序或本地业务数据；不要仅凭体积判断删除。");

        rule = Match(GenericCacheRules, normalized);
        if (rule is not null) return new(rule.Category, inProtectedArea ? "高" : rule.Risk, rule.Note);
        if (inProtectedArea) return new("系统/程序目录文件", "高", "位于 Windows 或程序目录；除非明确知道用途，否则不要手动删除。");
        return new("其他大文件", "未知", "本地规则暂未识别；结合文件名、上级目录、修改日期进一步判断。");
    }

    public static (string Category, string Risk, string Note, bool CleanupCandidate) ClassifyFolder(
        string path, long totalBytes, long threeDBytes, long videoBytes, long archiveBytes, long aiModelBytes)
    {
        var normalized = Normalize(path);
        var rule = Match(SpecificRules, normalized);
        if (rule is not null) return (rule.Category, IsProtected(normalized) ? "高" : rule.Risk, rule.Note, rule.CleanupCandidate);

        if (totalBytes > 0 && threeDBytes >= Math.Max(1, totalBytes / 20))
            return ("3D/游戏美术工程", "中-高", "该文件夹内检测到明显的 3D/引擎资产特征；更适合迁移或归档，而不是直接清理。", false);
        if (totalBytes > 0 && aiModelBytes >= Math.Max(1, totalBytes / 10))
            return ("AI 模型目录", "中", "该目录中有较多模型权重文件；可优先检查重复版本或已经不用的模型。", false);
        if (totalBytes > 0 && videoBytes >= totalBytes / 2)
            return ("视频资源文件夹", "低-中", "该文件夹主要由视频构成；可按内容和修改时间判断是否迁移或删除。", true);
        if (totalBytes > 0 && archiveBytes >= totalBytes / 2)
            return ("归档/压缩包文件夹", "低-中", "该文件夹主要由压缩包构成；已解压且无需备份的文件通常是清理候选。", true);

        rule = Match(GenericCacheRules, normalized);
        if (rule is not null) return (rule.Category, IsProtected(normalized) ? "高" : rule.Risk, rule.Note, rule.CleanupCandidate);
        return ("普通大文件夹", "取决于内容", "文件夹占用较大；建议展开空间地图并结合内部大文件判断。", false);
    }

    private static PathRule? Match(IEnumerable<PathRule> rules, string normalized)
        => rules.FirstOrDefault(r => r.Tokens.Any(normalized.Contains));

    private static bool IsProtected(string normalized)
        => normalized.Contains("\\windows\\") || normalized.Contains("\\program files\\") ||
           normalized.Contains("\\program files (x86)\\") || normalized.Contains("\\programdata\\") ||
           normalized.Contains("\\system volume information\\");

    private static string Normalize(string path) => path.Replace('/', '\\').ToLowerInvariant();
}
