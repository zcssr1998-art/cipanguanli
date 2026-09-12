# Cipanguanli / 磁盘空间管理器

Windows 10/11 x64 本地磁盘分析工具。目标不是做“无脑清理器”，而是先把磁盘空间解释清楚：**什么占空间、它是什么、能不能动、应该删除还是迁移。**

## v0.2 功能

### 1. 大文件扫描

- 自定义阈值，例如 5 GB。
- 扫描一个或多个固定磁盘/目录。
- 大文件按大小降序。
- 同时显示逻辑大小和 NTFS 实际占用。
- 文件用途中文解释 + 风险等级。
- 资源管理器定位、复制路径、CSV 导出。

### 2. 可视化空间地图

扫描后生成分层目录树：

- 显示大目录占扫描根目录的百分比。
- 最多展开 5 层，每层仅保留显著占用目录，避免百万目录把 UI 卡死。
- 双击节点直接打开文件夹。

### 3. 长期未使用的大文件

可自定义：

- 未修改天数：默认 180 天，也可填 365 等。
- 最小文件大小：默认 1 GB。

注意：**长期未修改不等于可删除。** 该页只是帮助发现被遗忘的大文件。

### 4. 重复文件检测

- 自定义最小重复文件大小，默认 100 MB。
- 先按文件大小筛选候选，再做 SHA-256 全文件哈希。
- 只有字节级完全一致的文件才归为重复组。
- 显示理论可回收空间。
- 第一版只识别、不自动批量删除。

### 5. 更细的应用缓存识别

当前包含：

- 腾讯视频 / 爱奇艺 / 优酷
- 微信 / Telegram / Discord
- Chrome / Edge / Firefox
- NVIDIA / DirectX shader cache
- Adobe Media Cache / Camera Raw Cache
- Unreal DerivedDataCache
- Unity Library 生成缓存
- npm / pip / Gradle / NuGet / Yarn 缓存
- Hugging Face / Torch 模型缓存
- Windows Update / Delivery Optimization 缓存
- Steam / Epic 游戏资源
- Maya / ZBrush / Substance / Houdini / Unreal / Unity / MetaHuman / XGen / Mari 工程
- AI 模型权重（safetensors / gguf / ckpt / pth / onnx 等）

识别顺序刻意采用：**明确软件目录 > 文件内容语义 > 通用 Cache/Temp 路径**，避免把临时目录里的 3D 工程或视频误判成普通缓存。

### 6. 安全迁移

大文件、大文件夹、长期未使用文件均可使用“迁移到…”：

- 同盘：直接移动。
- 跨盘：先复制全部内容并校验文件大小，再删除源。
- 如果目标已完整复制但源删除失败，保留两份，不冒险丢数据。
- 系统关键文件（如 pagefile.sys / hiberfil.sys）禁止迁移。

## 安全边界

本工具仍然不会提供“扫描完一键全删”。删除判断需要结合项目依赖、应用状态、云同步规则等上下文。

重复文件页同样只负责识别；后续如加入批量删除，会默认进入回收站并提供强确认。

## 构建

项目为 `.NET 8 + WPF`：

```powershell
dotnet build .\src\Cipanguanli\Cipanguanli.csproj -c Release
```

发布独立 x64 EXE：

```powershell
dotnet publish .\src\Cipanguanli\Cipanguanli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 自动测试

GitHub Actions `windows-latest` 会依次执行：

1. Release Build
2. xUnit 单元测试
3. 发布 self-contained EXE
4. 直接运行打包后的 EXE 功能自测（扫描 / 分类 / 旧文件 / 重复文件 / 迁移）
5. 直接启动 WPF 主窗口进行 UI smoke test
6. 两项均通过后才打包 Windows artifact

## 第三方代码

底层 NTFS 实际占用计算方案参考/改编自 MIT 许可的 ValleySoft DiskAnalyzer。详见 `THIRD_PARTY_NOTICES.md`。
