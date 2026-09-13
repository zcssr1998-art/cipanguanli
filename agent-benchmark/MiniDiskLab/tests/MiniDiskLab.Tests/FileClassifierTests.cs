using MiniDiskLab.Core.Classifiers;
using MiniDiskLab.Core.Models;
using MiniDiskLab.Core.Services;

namespace MiniDiskLab.Tests;

/// <summary>
/// <see cref="FileClassifier"/> 的单元测试。
/// 覆盖任务文档中列出的全部关键字示例与类别。
/// </summary>
public class FileClassifierTests
{
    private readonly FileClassifier _classifier = new();

    // ==========================================================
    //  任务文档中的三个显式示例
    // ==========================================================

    [Fact]
    public void TencentVideo_Download_Mp4_IsRecognisedAsTencentVideo()
    {
        var result = _classifier.Classify(@"D:\Tencent Video\Download\xxx.mp4");

        Assert.Equal(FileCategory.TencentVideo, result.Category);
        Assert.Contains("腾讯视频", result.Description);
    }

    [Fact]
    public void MayaProject_Ma_File_MentionsMayaAndCharacterProject()
    {
        var result = _classifier.Classify(@"D:\Projects\Character01\maya\body.ma");

        Assert.Equal(FileCategory.Maya, result.Category);
        Assert.Contains("Maya", result.Description);
    }

    [Fact]
    public void SteamLibrary_Pak_IsWarnedAgainstDeletion()
    {
        var result = _classifier.Classify(@"D:\SteamLibrary\steamapps\common\Game\xxx.pak");

        Assert.Equal(FileCategory.Steam, result.Category);
        Assert.Contains("Steam", result.Description);
        Assert.Contains("不建议", result.Description);
    }

    // ==========================================================
    //  每个必需类别至少一个用例
    // ==========================================================

    [Theory]
    // 视频
    [InlineData(@"D:\Movies\holiday.mp4", FileCategory.Video)]
    [InlineData(@"D:\Movies\holiday.mkv", FileCategory.Video)]
    [InlineData(@"D:\Movies\clip.avi", FileCategory.Video)]
    // 图片
    [InlineData(@"D:\Photos\p1.jpg", FileCategory.Image)]
    [InlineData(@"D:\Photos\p1.png", FileCategory.Image)]
    [InlineData(@"D:\Photos\p1.raw", FileCategory.Image)]
    // 压缩包
    [InlineData(@"D:\Backup\data.zip", FileCategory.Archive)]
    [InlineData(@"D:\Backup\data.7z", FileCategory.Archive)]
    [InlineData(@"D:\Backup\data.rar", FileCategory.Archive)]
    // ISO 镜像
    [InlineData(@"D:\Downloads\windows.iso", FileCategory.IsoImage)]
    [InlineData(@"D:\Images\disk.img", FileCategory.IsoImage)]
    // 安装包
    [InlineData(@"D:\Downloads\setup.msi", FileCategory.Installer)]
    [InlineData(@"D:\Downloads\app.msix", FileCategory.Installer)]
    // 日志
    [InlineData(@"D:\Logs\server.log", FileCategory.Log)]
    // 临时文件
    [InlineData(@"D:\Temp\x.tmp", FileCategory.TempFile)]
    // 3D 模型
    [InlineData(@"D:\Models\chair.obj", FileCategory.Model3D)]
    [InlineData(@"D:\Models\chair.fbx", FileCategory.Model3D)]
    // 未知
    [InlineData(@"D:\Random\unknown.xyz", FileCategory.Unknown)]
    public void ExtensionBasedCategories(string path, FileCategory expected)
    {
        Assert.Equal(expected, _classifier.ClassifyCategory(path));
    }

