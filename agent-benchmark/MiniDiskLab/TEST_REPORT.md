# TEST_REPORT — MiniDiskLab

本文件记录本次任务的**真实执行过程与结果**。所有数据均来自本机实际运行的命令输出，
未做任何编造。

- **执行时间**：2026-09-12
- **机器**：DESKTOP-RHFCBBR
- **操作系统**：Windows（win32），简体中文
- **运行时**：.NET 8.0.11（`DOTNET_ROOT` 指向本机解压的 .NET SDK 8.0.404）
- **测试框架**：xUnit 2.9.2 + VSTest 17.11.1

---

## 1. 总览

| 项目 | 结果 |
|---|---|
| Build | **PASS**（0 warning / 0 error） |
| Unit Tests | **157 通过 / 0 失败 / 0 跳过（总计 157）** |
| Smoke Test（核心流程，`--self-test`） | **PASS（27/27 检查通过）** |
| Smoke Test（界面构造，`--ui-check`） | **PASS（全部检查通过）** |
| Smoke Test（真实 GUI 人工交互） | **未验证**（见第 6 节说明） |

---

## 2. 实际执行的命令与输出

### 2.1 编译

```
$ dotnet build MiniDiskLab.sln -c Release
```

关键输出：

```
  MiniDiskLab.Core -> ...\src\MiniDiskLab.Core\bin\Release\net8.0-windows\MiniDiskLab.Core.dll
  MiniDiskLab.App  -> ...\src\MiniDiskLab.App\bin\Release\net8.0-windows\MiniDiskLab.dll
  MiniDiskLab.Tests-> ...\tests\MiniDiskLab.Tests\bin\Release\net8.0-windows\MiniDiskLab.Tests.dll

已成功生成。
    0 个警告
    0 个错误
```

结论：**Build PASS**。

### 2.2 单元测试

```
$ dotnet test MiniDiskLab.sln -c Release
```

关键输出：

```
已通过! - 失败:     0，通过:   157，已跳过:     0，总计:   157，持续时间: 2 s - MiniDiskLab.Tests.dll (net8.0)
EXITCODE=0
```

结论：**157 passed / 0 failed**。

测试覆盖范围：

| 测试文件 | 覆盖内容 | 测试方法数 |
|---|---|---|
| `SizeConverterTests.cs` | 大小格式化：1024 B / 1 MB / 1 GB / 5.3 GB、单位换算、非法输入（负数 / NaN / ∞ / 未知单位）抛异常 | 30+ 用例 |
| `FileClassifierTests.cs` | 17 类别的扩展名与路径判定、路径规则优先于扩展名、优先级、大小写不敏感、斜杠归一化、中文标签、每个必需类别都可达 | 40+ 用例 |
| `ThresholdTests.cs` | 阈值边界：499 MB 排除 / 500 MB 包含 / 501 MB 包含 / ±1 字节 / GB 阈值 / 极端阈值 | 10 用例 |
| `DiskScannerTests.cs` | 降序排序、计数正确、空目录、深层嵌套、根目录不存在抛 `DirectoryNotFoundException`、取消真正停止、`maxEntries` 上限、扫描中删除文件/目录、锁定文件、无权限目录被跳过并计数、符号链接循环终止、超长路径、进度单调递增、增量回调、分类集成、扩展名统计、Top 目录 | 30+ 用例 |
| `ExceptionHandlingTests.cs` | 文件不存在 / 被独占锁定 / 扫描中消失 / 特殊字符文件名 / 0 字节文件 / 空文件夹 / 测试数据工厂布局 / 稀疏文件不占满磁盘 | 25+ 用例 |
| `ExporterTests.cs` | CSV 表头与数据行、UTF-8 BOM、逗号与引号转义、空结果、父目录自动创建、JSON 结构、中文不转义、CSV 与 JSON 条目数一致 | 20+ 用例 |

### 2.3 Smoke Test A —— 核心流程（无界面）

