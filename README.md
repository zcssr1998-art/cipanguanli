# 磁盘管理（Cipanguanli）

Windows 10/11 本地磁盘空间分析器。目标不是“显示一个饼图”，而是回答：**什么东西占空间、它大概是干嘛的、该不该动、去哪里定位。**

## 当前第一版

- 可同时扫描多个固定磁盘，也可添加单独目录
- 自定义大文件阈值，默认 `5 GB`
- 找出所有超过阈值的文件，并按大小降序显示
- 显示逻辑大小 + 实际磁盘占用（压缩/稀疏文件会更准确）
- 根据路径和扩展名自动解释用途与删除风险
- 已覆盖：
  - 腾讯视频 / 爱奇艺 / 优酷缓存与下载
  - 微信文件
  - Maya / ZBrush / Substance / Houdini / Unreal / Unity / XGen / Mari 等 3D/游戏工程
  - 视频、贴图、压缩包、ISO/安装包、虚拟机磁盘、数据库、游戏资源包
  - `safetensors / ckpt / gguf / pth / pt / onnx` 等 AI 模型权重
  - Windows 系统关键文件
- 聚合大文件夹占用，并通过目录名 + 文件类型特征判断“视频资源 / 3D工程 / AI模型目录”等
- 单独列出“清理线索”：下载、播放器缓存、临时缓存、视频/归档类目录
- 双击大文件可在资源管理器中定位
- CSV 导出
- 支持取消扫描
- 默认不删除任何文件

## 为什么没有直接 fork 一个现成软件

开发前检索了 GitHub。最接近目标的是 MIT 许可的 `valley-soft/powertoys-diskanalyzer`：扫描能力成熟，但它同时包含 PowerToys 插件、WinUI 3、Command Palette 和 MSIX 分发，直接 fork 会带来大量本项目不需要的复杂度。

因此采用“复用成熟扫描思路 + 保留轻量桌面程序”的路线：当前仅适配复用了它的磁盘实际占用计算策略，并在此之上实现本项目自己的扫描器、中文用途注释、目录聚合和风险规则。授权见 `THIRD_PARTY_NOTICES.md`。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet build src/Cipanguanli/Cipanguanli.csproj -c Release
dotnet test tests/Cipanguanli.Tests/Cipanguanli.Tests.csproj -c Release
dotnet publish src/Cipanguanli/Cipanguanli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## 测试策略

GitHub Actions 使用真实 `windows-latest` runner：

1. 编译 WPF 项目
2. 执行 xUnit 分类器/扫描器测试
3. 发布 x64 自包含 EXE
4. 直接运行发布后的 `Cipanguanli.exe --self-test`
5. self-test 会在 Windows 临时目录创建测试文件、执行真实扫描、校验排序/分类/目录聚合，并输出 `selftest-report.json`
6. 只有以上步骤都通过才上传可下载构建包

## 安全边界

第一版不会自动删除。原因很简单：大型 `.mb/.abc/.vdb/.uasset/.vhdx/.db` 文件都可能存在工程或业务依赖，仅凭“体积很大”无法证明可以删除。

下一步更值得做的是：重复文件哈希、180/365 天未使用文件、按目录钻取、白名单、迁移到其他盘，以及可选的 AI 二次解释。