    [Theory]
    // Steam
    [InlineData(@"D:\SteamLibrary\steamapps\common\Game\data.pak", FileCategory.Steam)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\downloading\x.bin", FileCategory.Steam)]
    // 腾讯视频
    [InlineData(@"E:\Tencent Video\Cache\preview.mp4", FileCategory.TencentVideo)]
    [InlineData(@"D:\腾讯视频\Download\a.mp4", FileCategory.TencentVideo)]
    // Adobe 缓存
    [InlineData(@"C:\Users\me\AppData\Roaming\Adobe\Common\Media Cache\c.pek", FileCategory.AdobeCache)]
    [InlineData(@"D:\Adobe\Cache\cache.tmp", FileCategory.AdobeCache)]
    // Unreal Engine
    [InlineData(@"D:\UnrealProjects\Game\Content\Paks\pakchunk0.pak", FileCategory.UnrealEngine)]
    [InlineData(@"D:\UE5\Epic Games\Engine\Content\x.uasset", FileCategory.UnrealEngine)]
    // Unity
    [InlineData(@"D:\UnityProjects\MyGame\Library\artifacts\a.asset", FileCategory.Unity)]
    // Maya
    [InlineData(@"D:\Projects\Hero\maya\scenes\body.ma", FileCategory.Maya)]
    // Blender
    [InlineData(@"D:\Projects\Hero\blender\hero.blend", FileCategory.Blender)]
    // 游戏资源
    [InlineData(@"D:\Games\SomeGame\GameData\level.pak", FileCategory.GameResource)]
    // 3D 模型资源（路径线索）
    [InlineData(@"D:\3DProjects\Hero\textures\hero_diffuse.png", FileCategory.Model3D)]
    public void PathBasedCategories(string path, FileCategory expected)
    {
        Assert.Equal(expected, _classifier.ClassifyCategory(path));
    }

    // ==========================================================
    //  优先级与细节
    // ==========================================================

    [Fact]
    public void PathRuleBeatsExtensionRule()
    {
        // 纯扩展名判断会得到 Video，但路径表明这是腾讯视频下载资源。
        var result = _classifier.Classify(@"D:\Tencent Video\Download\movie.mp4");

        Assert.Equal(FileCategory.TencentVideo, result.Category);
        Assert.NotEqual(FileCategory.Video, result.Category);
    }

    [Fact]
    public void SteamPathBeatsGenericPak()
    {
        var result = _classifier.Classify(@"D:\SteamLibrary\steamapps\common\Game\data.pak");

        Assert.Equal(FileCategory.Steam, result.Category);
    }

    [Fact]
    public void DescriptionIsNeverEmpty()
    {
        foreach (var path in AllSamplePaths)
        {
            var result = _classifier.Classify(path);
            Assert.False(string.IsNullOrWhiteSpace(result.Description),
                $"描述为空: {path}");
        }
    }

    [Fact]
    public void UnknownCategory_StillHasDescription()
    {
        var result = _classifier.Classify(@"D:\Random\mystery.qqqq");

        Assert.Equal(FileCategory.Unknown, result.Category);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public void EmptyPath_IsUnknown()
    {
        var result = _classifier.Classify(string.Empty);

        Assert.Equal(FileCategory.Unknown, result.Category);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public void ForwardSlashesAreNormalised()
    {
        var result = _classifier.Classify("D:/Tencent Video/Download/movie.mp4");

        Assert.Equal(FileCategory.TencentVideo, result.Category);
    }

    [Fact]
    public void ClassificationIsCaseInsensitive()
    {
        var lower = _classifier.Classify(@"d:\steamlibrary\steamapps\common\game\data.PAK");
        var upper = _classifier.Classify(@"D:\STEAMLIBRARY\STEAMAPPS\COMMON\GAME\DATA.PAK");

        Assert.Equal(FileCategory.Steam, lower.Category);
        Assert.Equal(FileCategory.Steam, upper.Category);
    }

    [Theory]
    [InlineData("noextension")]
    [InlineData(".gitignore")]
    [InlineData("trailing.")]
    public void OddFileNames_DoNotThrow(string fileName)
    {
        var result = _classifier.Classify(@"D:\Random\" + fileName);

        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    [Fact]
    public void EveryMandatoryCategoryIsReachable()
    {
        // 任务文档要求至少支持这些类别。
        var required = new[]
        {
            FileCategory.Video,
            FileCategory.Image,
            FileCategory.Archive,
            FileCategory.GameResource,
            FileCategory.Steam,
            FileCategory.TencentVideo,
            FileCategory.AdobeCache,
            FileCategory.UnrealEngine,
            FileCategory.Unity,
            FileCategory.Maya,
            FileCategory.Blender,
            FileCategory.Model3D,
            FileCategory.Installer,
            FileCategory.IsoImage,
            FileCategory.Log,
            FileCategory.TempFile,
            FileCategory.Unknown,
        };

        var samples = new Dictionary<FileCategory, string>
        {
            [FileCategory.Video] = @"D:\M\a.mp4",
            [FileCategory.Image] = @"D:\P\a.jpg",
            [FileCategory.Archive] = @"D:\B\a.zip",
            [FileCategory.GameResource] = @"D:\Games\G\GameData\a.pak",
            [FileCategory.Steam] = @"D:\SteamLibrary\steamapps\common\G\a.pak",
            [FileCategory.TencentVideo] = @"D:\Tencent Video\Download\a.mp4",
            [FileCategory.AdobeCache] = @"D:\Adobe\Cache\a.pek",
            [FileCategory.UnrealEngine] = @"D:\UnrealProjects\G\Content\Paks\a.pak",
            [FileCategory.Unity] = @"D:\UnityProjects\G\Library\a.asset",
            [FileCategory.Maya] = @"D:\P\Hero\maya\a.ma",
            [FileCategory.Blender] = @"D:\P\Hero\blender\a.blend",
            [FileCategory.Model3D] = @"D:\Models\a.obj",
            [FileCategory.Installer] = @"D:\D\a.msi",
            [FileCategory.IsoImage] = @"D:\D\a.iso",
            [FileCategory.Log] = @"D:\L\a.log",
            [FileCategory.TempFile] = @"D:\T\a.tmp",
            [FileCategory.Unknown] = @"D:\R\a.xyz",
        };

        foreach (var category in required)
        {
            Assert.True(samples.ContainsKey(category), $"缺少 {category} 的样例");
            Assert.Equal(category, _classifier.ClassifyCategory(samples[category]));
        }
    }

    // ==========================================================
    //  标签映射
    // ==========================================================

    [Theory]
    [InlineData(FileCategory.Video, "视频")]
    [InlineData(FileCategory.Image, "图片")]
    [InlineData(FileCategory.Archive, "压缩包")]
    [InlineData(FileCategory.GameResource, "游戏资源")]
    [InlineData(FileCategory.Steam, "Steam")]
    [InlineData(FileCategory.TencentVideo, "腾讯视频")]
    [InlineData(FileCategory.AdobeCache, "Adobe 缓存")]
    [InlineData(FileCategory.UnrealEngine, "Unreal Engine")]
    [InlineData(FileCategory.Unity, "Unity")]
    [InlineData(FileCategory.Maya, "Maya")]
    [InlineData(FileCategory.Blender, "Blender")]
    [InlineData(FileCategory.Model3D, "3D 模型资源")]
    [InlineData(FileCategory.Installer, "安装包")]
    [InlineData(FileCategory.IsoImage, "ISO 镜像")]
    [InlineData(FileCategory.Log, "日志")]
    [InlineData(FileCategory.TempFile, "临时文件")]
    [InlineData(FileCategory.Unknown, "未知文件")]
    public void CategoryLabelsAreChinese(FileCategory category, string expected)
    {
        Assert.Equal(expected, ResultExporter.GetCategoryLabel(category));
    }

    // ==========================================================
    //  自定义规则
    // ==========================================================

    [Fact]
    public void CustomRules_AreHonoured()
    {
        var extensionRules = FileClassifier.DefaultExtensionRules();
        var pathRules = new[]
        {
            new ClassificationRuleBuilder(
                FileCategory.GameResource,
                "自定义规则命中的文件",
                priority: 200,
                folderKeywords: new[] { "mycustomfolder" }).Build(),
        };

        var classifier = new FileClassifier(extensionRules, pathRules);
        var result = classifier.Classify(@"D:\mycustomfolder\file.dat");

        Assert.Equal(FileCategory.GameResource, result.Category);
        Assert.Equal("自定义规则命中的文件", result.Description);
    }

    private static readonly string[] AllSamplePaths =
    {
        @"D:\Tencent Video\Download\movie.mp4",
        @"D:\SteamLibrary\steamapps\common\Game\data.pak",
        @"D:\Projects\Hero\maya\body.ma",
        @"D:\Downloads\windows.iso",
        @"D:\Adobe\Cache\cache.tmp",
        @"D:\Random\unknown.xyz",
        @"D:\Movies\a.mp4",
        @"D:\Photos\a.jpg",
        @"D:\Backup\a.zip",
        @"D:\Logs\a.log",
        @"D:\Temp\a.tmp",
        @"D:\Models\a.obj",
    };
}

/// <summary>
/// 便于在测试中构造公开可用的自定义分类规则。
/// </summary>
internal sealed class ClassificationRuleBuilder
{
    private readonly FileCategory _category;
    private readonly string _description;
    private readonly int _priority;
    private readonly string[] _folderKeywords;

    public ClassificationRuleBuilder(
        FileCategory category,
        string description,
        int priority,
        string[] folderKeywords)
    {
        _category = category;
        _description = description;
        _priority = priority;
        _folderKeywords = folderKeywords;
    }

    public ClassificationRule Build() =>
        ClassificationRule.CreateForTests(
            new ClassificationResult(_category, _description),
            _priority,
            _folderKeywords);
}