```
$ MiniDiskLab.exe --self-test ^
    --data=...\MiniDiskLab\test_data\smoke ^
    --report=...\MiniDiskLab\test_data\smoke_report.json
```

真实输出（节选，中文原样）：

```
MiniDiskLab headless self-test
  数据目录 : ...\MiniDiskLab\test_data\smoke
  报告路径 : ...\MiniDiskLab\test_data\smoke_report.json

[1/6] 生成测试数据 (稀疏文件)
      创建 20 个文件，稀疏文件 19 个
  [PASS] 测试数据已创建 :: 20 files, 19 sparse
      逻辑大小 11,063 MB / 实际占用 0 MB
  [PASS] 稀疏文件未占用等量真实磁盘 :: logical=11,063MB actual=0MB

[2/6] 执行扫描 (阈值 500 MB)
      已扫描文件 20，发现大文件 11，跳过目录 0，耗时 0.02s
  [PASS] 扫描完成且找到大文件 :: 11 items
  [PASS] 扫描未抛出异常且未取消
  [PASS] 进度回调有输出 :: 13 reports
  [PASS] 大文件增量回调与结果数一致 :: 11 vs 11

[3/6] 校验阈值边界 (499/500/501 MB)
  [PASS] 499MB 低于阈值被排除
  [PASS] 500MB 等于阈值被包含
  [PASS] 501MB 高于阈值被包含

[4/6] 校验按大小降序排序
  [PASS] 结果按文件大小降序
      最大文件: windows.iso (2.15 GB) - ISO 光盘镜像文件

[5/6] 校验分类与自动说明
  [PASS] 分类: movie.mp4 -> TencentVideo :: 实际 category=TencentVideo, description="腾讯视频下载的视频资源"
  [PASS] 分类: data.pak -> Steam :: 实际 category=Steam, description="Steam 游戏资源文件，不建议单独删除"
  [PASS] 分类: body.ma -> Maya :: 实际 category=Maya, description="Maya 3D 工程文件，可能属于角色/模型项目"
  [PASS] 分类: windows.iso -> IsoImage :: 实际 category=IsoImage, description="ISO 光盘镜像文件"
  [PASS] 分类: cache.tmp -> AdobeCache :: 实际 category=AdobeCache, description="Adobe 软件缓存文件，通常可以安全清理"
  [PASS] 分类: pakchunk0.pak -> UnrealEngine :: 实际 category=UnrealEngine, description="Unreal Engine 工程资源文件"
  [PASS] 分类: unknown.xyz -> Unknown :: 实际 category=Unknown, description="未知文件（.xyz）"
  [PASS] 每个结果都有自动说明

[6/6] 导出 CSV 与 JSON
  [PASS] CSV 已生成且非空 :: ...\test_data\selftest-export.csv
  [PASS] JSON 已生成且非空 :: ...\test_data\selftest-export.json
  [PASS] CSV 行数包含全部结果 :: 15 lines for 11 items
  [PASS] CSV 含 UTF-8 BOM (Excel 兼容) :: EF BB BF
  [PASS] JSON 项数与扫描结果一致 :: 11 vs 11

[附加] 校验取消功能
  [PASS] 预先取消时能立即停止

[附加] 校验异常文件不导致崩溃
  [PASS] 不存在的文件仍能分类而不抛异常
  [PASS] 扫描过程中删除文件不导致崩溃
  [PASS] 空目录扫描返回 0 结果

===== 自检结果: 27/27 PASS, 0 FAIL =====
EXITCODE=0
```

结论：**Smoke Test A PASS（27/27）**。

### 2.4 Smoke Test B —— 界面构造（无界面）

```
$ MiniDiskLab.exe --ui-check
```

真实输出（节选）：

