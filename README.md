# Cipanguanli / 磁盘空间诊断器

Windows 10/11 x64 本地磁盘诊断工具。目标不是做“无脑清理器”，而是回答四个问题：**什么占空间、它属于谁、能不能动、应该清理/隔离/迁移还是走官方机制。**

## v0.4：C盘专清

启动程序后可选择“C盘专清”或“全盘诊断”。C盘模式单独采用更严格的系统盘规则。

### C盘专项体检

会分别检查并解释：

- Windows Temp / 用户 Temp
- Windows Update 下载缓存
- Delivery Optimization 缓存
- Windows 错误报告、Minidump、MEMORY.DMP、CBS 日志
- Windows.old
- WinSxS 组件存储
- Windows Installer / Package Cache
- DriverStore
- WindowsApps
- `hiberfil.sys` / `pagefile.sys` / `swapfile.sys`
- 回收站
- NVIDIA / DirectX Shader Cache
- Adobe Media Cache / Camera Raw Cache
- Chrome / Edge 缓存
- npm / pip / NuGet / Gradle / Cargo / pnpm 等开发缓存
- Hugging Face / Torch / Ollama 等 AI 模型与缓存
- 微信 / QQ 文件
- OneDrive 本地副本

每项显示：占用、预计可释放、安全分、风险、建议动作、路径和中文说明。

### WinSxS 正确计量

真实 C 盘分析会调用：

`DISM /Online /Cleanup-Image /AnalyzeComponentStore /English`

避免把 WinSxS 硬链接造成的资源管理器“表面大小”直接当成真正可回收空间。只有 DISM 建议清理时，才提供 `StartComponentCleanup` 官方动作；**绝不直接删除 WinSxS。**

### Installer / DriverStore / Pagefile 红线

以下项目默认视为保护区：

- `C:\Windows\Installer`
- `DriverStore\FileRepository`
- `pagefile.sys` / `swapfile.sys`
- WindowsApps
- System Volume Information / VSS

程序只给占用解释和官方管理入口，不提供暴力删除。

### 休眠文件顾问

检测 `hiberfil.sys` 后可选择：

- `reduced`：通常保留快速启动、关闭完整休眠
- 完全关闭休眠：释放更多空间，但休眠与快速启动可能受影响

两种操作都需要管理员权限并明确二次确认。

### VSS / 系统还原点

尝试读取卷影副本存储占用；处理入口使用 Windows 系统保护设置，不直接操作 `System Volume Information`。

### AppData / “装D写C”

按 LocalAppData / Roaming 的一级目录聚合体积，并结合 Windows 安装项、Steam、Epic 本地信息推测归属。

如果程序主体安装在 D/E 盘，而 AppData 仍在 C 盘占用大量空间，会明确标记：

`装在其他盘，但写入 C 盘`

用于定位“明明软件装 D 盘，C 盘却越来越小”的情况。

### WSL / Docker 虚拟磁盘

检测常见 `ext4.vhdx` / Docker VHDX：

- 显示宿主文件实际大小
- Docker 提供 `docker system df` 检查入口
- WSL 提供 `wsl --list --verbose` 状态入口
- 明确提示：Linux 内删除数据后 VHDX 不一定立即缩小，应该先清内部数据、`wsl --shutdown`，再考虑迁移或压缩

不会直接删除 VHDX。

### 目标式清理

用户可以输入例如“我要释放 50 GB”。程序会从当前 C盘专项候选中优先选择：

1. 安全分更高
2. 可重建缓存
3. Windows 官方可清理项目
4. 预计释放空间较大的项目

如果低风险项目凑不够目标，软件会明确告诉用户“不足”，而不是为了凑数去碰 Installer、Pagefile、工程文件等高危数据。

另有“紧急救援模式”，只选更高安全分且动作类型更保守的候选，用于 C 盘只剩几 GB 的情况。

### C盘增长历史

C盘专项分析会保存独立快照。第二次分析开始显示：

- 上次大小
- 当前大小
- 增长量
- 哪个缓存/AppData/VHDX 增长最快

只显示显著增长，减少噪声。

### “谁在写C盘”实时追踪

使用 Microsoft TraceEvent + Windows Kernel ETW File I/O：

- 按进程累计写入系统盘的字节数
- 显示进程名、PID、累计写入量、最近写入路径
- 用于定位 NVIDIA、Steam、微信、浏览器、Docker 等谁在持续吃 C 盘

该功能需要管理员权限。停止追踪后 ETW session 会自动释放。

## v0.3 继承功能

### 软件 / 游戏归属识别

- Windows 已安装程序注册表
- Steam `appmanifest_*.acf` 与 Library 路径
- Epic Games 本地 Manifest
- 软件名、识别来源、置信度和官方管理建议

### 智能安全评分与依赖提示

大文件和大目录生成 0–100 处理安全分，结合系统路径、软件归属、缓存语义、3D工程、数据库、虚拟机、云盘等风险。

### 卸载残留、增长历史、三档清理

继续保留 v0.3 的：

- 疑似卸载残留
- 全盘空间增长历史
- 保守 / 推荐 / 激进三档清理建议
- 可恢复隔离区
- 路径用途学习
- 资源管理器右键菜单
- 相似媒体候选
- 压缩收益预测

## v0.2 继承功能

- 自定义大文件阈值
- 多磁盘 / 指定目录扫描
- 大文件按大小排序
- NTFS 逻辑大小 + 实际占用
- 中文用途解释与风险等级
- 分层空间地图
- 180 / 365 天长期未修改文件
- SHA-256 完全重复文件检测
- 腾讯视频 / 爱奇艺 / 优酷 / 微信 / Discord / Telegram / 浏览器缓存
- NVIDIA / DirectX shader cache
- Adobe 缓存
- Unreal / Unity / Maya / ZBrush / Substance / Houdini 等工程识别
- npm / pip / Gradle / NuGet / Hugging Face / Torch 等开发/AI缓存
- AI 模型权重识别
- 安全迁移
- CSV 导出

## 安全原则

1. 系统关键文件拒绝普通删除、迁移和整目录隔离。
2. WinSxS、Installer、DriverStore、WindowsApps 等只走 Windows 官方/受支持机制。
3. 已安装软件目录优先走官方卸载或游戏平台管理。
4. 3D工程、数据库、虚拟机、云盘、聊天数据默认提高风险。
5. “预计可释放”是决策辅助，不等于自动删除许可。
6. 不确定时优先隔离或迁移，而不是永久删除。
7. C盘目标式清理如果低风险空间不足，会选择“不达标”，不会自动升级到高危操作。

## 构建

项目为 `.NET 8 + WPF`：

```powershell
dotnet build .\src\Cipanguanli\Cipanguanli.csproj -c Release
```

独立 x64 EXE：

```powershell
dotnet publish .\src\Cipanguanli\Cipanguanli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Windows 自动验收

GitHub Actions `windows-latest` 依次执行：

1. Release Build
2. xUnit 单元测试
3. 发布 self-contained EXE
4. 运行打包后的 EXE 功能自测
5. 直接启动 Launcher、全盘诊断窗口、C盘专清窗口进行 WPF UI smoke test
6. 全部成功后才上传 Windows artifact

## 第三方代码

- 底层 NTFS 实际占用计算方案参考/改编自 MIT 许可的 ValleySoft DiskAnalyzer。
- C盘实时写入来源追踪使用 MIT 许可的 Microsoft.Diagnostics.Tracing.TraceEvent。

详见 `THIRD_PARTY_NOTICES.md`。
