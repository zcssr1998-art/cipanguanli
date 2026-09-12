using Cipanguanli.Core;

namespace Cipanguanli.Tests;

public sealed class FileClassifierTests
{
    [Theory]
    [InlineData(@"D:\Tencent Video\QLDownload\movie.mp4", "腾讯视频")]
    [InlineData(@"D:\Projects\Maya\Hero\hero.mb", "3D")]
    [InlineData(@"D:\AI\models\qwen.gguf", "AI 模型")]
    [InlineData(@"D:\Movies\demo.mkv", "视频")]
    public void ClassifiesCommonLargeFiles(string path, string expected)
    {
        var result = FileClassifier.ClassifyFile(path);
        Assert.Contains(expected, result.Category);
    }

    [Fact]
    public void ProtectsWindowsPagingFile()
    {
        var result = FileClassifier.ClassifyFile(@"C:\pagefile.sys");
        Assert.Contains("系统", result.Category);
        Assert.Contains("禁止", result.Risk);
    }

    [Fact]
    public void DetectsThreeDFolderByContentSignature()
    {
        var result = FileClassifier.ClassifyFolder(@"D:\Work\Hero", 1000, 800, 0, 0, 0);
        Assert.Contains("3D", result.Category);
        Assert.False(result.CleanupCandidate);
    }
}