```
MiniDiskLab UI smoke check

[1/4] MainWindow 构造成功，标题="MiniDiskLab - 磁盘大文件分析工具"
  [PASS] MainWindow XAML 可成功加载
  [PASS] 窗口标题非空
  [PASS] 窗口允许调整大小 :: CanResize

[2/4] 核对任务文档要求的界面元素
  [PASS] 目录选择框 (PathTextBox)
  [PASS] “选择…”按钮 (BrowseButton)
  [PASS] 阈值输入框 (ThresholdTextBox)
  [PASS] 阈值单位下拉 (UnitComboBox)
  [PASS] 开始扫描按钮 (StartButton)
  [PASS] 取消按钮 (CancelButton)
  [PASS] 状态显示 (StatusText)
  [PASS] 进度条 (ScanProgressBar)
  [PASS] 已扫描计数 (ScannedCountText)
  [PASS] 大文件计数 (LargeFileCountText)
  [PASS] 跳过目录计数 (SkippedCountText)
  [PASS] 结果表格 (ResultsGrid)
  [PASS] 导出 CSV 按钮 (ExportCsvButton)
  [PASS] 单位下拉含 KB/MB/GB :: KB/MB/GB
  [PASS] 表格列包含 大小/类型/说明/文件名/完整路径

[3/4] 核对主题资源可解析（浅色 / 深色）
  [PASS] Light 主题画刷全部可解析 :: 7 brushes
  [PASS] Dark 主题画刷全部可解析 :: 7 brushes

[4/4] 核对数值与文本初始化
  [PASS] 阈值默认值可解析为数字 :: 500
  [PASS] 类型筛选下拉已填充 :: 18 项

===== 界面自检结果: 全部 PASS =====
EXITCODE=0
```

结论：**Smoke Test B PASS**。这证明了 `MainWindow.xaml` 能被真实解析、
所有 `DynamicResource` 画刷与 `StaticResource` 样式都能解析、
DataGrid 列与筛选下拉框都正确建立 —— 即界面层不存在运行时构造错误。

### 2.5 稀疏文件真实占用验证

```
$ du -sh test_data
40K     test_data

$ find test_data -type f -printf "%s\n" | awk '{s+=$1} END {printf "apparent total: %.2f GB\n", s/1024/1024/1024}'
apparent total: 10.80 GB
```

**逻辑总大小 10.80 GB，真实磁盘占用仅 40 KB** —— 满足任务文档
“不要占用几 GB 真实磁盘空间”的要求。

### 2.6 导出的 CSV 样例

```
大小(字节),大小,类型,类别,说明,文件名,完整路径,修改时间
2306867200,2.15 GB,ISO 镜像,IsoImage,ISO 光盘镜像文件,windows.iso,...\Downloads\windows.iso,2026-09-12 23:55:36
1520435200,1.42 GB,Steam,Steam,Steam 游戏资源文件，不建议单独删除,data.pak,...\steamapps\common\TestGame\data.pak,...
943718400,900 MB,Unreal Engine,UnrealEngine,Unreal Engine 工程资源文件,pakchunk0.pak,...
817889280,780 MB,压缩包,Archive,压缩包,archive.zip,...
734003200,700 MB,Adobe 缓存,AdobeCache,Adobe 软件缓存文件，通常可以安全清理,cache.tmp,...
```

文件首字节为 `EF BB BF`（UTF-8 BOM），Excel 双击不乱码；中文说明完整保留。

---

## 3. 执行过程中遇到的问题与修复

以下都是本次实际遇到并已解决的问题。按出现顺序记录。

### 3.1 环境层

