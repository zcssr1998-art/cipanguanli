# MiniDiskLab

一个用于 Windows 的小型磁盘大文件分析工具。选择任意文件夹、设定“大文件阈值”，
即可扫描出所有达到阈值的大文件，并自动给出**人类可读的中文说明**，最后可导出 CSV / JSON。

界面使用 WPF 编写，扫描与分类逻辑全部放在与界面无关的 Core 类库中，并有完整的 xUnit 自动化测试。

---

## 1. 它能做什么

- **选择目录**：通过文件夹选择对话框挑选目录，也可以一键改为扫描整块盘符的根目录。
- **设置大文件阈值**：输入数值 + 选择单位（KB / MB / GB），例如 `500 MB`。
- **递归扫描**：在后台线程遍历整个目录树，界面始终保持响应（不会假死）。
- **找出大文件并降序排列**：只列出 `大小 >= 阈值` 的文件，按体积从大到小排序。
- **自动说明（离线规则，不联网、不调用在线 AI）**：综合“扩展名 + 完整路径 + 所在文件夹名”
  推断文件类别，并生成一句中文说明，例如：

  | 文件 | 类型 | 自动说明 |
  |---|---|---|
  | `Tencent Video/Download/movie.mp4` | 腾讯视频 | 腾讯视频下载的视频资源 |
  | `SteamLibrary/steamapps/common/TestGame/data.pak` | Steam | Steam 游戏资源文件，不建议单独删除 |
  | `3DProjects/Hero/maya/body.ma` | Maya | Maya 3D 工程文件，可能属于角色/模型项目 |
  | `Downloads/windows.iso` | ISO 镜像 | ISO 光盘镜像文件 |
  | `Adobe/Cache/cache.tmp` | Adobe 缓存 | Adobe 软件缓存文件，通常可以安全清理 |
  | `Random/unknown.xyz` | 未知文件 | 未知文件（.xyz） |

- **随时取消**：点击“取消”会真正终止后台扫描线程（不是仅仅不再刷新界面）。
- **实时状态**：显示当前扫描位置、已扫描文件数、发现的大文件数、跳过的无权限目录数、
  扫描速度（文件/秒）与已耗时。
- **导出结果**：CSV（带 UTF-8 BOM，Excel 直接双击不乱码）与 JSON。

### 支持的 17 种文件类别

视频、图片、压缩包、游戏资源、Steam、腾讯视频、Adobe 缓存、Unreal Engine、
Unity、Maya、Blender、3D 模型资源、安装包、ISO 镜像、日志、临时文件、未知文件。

---

## 2. 使用的技术

| 项 | 说明 |
|---|---|
| 运行时 | .NET 8（`net8.0-windows`） |
| 界面 | WPF（`UseWPF`），同时启用 WinForms 仅用于 `FolderBrowserDialog` |
| 测试 | xUnit 2.9.2 + `Microsoft.NET.Test.Sdk` 17.11.1 + `xunit.runner.visualstudio` 2.8.2 |
| 稀疏文件 | Win32 `DeviceIoControl(FSCTL_SET_SPARSE)`，用于生成“逻辑上很大、几乎不占磁盘”的测试数据 |
| 磁盘占用测量 | `GetCompressedFileSizeW` |
| 目录遍历 | `Directory.EnumerateFiles/EnumerateDirectories` + `EnumerationOptions` |
| 并发 | 显式栈迭代 DFS + `CancellationToken` + `IProgress<T>` 节流 |

### 关键工程实现

- **不卡界面**：扫描通过 `Task.Run` 在后台线程执行；UI 更新用 `DispatcherTimer`（150 ms）
  合并，避免上千次/秒的进度回调拖垮界面。
- **权限错误不崩溃**：所有目录/文件级别的 IO 异常都被单独捕获、计数并跳过，扫描继续。
  触发崩溃的只有“根目录不存在”这类调用方错误。
- **不用递归**：使用显式 `Stack<string>` 做深度优先遍历，因此再深的目录树也不会栈溢出。
- **目录循环保护**：通过 `DirectoryInfo.ResolveLinkTarget(returnFinalTarget: true)` 解析重解析点
  目标，用 `HashSet` 记录已访问目标，`A → B → A` 这类链接循环会被识别并跳过。
- **超长路径**：`app.manifest` 中声明 `longPathAware=true`。
- **真正可取消**：`CancellationToken` 一路传入扫描循环；取消时返回 `WasCancelled = true`
  的结果摘要，而不是抛异常。
- **分类规则优先级**：路径规则（如 `steamapps`、`腾讯视频`）比单纯扩展名更可靠，因此优先级更高；
  同时候选规则按 `Priority` 取最高分。短关键词（≤3 字符，如 `3d`）只做整段匹配，
  避免误命中 GUID 等随机字符串。

---

## 3. 如何运行

需要 .NET 8 SDK（或 .NET 8 运行时不加 SDK 时用 `dotnet run` 需 SDK）。

```bash
# 1. 还原 + 编译整个解决方案
dotnet build MiniDiskLab.sln -c Release

# 2. 启动图形界面
dotnet run --project src/MiniDiskLab.App/MiniDiskLab.App.csproj -c Release
```

也可以直接运行编译产物：

```
src/MiniDiskLab.App/bin/Release/net8.0-windows/MiniDiskLab.exe
```

### 使用方法

