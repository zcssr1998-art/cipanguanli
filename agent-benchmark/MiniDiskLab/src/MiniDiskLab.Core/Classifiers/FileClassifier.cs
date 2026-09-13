using MiniDiskLab.Core.Models;

namespace MiniDiskLab.Core.Classifiers;

/// <summary>
/// 与框架无关的规则，用于根据扩展名**以及**路径线索
/// （目录名、产品文件夹、用户主目录片段）对文件进行分类。
/// </summary>
/// <remarks>
/// 执行分类时不需要网络访问，也不调用在线 AI。
/// 规则按优先级排序 —— 当多条规则同时匹配时，选择分数最高的一条。
/// </remarks>
public sealed class FileClassifier : IFileClassifier
{
    private readonly IReadOnlyList<ClassificationRule> _extensionRules;
    private readonly IReadOnlyList<ClassificationRule> _pathRules;

    /// <summary>创建一个使用内置规则集的分类器。</summary>
    public FileClassifier()
        : this(DefaultExtensionRules(), DefaultPathRules())
    {
    }

    /// <summary>创建一个使用自定义规则集的分类器（测试时使用）。</summary>
    public FileClassifier(
        IReadOnlyList<ClassificationRule> extensionRules,
        IReadOnlyList<ClassificationRule> pathRules)
    {
        _extensionRules = extensionRules ?? throw new ArgumentNullException(nameof(extensionRules));
        _pathRules = pathRules ?? throw new ArgumentNullException(nameof(pathRules));
    }

    /// <inheritdoc />
    public ClassificationResult Classify(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return new ClassificationResult(FileCategory.Unknown, "未知文件");
        }

        var normalized = fullPath.Replace('/', '\\');
        var fileName = GetFileName(normalized);
        var extension = GetExtension(fileName);
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        ClassificationResult? best = null;
        var bestPriority = int.MinValue;

        foreach (var rule in _pathRules)
        {
            if (!rule.Matches(extension, segments, fileName))
            {
                continue;
            }

            if (rule.Priority > bestPriority)
            {
                bestPriority = rule.Priority;
                best = rule.Result;
            }
        }

        if (best.HasValue)
        {
            return best.Value;
        }

        foreach (var rule in _extensionRules)
        {
            if (rule.Matches(extension, segments, fileName))
            {
                return rule.Result;
            }
        }