| # | 问题 | 现象 | 定位与修复 |
|---|---|---|---|
| 1 | `git clone` 超时 | 进程被 SIGTERM（exit 124） | 用 `timeout 120 git clone` 重试并改到临时目录后改名 |
| 2 | 机器上没有 .NET SDK | `dotnet --info` 只有 runtime 6.0.x，无 SDK | 下载并解压 .NET SDK 8.0.404 到独立目录 |
| 3 | 官方 SDK 安装器下载损坏 | 下载文件头部上百万字节为 0，系统报“不是此操作系统平台的有效应用程序” | 放弃该安装器，改用 `dotnet-sdk-8.0.404-win-x64.zip`，用 Python `zipfile` 解压，`dotnet --version` 输出 `8.0.404` |
| 4 | `dotnet restore` 报 `Value cannot be null. (Parameter 'path1')` | 每次还原都崩在 NuGet 内部 | 用 `-v diag` 追到 `NuGetEnvironment.GetFolderPath(SpecialFolder.ProgramFilesX86)`；根因是当前 shell **完全没有设置 `PROGRAMFILES(X86)` 等环境变量**。在启动脚本中补齐 `ProgramData / APPDATA / LOCALAPPDATA / USERPROFILE / ProgramFiles / PROGRAMFILES(X86) / TEMP / TMP` 后解决 |
| 5 | PowerShell 报“已添加项。字典中的关键字: http_proxy 所添加的关键字: HTTP_PROXY” | 环境里存在大小写重复的代理变量 | 在启动脚本中把 `http_proxy` 等小写变量清空 |
| 6 | `bash` 无法执行 Windows `.exe` | `Exec format error` | 改用 PowerShell 工具调用 `dotnet.exe` |
| 7 | PowerShell 的 `&` + 管道调用 .exe 报“无法在管道中间运行文档” | 输出无法捕获 | 改用 `System.Diagnostics.ProcessStartInfo` + `RedirectStandardOutput/Error` |
| 8 | NuGet 缓存目录文件被锁 | `Access to the path '...\testhost.exe' is denied` | 切换 `NUGET_PACKAGES` 到新的缓存目录并完整重新还原 |

### 3.2 编译层

| # | 问题 | 修复 |
|---|---|---|
| 9 | `ClassificationResult?` 相关 CS0266 | 改为 `best.HasValue` / `best.Value` |
| 10 | `StringBuilder.AppendLine` 多参数重载不存在（CS1501） | 改用 `string.Join(",", ...)` |
| 11 | `ScanSummary.ExtensionStats` 类型不匹配（CS1061） | 把类型从字典改为 `IReadOnlyList<ExtensionStat>` |
| 12 | WPF + WinForms 同名类型导致大量 CS0104（`Application` / `MessageBox` / `Color` / `ColorConverter` / `SaveFileDialog` / `SizeConverter` / `TextBox` / `Button` / `ComboBox` / `ProgressBar` / `DataGrid` …） | 新建 `GlobalUsings.cs`，统一用 `global using X = System.Windows...` 别名固定到 WPF 版本 |
| 13 | `WFAC010` DPI 清单警告 | 从 `app.manifest` 移除 `dpiAware/dpiAwareness`，改在 csproj 设 `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` |
| 14 | 测试项目找不到 `FactAttribute`（CS0246） | 新增 `tests/MiniDiskLab.Tests/GlobalUsings.cs`，写入 `global using Xunit;` |
| 15 | 测试代码里写了 `Models.SizeUnit` | 批量修正为 `SizeUnit` 并补 `using MiniDiskLab.Core.Models;` |
| 16 | `App.xaml.cs` 用了 `IOException` 但缺 `using System.IO;` | 补上 using |

### 3.3 测试逻辑层（重点）

首次完整跑测试时是 **134 通过 / 23 失败**。逐个定位根因如下。

#### 问题 A：扩展名规则全部失效（约 15 个失败）

- **现象**：`D:\Photos\p1.jpg`、`D:\Movies\holiday.mp4`、`D:\Backup\data.zip`
  等一律被判为 `Unknown`。
- **根因**：`ClassificationRule.Matches` 里有一段

  ```csharp
  if (_extensions is not null) { if (extension.Length == 0 || !_extensions.Contains(extension)) return false; }
  if (_folderKeywords.Length == 0) { return false; }   // ← 这里
  ```

  纯扩展名规则是以 `folderKeywords: Array.Empty<string>()` 构造的，
  于是**每一条扩展名规则都在这一行直接返回 false**，永远无法命中。
- **修复**：改为 —— 有扩展名且匹配时，若规则没有路径关键词则直接判定命中：

  ```csharp
  if (_folderKeywords.Length == 0) { return _extensions is not null; }
  ```

