using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;

namespace MiniDiskLab.App;

/// <summary>
/// 结果表格的一行。包装 <see cref="ScanResultItem"/>，
/// 为界面补充显示字段，且不污染核心模型。
/// </summary>
public sealed class ResultRow
{
    public ResultRow(ScanResultItem item)
    {
        Item = item;
    }

    /// <summary>底层扫描结果。</summary>
    public ScanResultItem Item { get; }

    /// <summary>格式化后的大小，例如 <c>1.42 GB</c>。</summary>
    public string SizeText => Item.SizeText;

    /// <summary>大小的中文类型标签。</summary>
    public string CategoryLabel => ResultExporter.GetCategoryLabel(Item.Category);

    /// <summary>自动生成的说明。</summary>
    public string Description => Item.Description;

    /// <summary>不含目录的文件名。</summary>
    public string FileName => Item.FileName;

    /// <summary>完整路径。</summary>
    public string FullPath => Item.FullPath;

    /// <summary>格式化后的修改时间。</summary>
    public string LastWriteText =>
        Item.LastWriteTime?.ToString("yyyy-MM-dd HH:mm") ?? "-";

    /// <summary>大小（字节），用于排序。</summary>
    public long SizeBytes => Item.SizeBytes;

    /// <summary>用于搜索的合并文本。</summary>
    public string SearchText => $"{Item.FileName}\n{Item.FullPath}\n{Item.Description}";
}