        return new ClassificationResult(
            FileCategory.Unknown,
            extension.Length > 0
                ? $"未知文件（{extension}）"
                : "未知文件（无扩展名）");
    }

    /// <inheritdoc />
    public FileCategory ClassifyCategory(string fullPath) => Classify(fullPath).Category;

    private static string GetFileName(string path)
    {
        var idx = path.LastIndexOf('\\');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    /// <summary>返回小写扩展名（包含前导点号），如果不存在则返回空字符串。</summary>
    internal static string GetExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        // 点号在开头或结尾（例如 “.gitignore”）按无扩展名处理。
        if (dot <= 0 || dot == fileName.Length - 1)
        {
            return string.Empty;
        }

        return fileName[dot..].ToLowerInvariant();
    }

    /// <summary>
    /// 用于通用扩展名的后备规则 —— 仅在没有任何路径规则匹配时使用。
    /// </summary>
    internal static IReadOnlyList<ClassificationRule> DefaultExtensionRules() => new[]
    {
        ExtensionRule(FileCategory.Video, "视频文件",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".rmvb", ".3gp", ".vob"),

        ExtensionRule(FileCategory.Image, "图片文件",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".svg", ".raw", ".cr2", ".nef", ".arw", ".dng", ".psd", ".ai"),

        ExtensionRule(FileCategory.Archive, "压缩包",
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".cab", ".lz", ".lzma", ".zst"),

        ExtensionRule(FileCategory.IsoImage, "ISO 光盘镜像文件",
            ".iso", ".img", ".vhd", ".vhdx", ".dmg", ".mdf", ".nrg"),

        ExtensionRule(FileCategory.Installer, "安装包",
            ".msi", ".msix", ".appx", ".msu", ".exe", ".dll", ".apk", ".deb", ".rpm", ".pkg", ".dmg"),

        ExtensionRule(FileCategory.Model3D, "3D 模型/场景资源文件",
            ".obj", ".fbx", ".stl", ".dae", ".3ds", ".gltf", ".glb", ".ply", ".usd", ".usda", ".usdc", ".usdz", ".abc", ".step", ".stp", ".iges"),

        ExtensionRule(FileCategory.Log, "日志文件",
            ".log", ".log1", ".log2", ".txt", ".trace", ".etl", ".dmp", ".mdmp", ".wer"),

        ExtensionRule(FileCategory.TempFile, "临时文件，通常可以安全清理",
            ".tmp", ".temp", ".bak", ".old", ".cache", ".crdownload", ".part", ".partial", ".~tmp", ".swp", ".tmpfile"),
    };

    /// <summary>
    /// 路径感知规则 —— 这些规则优先级更高，因为文件夹名
    /// 比扩展名能提供更强的语义信号。
    /// </summary>
    internal static IReadOnlyList<ClassificationRule> DefaultPathRules() => new[]
    {
        PathRule(FileCategory.TencentVideo, "腾讯视频下载的视频资源", 90,
            folderKeywords: new[] { "tencent video", "腾讯视频", "qqlive", "tencentvideo" }),

        PathRule(FileCategory.Steam, "Steam 游戏资源文件，不建议单独删除", 88,
            folderKeywords: new[] { "steamlibrary", "steamapps", "steam", "steam\\common", "steamapps\\common" }),

        PathRule(FileCategory.AdobeCache, "Adobe 软件缓存文件，通常可以安全清理", 86,
            folderKeywords: new[] { "adobe", "media cache", "mediacache", "premiere", "after effects", "common files\\adobe", "adobe\\common" },
            extensionFilter: new[] { ".tmp", ".cache", ".pek", ".cfa", ".mcdb", ".prv", ".xmp", ".aif", ".bmp" }),

        PathRule(FileCategory.AdobeCache, "Adobe 软件缓存/媒体文件，通常可以安全清理", 82,
            folderKeywords: new[] { "adobe", "media cache", "mediacache", "premiere", "after effects" }),

        PathRule(FileCategory.UnrealEngine, "Unreal Engine 工程资源文件", 85,
            folderKeywords: new[] { "ue4", "ue5", "unreal", "unrealeditor", "epic games", "epicgames", "engine\\content", "saved\\cooked" }),

        PathRule(FileCategory.Unity, "Unity 工程资源文件", 84,
            folderKeywords: new[] { "unity", "unityhub", "assetstore", "library\\artifacts" }),

        PathRule(FileCategory.Maya, "Maya 3D 工程文件，可能属于角色/模型项目", 84,
            folderKeywords: new[] { "maya", "autodesk\\maya", "scenes", "maya projects" }),

        PathRule(FileCategory.Blender, "Blender 工程文件", 84,
            folderKeywords: new[] { "blender", "blender foundation" }),

        PathRule(FileCategory.Model3D, "3D 模型/工程资源文件，可能属于角色或场景项目", 70,
            folderKeywords: new[] { "3dprojects", "3d projects", "models", "model", "character01", "character", "characters", "assets", "rigging", "textures" }),

        PathRule(FileCategory.GameResource, "游戏资源文件，不建议单独删除", 68,
            folderKeywords: new[] { "gamedata", "game data", "games", "game", "pak", "content\\paks" }),

        PathRule(FileCategory.Installer, "下载目录中的安装包，安装后可删除", 60,
            folderKeywords: new[] { "downloads", "下载" },
            extensionFilter: new[] { ".exe", ".msi", ".msix", ".appx", ".msu" }),
    };

    private static ClassificationRule ExtensionRule(
        FileCategory category, string description, params string[] extensions)
    {
        var set = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        return new ClassificationRule(
            extensions: set,
            folderKeywords: Array.Empty<string>(),
            priority: 0,
            result: new ClassificationResult(category, description));
    }

    private static ClassificationRule PathRule(
        FileCategory category,
        string description,
        int priority,
        string[] folderKeywords,
        string[]? extensionFilter = null)
    {
        return new ClassificationRule(
            extensions: extensionFilter is null
                ? null
                : new HashSet<string>(extensionFilter, StringComparer.OrdinalIgnoreCase),
            folderKeywords: folderKeywords,
            priority: priority,
            result: new ClassificationResult(category, description));
    }
}