#### 问题 B：`Downloads/windows.iso` 被误判为安装包

- **现象**：期望 `IsoImage`，实际 `Installer`。
- **根因**：`Downloads` 路径规则（优先级 60）的扩展名过滤里包含了 `.iso`，
  覆盖了 `IsoImage` 的扩展名规则。
- **修复**：把 `.iso / .zip / .rar / .7z` 从该规则的扩展名过滤中移除，
  只保留真正的安装包后缀（`.exe/.msi/.msix/.appx/.msu`）。语义也更合理：
  `Downloads` 里的 `.zip` 应该被归为“压缩包”，而不是“安装包”。

#### 问题 C：Adobe 缓存被误判为 Steam

- **现象**：`AppData\Roaming\Adobe\Common\Media Cache\c.pek` 期望 `AdobeCache`，实际 `Steam`。
- **根因**：Steam 规则里有一个过于宽泛的关键词 `"common"`（优先级 88），
  而 Adobe 规则里恰好有 `Common` 这一段，Steam 优先级更高就赢了。
- **修复**：把 `"common"` 换成 `"steam\common"` / `"steamapps\common"` 这类
  带上下文的组合关键词。

#### 问题 D：路径里随机十六进制串导致误判（真实触发场景）

- **现象**：`TestDataFactory_FullLayoutScansAndClassifies` 中
  `...\MiniDiskLabTests\full\<guid>\Downloads\windows.iso` 被判为 `Model3D`。
  用固定目录名 `abc` 做探针时却完全正常。
- **根因**：`ContainsSegment` 对关键词做**无限制子串匹配**。
  测试临时目录名是一个 GUID，而 GUID 只由 `0-9a-f` 组成，
  很容易包含 `"3d"` 这样的短片段 —— 于是命中了 `Model3D` 规则里的 `"3d"` 关键词。
- **修复**：短关键词（长度 ≤ 3，如 `3d`、`ue4`）改为**整段精确匹配**，
  长关键词才允许子串匹配。同时把裸的 `"3d"` 关键词从规则里删掉，保留 `"3dprojects"`。
  文件名维度也同样处理：短关键词只与“去掉扩展名的文件名”整体比较。

#### 问题 E：取消时抛 `TaskCanceledException`（2 个失败）

- **现象**：`Cancel_ReleasesTheBackgroundTask` 与 `Cancel_BeforeStart_ReturnsImmediately`
  抛异常，而不是返回 `WasCancelled = true` 的结果。
- **根因**：`ScanAsync` 把 `cancellationToken` 也传给了 `Task.Run`。
  当 token 在委托开始执行前就已取消时，`Task.Run` 会直接让任务进入取消态并抛
  `TaskCanceledException`，调用方拿不到结果对象。
- **修复**：不再把 token 传给 `Task.Run`，只传给内部 `Scan`。
  于“取消”表现为“扫描提前结束并返回带 `WasCancelled = true` 的摘要”，语义更合理。

#### 问题 F：无权限目录没有被计入“跳过目录”

- **现象**：`UnauthorisedDirectory_IsSkippedAndCounted` 期望跳过数 > 0，实际为 0。
- **根因**：枚举时用了 `IgnoreInaccessible = true`，框架**静默吞掉**了权限错误，
  所以 `SkippedDirectories` 从未自增。
- **修复**：改为 `IgnoreInaccessible = false`，让权限错误抛出并被上层捕获计数，
  这样用户才能真正看到“有多少目录因为没权限被跳过”。

#### 问题 G：`maxEntries` 上限被突破

- **现象**：设置上限 50，实际扫描了 60 个文件。
- **根因**：上限检查只写在最外层 `while` 的循环头，当前目录里剩下的文件会继续被处理完。
- **修复**：在**文件循环内部**也检查上限，并在触顶时立即跳出外层循环。

#### 问题 H：CSV 与 JSON 条目数对比断言失败