1. 点击“选择…”，选一个文件夹（或点击“整个盘符”）。
2. 在“大文件阈值”输入 `500`，单位选 `MB`。
3. 点击“开始扫描”。扫描过程中可以随时点“取消”。
4. 结果表按大小降序显示：大小 / 类型 / 说明 / 文件名 / 完整路径 / 修改时间。
5. 点击底部“导出 CSV”保存结果。

> **安全承诺：本工具只读取文件元数据，绝不删除或修改任何真实文件。**

---

## 4. 如何测试

```bash
# 运行全部 157 个单元测试
dotnet test MiniDiskLab.sln -c Release

# 只跑某一类
dotnet test tests/MiniDiskLab.Tests/MiniDiskLab.Tests.csproj -c Release --filter "FullyQualifiedName~FileClassifierTests"
```

### 两种无界面自检模式（用于 CI / 无法操作 GUI 的环境）

```bash
# 核心流程自检：生成测试数据 → 扫描 → 边界 → 排序 → 分类 → 导出
MiniDiskLab.exe --self-test --data=<数据目录> --report=<报告.json>

# 界面自检：真实构造 MainWindow，核对所有要求的控件与主题资源
MiniDiskLab.exe --ui-check
```

两者都以退出码返回结果：`0` 表示全部通过，`2` 表示有失败项。

### 测试数据

测试所需的样例文件可以由程序自己生成（**不会**占用真实磁盘空间）：

- 界面上的“生成测试数据”按钮；
- 或 `--self-test` 自动生成；
- 或代码里调用 `TestDataFactory.Create(dir)`。

生成的文件绝大多数是**稀疏文件**：逻辑大小合计约 11 GB，实际磁盘占用接近 0 MB。
默认布局与任务文档一致：

```
Tencent Video/Download/movie.mp4                              620 MB
SteamLibrary/steamapps/common/TestGame/data.pak              1450 MB
3DProjects/Hero/maya/body.ma                                  512 MB
Downloads/windows.iso                                        2200 MB
Adobe/Cache/cache.tmp                                         700 MB
Random/unknown.xyz                                            640 MB
Mixed/boundary_499MB.bin  /  boundary_500MB.bin  /  501MB.bin
...（共 20 个文件）
```

---

## 5. 项目结构

```
MiniDiskLab/
├── AGENT_TEST_TASK.txt              # 任务文档（未修改）
├── README.md                        # 本文件
├── TEST_REPORT.md                   # 真实测试报告
├── MiniDiskLab.sln
├── build.cmd                        # Windows 上的编译入口
├── src/
│   ├── MiniDiskLab.Core/            # 与界面无关的核心逻辑
│   │   ├── SizeConverter.cs             # 大小格式化（1024 B → "1 KB" 等）
│   │   ├── Models/
│   │   │   ├── ScanResultItem.cs        # FileCategory 枚举、阈值、结果项
│   │   │   └── ScanProgress.cs          # 进度与摘要（含扩展名统计、Top 目录）
│   │   ├── Classifiers/
│   │   │   └── FileClassifier.cs        # 扩展名 + 路径规则分类器
│   │   ├── Services/
│   │   │   ├── DiskScanner.cs           # 可取消、容错的目录扫描
│   │   │   └── ResultExporter.cs        # CSV / JSON 导出
│   │   └── Utilities/
│   │       ├── SparseFileWriter.cs      # 稀疏文件生成
│   │       └── TestDataFactory.cs       # 测试数据布局
│   └── MiniDiskLab.App/             # WPF 界面层
│       ├── App.xaml(.cs)                # 入口，含 --self-test / --ui-check
│       ├── MainWindow.xaml(.cs)         # 主窗口（只负责展示与转发）
│       ├── ThemeManager.cs              # 浅色 / 深色主题
│       ├── AppSettingsStore.cs          # 记住上次目录与阈值
│       ├── ResultRow.cs                 # 表格行视图模型
│       ├── HeadlessSelfTest.cs          # 无界面核心自检
│       ├── UiSmokeCheck.cs              # 界面构造自检
│       └── GlobalUsings.cs              # WPF/WinForms 同名类型别名
└── tests/
    └── MiniDiskLab.Tests/           # 157 个 xUnit 测试
        ├── SizeConverterTests.cs        # 大小格式化
        ├── FileClassifierTests.cs       # 分类器
        ├── ThresholdTests.cs            # 阈值边界
        ├── DiskScannerTests.cs          # 扫描器（取消/循环/长路径/上限）
        ├── ExceptionHandlingTests.cs    # 异常与特殊文件
        └── ExporterTests.cs             # CSV / JSON 导出
```

---

## 6. 额外功能（在基础要求之外）

- 实时增量结果显示（扫描过程中结果就陆续出现）
- 扫描速度（文件/秒）与耗时统计
- 扩展名统计导出
- Top 10 最大目录
- 结果搜索 + 按类型筛选
- JSON 导出
- 深色 / 浅色主题切换
- 记住上次扫描目录与阈值

---

## 7. 已知限制

- 界面本身依赖 Windows 桌面环境；在没有桌面的会话里只能用 `--ui-check` / `--self-test`
  验证“窗口能否正常构造”和“核心流程是否正确”，无法模拟鼠标点击。
- 扫描以文件为粒度统计；目录大小统计是按“文件直接父目录”汇总，不含子目录递归合计。