/// <summary>
/// 单条分类规则：可选的扩展名过滤器，加上一组必须出现在路径中的目录关键词。
/// </summary>
public sealed class ClassificationRule
{
    private readonly HashSet<string>? _extensions;
    private readonly string[] _folderKeywords;

    internal ClassificationRule(
        HashSet<string>? extensions,
        IEnumerable<string> folderKeywords,
        int priority,
        ClassificationResult result)    {
        _extensions = extensions;
        _folderKeywords = folderKeywords.ToArray();
        Priority = priority;
        Result = result;
    }

    /// <summary>数值越大越优先。</summary>
    public int Priority { get; }

    /// <summary>规则匹配时得出的分类结果。</summary>
    public ClassificationResult Result { get; }

    /// <summary>
    /// 构造一条基于路径关键词的规则。
    /// 主要用于测试以及调用方自定义规则。
    /// </summary>
    /// <param name="result">匹配后的分类结果。</param>
    /// <param name="priority">优先级，数值越大越先匹配。</param>
    /// <param name="folderKeywords">路径中需要出现的关键词。</param>
    /// <param name="extensions">可选的扩展名过滤（包含前导点号）。</param>
    public static ClassificationRule Create(
        ClassificationResult result,
        int priority,
        IEnumerable<string> folderKeywords,
        IEnumerable<string>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(folderKeywords);

        var keywordList = folderKeywords.ToArray();
        var extensionSet = extensions is null
            ? null
            : new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        return new ClassificationRule(extensionSet, keywordList, priority, result);
    }

    /// <summary>测试辅助：构造路径关键词规则。</summary>
    internal static ClassificationRule CreateForTests(
        ClassificationResult result, int priority, IEnumerable<string> folderKeywords) =>
        Create(result, priority, folderKeywords);

    internal bool Matches(string extension, string[] pathSegments, string fileName)
    {
        if (_extensions is not null)
        {
            if (extension.Length == 0 || !_extensions.Contains(extension))
            {
                return false;
            }
        }

        // 纯扩展名规则（没有路径关键词）在扩展名匹配时即视为命中。
        if (_folderKeywords.Length == 0)
        {
            return _extensions is not null;
        }

        foreach (var keyword in _folderKeywords)
        {
            if (ContainsSegment(pathSegments, keyword) || ContainsInFileName(fileName, keyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSegment(string[] segments, string keyword)
    {
        var normalizedKeyword = keyword.Replace('/', '\\');

        // 多段关键词（例如 “adobe\common”）作为连续子串进行匹配。
        if (normalizedKeyword.Contains('\\'))
        {
            foreach (var segment in segments)
            {
                if (segment.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // 短关键词（如 “3d”“ue4”“pak”）只接受整段相等，
        // 否则会误命中 GUID、缓存目录名等随机十六进制串（例如 “a3d9…”）。
        var exactOnly = normalizedKeyword.Length <= 3;

        foreach (var segment in segments)
        {
            if (segment.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!exactOnly && segment.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 文件名只有在前缀/整名匹配时才作为路径线索使用 ——
    /// 尽量避免“恰好含有某个短片段”造成的误判。
    /// </summary>
    private static bool ContainsInFileName(string fileName, string keyword)
    {
        // 对于较长的关键词（如 “steamlibrary”）允许子串匹配。
        if (keyword.Length > 3)
        {
            return fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        // 短关键词只允许整名（去掉扩展名后）相等。
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>分类结果：类别加上人类可读的说明。</summary>
public readonly record struct ClassificationResult(FileCategory Category, string Description);

/// <summary>可测试的分类器抽象。</summary>
public interface IFileClassifier
{
    /// <summary>对单个文件路径进行分类。</summary>
    ClassificationResult Classify(string fullPath);

    /// <summary>仅返回类别。</summary>
    FileCategory ClassifyCategory(string fullPath);
}