- **现象**：期望 3，实际 4。
- **根因**：CSV 末尾有“汇总”和“统计”两行汇总信息，其中“汇总”行包含扫描根路径
  `D:\TestData`，而测试用 `l.Contains("D:\\TestData")` 统计数据行，把汇总行也算了进去。
- **修复**：这是**测试假设过宽**，修正测试：统计时排除以“汇总”“统计”开头的行。

### 3.4 自检脚本自身的问题

| # | 问题 | 根因 | 修复 |
|---|---|---|---|
| 17 | `--self-test` 报告“进度回调 0 次” | 用了 `Progress<T>`，它会把回调 post 到捕获的 `SynchronizationContext`；无界面场景下落在线程池，检查时还没执行 | 改用同步的 `IProgress<T>` 实现（直接在当前线程调用） |
| 18 | `--self-test` 报告“CSV 缺少 BOM” | `File.ReadAllText` 会自动**吞掉 BOM**，所以判断 `text[0] == '\uFEFF'` 永远为假 | 改为读取文件原始前 3 个字节判断 `EF BB BF` |
| 19 | 自检输出中文在管道中显示为乱码 | 控制台输出编码不是 UTF-8 | 在自检入口显式设置 `Console.OutputEncoding = UTF8`（输出文件本身一直是 UTF-8，内容正确） |
| 20 | `--ui-check` 编译报 CS0104 | 同样遇到 WPF/WinForms 控件同名问题 | 在 `GlobalUsings.cs` 中补齐控件别名 |

---

## 4. 需求对照检查

| 任务要求 | 实现情况 | 验证方式 |
|---|---|---|
| 选择文件夹 | `FolderBrowserDialog` | `--ui-check` 核对 `BrowseButton` |
| 输入大文件阈值 + 单位下拉 | 阈值框 + KB/MB/GB 下拉 | `--ui-check` 核对；`ThresholdTests` |
| “开始扫描”按钮 | 是 | `--ui-check` |
| 递归扫描目录 | 迭代 DFS | `DiskScannerTests` |
| 找出 ≥ 阈值的大文件 | 是 | `ThresholdTests`（499/500/501） |
| 按大小降序 | 是 | `--self-test` + `DiskScannerTests` |
| 显示 大小/类型/说明/文件名/完整路径 | DataGrid 5 列（另加修改时间） | `--ui-check` 核对列头 |
| 自动说明（扩展名 + 路径 + 文件夹名，离线） | `FileClassifier` | `FileClassifierTests`、`--self-test` |
| 覆盖至少 17 类 | `FileCategory` 恰好 17 个值 | 代码 + `EveryMandatoryCategoryIsReachable` |
| 支持取消 | `CancellationToken` | `Cancel_ActuallyStopsTheScan` 等 |
| 扫描状态 + 当前位置 | `StatusText` + 进度信息 | `--ui-check` |
| 已扫描数 / 大文件数 / 跳过目录数 | 三个独立计数 | `--ui-check` + `DiskScannerTests` |
| 导出 CSV | 是（UTF-8 BOM） | `--self-test` 验证 BOM 与行数 |
| 扫描不卡 UI（后台执行） | `Task.Run` + `DispatcherTimer` 合并刷新 | 设计 + 代码评审 |
| 权限错误不崩溃 | 逐条 try/catch + 计数 | `UnauthorisedDirectory_IsSkippedAndCounted` |
| 空文件夹 | 正常处理 | `EmptyDirectory_ProducesNoResults` |
| 扫描中文件消失 | 捕获并计数 | `FileDeletedDuringScan_DoesNotThrow` |
| 无权限目录 | 跳过并计数 | 同上 |
| 超长路径 | `longPathAware` | `LongPaths_AreHandledWithoutCrashing` |
| 符号链接 / junction / 循环 | 重解析点识别 + 已访问集合 | `SymbolicLinkLoop_DoesNotCauseInfiniteRecursion` |
| 取消真正终止后台任务 | 是 | `Cancel_ReleasesTheBackgroundTask` |
| 不删除真实文件 | 全程只读元数据 | 代码中无任何删除调用 |
| 测试数据只在 `test_data/` | 是 | 目录检查 |
| 不占用几 GB 真实磁盘 | 稀疏文件：10.8 GB 逻辑 / 40 KB 实际 | `du -sh` + `GetCompressedFileSizeW` |
| 代码分层（UI/Scanner/Classifier/Models/Utilities/Tests） | 是 | 目录结构 |
| 自动测试 | 157 个用例 | `dotnet test` |
| 真实 Build / Test / Run | 是 | 本文件第 2 节 |
| 真实 Smoke Test | 是（核心 + 界面构造） | 第 2.3 / 2.4 节 |
| 自己制造测试数据 | `TestDataFactory` | `--self-test` |
| Git 前后检查 | 是 | 第 5 节 |
| 生成 README.md / TEST_REPORT.md | 是 | 本目录 |

