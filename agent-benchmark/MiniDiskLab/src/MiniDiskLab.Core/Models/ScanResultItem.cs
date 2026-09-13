namespace MiniDiskLab.Core.Models;

/// <summary>
/// 文件的内容类别。用于 UI 上的“类型”列。
/// </summary>
public enum FileCategory
{
    Unknown,
    Video,
    Image,
    Archive,
    GameResource,
    Steam,
    TencentVideo,
    AdobeCache,
    UnrealEngine,
    Unity,
    Maya,
    Blender,
    Model3D,
    Installer,
    IsoImage,
    Log,
    TempFile,
}

/// <summary>
/// 与大文件阈值一起使用的尺寸单位。
/// </summary>
public enum SizeUnit
{
    KB = 0,
    MB = 1,
    GB = 2,
}

/// <summary>
/// 按阈值单位表达的阈值，例如 <c>500 MB</c>。
/// </summary>
public readonly record struct SizeThreshold(double Value, SizeUnit Unit)
{
    /// <summary>将阈值转换为字节。</summary>
    public long ToBytes() => SizeConverter.UnitToBytes(Value, Unit);

    /// <summary>面向用户的描述，例如 <c>500 MB</c>。</summary>
    public override string ToString() => $"{Value:0.##} {SizeConverter.UnitLabel(Unit)}";

    /// <summary>常用默认值：500 MB。</summary>
    public static SizeThreshold Default => new(500d, SizeUnit.MB);
}

/// <summary>
/// 扫描期间发现的一个符合“大文件”条件的文件。
/// </summary>
public sealed class ScanResultItem
{
    /// <summary>不含目录前缀的文件名。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>完整路径。</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>字节数。</summary>
    public long SizeBytes { get; init; }

    /// <summary>小写扩展名，包含前导点号；无扩展名时为空字符串。</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>由分类器得出的类别。</summary>
    public FileCategory Category { get; init; } = FileCategory.Unknown;

    /// <summary>由分类器得出的中文说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>文件最后写入时间；无法获取时为 <see langword="null"/>。</summary>
    public DateTime? LastWriteTime { get; init; }

    /// <summary>面向 UI 的格式化大小，例如 <c>1.50 GB</c>。</summary>
    public string SizeText => SizeConverter.Format(SizeBytes);
}
