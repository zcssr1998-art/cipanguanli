using MiniDiskLab.Core;
using MiniDiskLab.Core.Models;

namespace MiniDiskLab.Tests;

/// <summary>
/// <see cref="SizeConverter"/> 的单元测试 —— 覆盖任务要求的大小格式化场景。
/// </summary>
public class SizeConverterTests
{
    // ---------- Format ----------

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1,023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(2048, "2 KB")]
    [InlineData(1536, "1.5 KB")]
    public void Format_BytesAndKilobytes(long bytes, string expected)
    {
        Assert.Equal(expected, SizeConverter.Format(bytes));
    }

    [Fact]
    public void Format_OneMegabyte_IsOneMB()
    {
        Assert.Equal("1 MB", SizeConverter.Format(SizeConverter.Megabyte));
    }

    [Fact]
    public void Format_OneGigabyte_IsOneGB()
    {
        Assert.Equal("1 GB", SizeConverter.Format(SizeConverter.Gigabyte));
    }

    [Fact]
    public void Format_OneTerabyte_IsOneTB()
    {
        Assert.Equal("1 TB", SizeConverter.Format(SizeConverter.Terabyte));
    }

    [Fact]
    public void Format_5Point3Gigabytes()
    {
        // 5.3 GB -> 注意这是二进制换算（IEC），5.3 * 1024^3。
        var bytes = (long)(5.3 * SizeConverter.Gigabyte);
        var text = SizeConverter.Format(bytes);

        // 展示为 5.3 GB（允许标度尾数差异）。
        Assert.EndsWith("GB", text);
        Assert.StartsWith("5.3", text);
    }

    [Fact]
    public void Format_GigabytesWithDecimals()
    {
        var bytes = (long)(1.5 * SizeConverter.Gigabyte);
        Assert.Equal("1.5 GB", SizeConverter.Format(bytes));
    }

    [Fact]
    public void Format_MegabytesWithDecimals()
    {
        var bytes = (long)(2.25 * SizeConverter.Megabyte);
        Assert.Equal("2.25 MB", SizeConverter.Format(bytes));
    }

    [Fact]
    public void Format_NegativeValue_IsPrefixedWithMinus()
    {
        Assert.Equal("-1 KB", SizeConverter.Format(-1024));
    }

    [Fact]
    public void Format_ThresholdValues_AreReadable()
    {
        Assert.Equal("499 MB", SizeConverter.Format(499 * SizeConverter.Megabyte));
        Assert.Equal("500 MB", SizeConverter.Format(500 * SizeConverter.Megabyte));
        Assert.Equal("501 MB", SizeConverter.Format(501 * SizeConverter.Megabyte));
    }

    // ---------- UnitToBytes ----------

    [Fact]
    public void UnitToBytes_Megabytes()
    {
        Assert.Equal(500L * 1024 * 1024, SizeConverter.UnitToBytes(500, SizeUnit.MB));
    }

    [Fact]
    public void UnitToBytes_Gigabytes()
    {
        Assert.Equal(2L * 1024 * 1024 * 1024, SizeConverter.UnitToBytes(2, SizeUnit.GB));
    }

    [Fact]
    public void UnitToBytes_Kilobytes()
    {
        Assert.Equal(1024L, SizeConverter.UnitToBytes(1, SizeUnit.KB));
    }

    [Fact]
    public void UnitToBytes_HalfMegabyte_RoundsAwayFromZero()
    {
        Assert.Equal(512L * 1024, SizeConverter.UnitToBytes(0.5, SizeUnit.MB));
    }

    [Fact]
    public void UnitToBytes_Zero_IsZero()
    {
        Assert.Equal(0L, SizeConverter.UnitToBytes(0, SizeUnit.MB));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnitToBytes_InvalidValues_Throw(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SizeConverter.UnitToBytes(value, SizeUnit.MB));
    }

    [Fact]
    public void UnitToBytes_UnknownUnit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SizeConverter.UnitToBytes(1, (SizeUnit)999));
    }

    // ---------- UnitLabel ----------

    [Theory]
    [InlineData(SizeUnit.KB, "KB")]
    [InlineData(SizeUnit.MB, "MB")]
    [InlineData(SizeUnit.GB, "GB")]
    public void UnitLabel_ReturnsShortName(SizeUnit unit, string expected)
    {
        Assert.Equal(expected, SizeConverter.UnitLabel(unit));
    }

    // ---------- ToBestUnit ----------

    [Fact]
    public void ToBestUnit_SelectsSensibleUnit()
    {
        Assert.Equal("B", SizeConverter.ToBestUnit(512).Unit);
        Assert.Equal("KB", SizeConverter.ToBestUnit(2048).Unit);
        Assert.Equal("MB", SizeConverter.ToBestUnit(5 * SizeConverter.Megabyte).Unit);
        Assert.Equal("GB", SizeConverter.ToBestUnit(5 * SizeConverter.Gigabyte).Unit);
        Assert.Equal("TB", SizeConverter.ToBestUnit(5 * SizeConverter.Terabyte).Unit);
    }
}