### 额外挑战（已完成 10 项，要求至少 2 项）

实时增量结果、扫描速度（文件/秒）、扩展名统计、Top 10 最大目录、
结果搜索、按类型筛选、JSON 导出、深色模式、记住上次扫描设置、耗时统计。

---

## 5. Git 检查

实现前后均执行过 `git status`。

```
$ git status --short
?? agent-benchmark/MiniDiskLab/.gitignore
?? agent-benchmark/MiniDiskLab/MiniDiskLab.sln
?? agent-benchmark/MiniDiskLab/README.md
?? agent-benchmark/MiniDiskLab/TEST_REPORT.md
?? agent-benchmark/MiniDiskLab/build.cmd
?? agent-benchmark/MiniDiskLab/src/
?? agent-benchmark/MiniDiskLab/tests/
```

```
$ git status --short agent-benchmark/MiniDiskLab/AGENT_TEST_TASK.txt
（无输出 —— 文件未被修改）

$ git diff --stat
（无输出 —— 仓库中既有的磁盘管理器代码零改动）
```

**结论**：本次所有改动都严格限制在 `agent-benchmark/MiniDiskLab/` 目录内；
`AGENT_TEST_TASK.txt` 未被修改；仓库既有的其他代码未被触碰。

---

## 6. 已知问题 / 未验证项（如实说明）

1. **未做真实 GUI 鼠标交互测试。**
   本机执行环境不允许启动 GUI 进程（进程创建被安全策略拦截），
   因此**没有**真正打开窗口去点点看。
   已经做的替代验证是：
   - `--ui-check`：真实构造 `MainWindow`，确认 XAML 能加载、所有要求的控件都存在、
     浅色/深色两套主题画刷都能解析 —— 这能排除界面层的构造期错误；
   - `--self-test`：完整跑通扫描 → 分类 → 排序 → 导出 的真实数据流。
   **仍然未被自动验证的部分**：按钮点击事件的实际响应、文件对话框弹出、
   界面在真实扫描时的视觉流畅度、窗口缩放行为。这些需要人工在桌面上确认。

2. **构建产物与被测项目外的工具链。**
   为完成编译，本机额外解压了一份 .NET SDK（在工作区根目录，不在仓库内），
   并用一个启动脚本修复了环境变量。这些都不属于交付内容，也未被提交到仓库。

3. **`test_data/` 已加入 `.gitignore`。**
   测试数据是运行时生成的（且体积“看起来”有 10 GB），不适合提交。
   需要复现时，运行 `--self-test` 或点“生成测试数据”按钮即可重建。

4. **目录大小统计的口径。**
   “Top 10 最大目录”统计的是**直接位于该目录下的文件**大小之和，
   不递归包含子目录，因此对深层目录树会低估。这是有意的实现简化。

5. **分类准确率依赖启发式规则。**
   极端或非典型的目录命名可能落到 `Unknown`。规则集集中在
   `FileClassifier.DefaultExtensionRules()` / `DefaultPathRules()` 中，便于后续增补。
