# AGY - PC 2.0 · WinUI 3 与混合构建

这一版替换 1.0 的 WPF 界面，使用真正的 Microsoft WinUI 3 / Windows App SDK 2.6.4。原生导航、输入框、密码框、按钮、InfoBar、文件选择器和对话框共享系统主题资源，支持跟随 Windows、浅色和深色。旧版明暗混用、文字不可见的界面不再作为当前实现。

**当前为 `2.0.0-beta.4`。Release 提供源码和 Windows 本机构建入口，不把 Linux 上产生的中间程序集标成 WinUI 运行包。** 本机构建入口本身是 Windows 控制台工具，不是新版 WinUI 客户端；它负责在 Windows 上生成真正的 WinUI 运行包。



## 这次更新：标题栏主题与 DoH

已安装的设备先退出 AGY-PC，下载 `AGY-PC-Theme-DoH-Update.zip`，**完整解压后双击 `Update-Theme-DoH.cmd`**。它会备份并更新最近一次 WindowsBuilds 源码目录，复用 SDK/NuGet 缓存，运行 32 项核心测试并重新生成客户端；不会删除保存的连接信息。

- 标题栏、窗口按钮及非活动窗口状态与正文的浅色/深色一致；切换主题、跟随 Windows 时一起更新，高对比度交给系统。
- 登录页和“配置与额度”都有“网络设置 · DoH”。默认启用 Cloudflare，可切换 Google、关闭 DoH，或关闭默认开启的系统 DNS 回退。设置在连接前就能保存，重启后保留；已连接时保存会重新连接，不自动重新发送任务。
- DoH 查询 A/AAAA，按 TTL 缓存，使用官方解析器 IP 引导连接；不依赖本机先解析解析器域名。连接仍使用原始服务器域名、SNI 和正常证书验证。仅 DNS 失败才允许回退，不重试失败的任务请求。
- 使用系统代理时保留原有代理流程，由代理解析目标域名。IP、localhost、局域网名称保留系统解析。DoH 服务只收到域名查询，连接密钥、消息、文件不发送给解析器。
- UI 检查会检查两种主题的标题栏颜色属性；登录页 PNG 是 XAML 正文截图，不包含系统标题栏。实际标题栏显示仍需在 Windows 窗口观察。

beta.3 标准 App.xaml 初始化、启动诊断和 beta.2 PRI 去重继续保留。若构建/启动失败，本机 `startup-diagnostics` 下保留 `diagnostic-summary.txt` 和 `startup.log`；诊断不会自动上传。

## 已遇到 PRI175 / PRI277 的现有构建

下载 `AGY-PC-PRI175-Repair.zip`，解压后双击 `Repair-PRI175.cmd`。脚本会修复当前用户最新一次 AGY-PC Windows 构建目录，备份项目文件，清除重复的 Assets 资源声明，仅清理该 WinUI 项目的 bin/obj，再复用现有 SDK 和 NuGet 缓存继续发布。SDK 不必重新下载。

也可以在修复包目录的 PowerShell 中指定原失败目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\repair-pri175.ps1 -BuildDirectory "$env:LOCALAPPDATA\AGY-PC\WindowsBuilds\20261031-181823-989ffa"
```

2.0.0-beta.3 已从源码修复：保留官方图标、Logo 和 EXE 嵌入图标；先清除 SDK 默认通配项再显式收录，每份图片只进入 PRI 一次。没有禁用资源索引。

## Windows 上使用新版

从 Release 下载 `AGY-PC-2.0.0-beta.4-Windows-Builder.zip`，解压到普通可写目录，运行 `AGY-PC-Windows-Builder.exe`。

它会：

1. 校验随包源码 SHA-256。
2. 检查 .NET 10 SDK；缺少时从 Microsoft 固定版本地址下载并验证 SHA-512，只安装到当前用户的 AGY-PC 工具缓存。
3. 在 Windows 上运行核心测试，调用真正的 Windows SDK 工具完整构建 WinUI。
4. 启动 WinUI 的 `--ui-check`，生成真实的浅色与深色登录页 PNG 和检查报告。缺少这些结果会判为未完成，而非伪报成功。
5. 生成运行包、打开截图目录，并启动刚编译的新客户端。截图仍需人工检查，文件选择、DPI、输入法等交互并不因截图生成而自动验收。

源码、日志、运行包位于 `%LOCALAPPDATA%\AGY-PC\WindowsBuilds\<本次构建>/`。首次下载 SDK / NuGet 需要网络。构建入口仅支持 Windows x64；不会在 WSL 中运行 Windows SDK 的 `mt.exe` 或 `MakePri.exe`。

新版启动后填写与 iPhone 相同的服务器地址、连接密钥。现有 Windows 用户的 DPAPI 加密连接记录兼容保留，源码和构建包不包含用户密钥。

## 混合工作流

“混合构建”面板提供三个明确执行位置：

| 项目 | 执行位置 | 如何执行 |
| --- | --- | --- |
| Windows 程序 / WinUI | Windows 本机 | 同步到新本地副本或选择本地目录，确认命令后用 PowerShell 构建 |
| 网页、服务端项目 | 当前服务器 | 把构建请求发送给当前 AGY 会话，继续使用会话审批、日志和产物逻辑 |
| Linux x64 项目 | Windows 设备的 WSL | 指定发行版或用默认发行版，自动转换本地目录路径；先验证 `uname -m` 为 `x86_64` |

当前服务器为 ARM64，不把服务器原生构建当成 x64 Linux 构建。WSL 不可用或架构不符合时会拒绝，不静默切换目标。

执行前展示实际目标、目录与完整命令，只有点击“执行这一次”才启动本机/WSL 操作。当前为用户确认驱动的混合构建面板，**尚未实现模型自动调度到离线 Windows 节点、无人值守任务队列、WSL 发行版自动安装或双向增量文件同步**。

“同步服务器项目到新副本”通过认证快照接口获取源文件。每次生成独立目录，不覆盖 Windows 本地改动；排除常见凭据目录、`.env*`、密钥文件、构建缓存、网关数据库和符号链接。提取拒绝越界、符号链接、大小写冲突与 Windows 保留文件名。仍需遵守服务器允许的工作区范围。快照上限 25 MiB / 5000 项。

本机与 WSL 构建日志保存在本机，可明确点击“回传构建日志”“回传产物文件”，上传到当前会话的“已上传”区后再引用。单文件回传目前为 25 MiB，大型自包含 Windows 程序的安装包请先保留在 Windows 本机；本版未提供大文件分片回传。WSL 停止操作会尝试结束本次构建的独立进程组及对应 Windows 主进程。

## 会话与文件

保留现有网关逻辑：聊天与历史、真实模型清单、独立思考强度、Restriction/Turbo、单次审批、账号额度、配置记忆、草稿与原编号重试。输入 `@` 或打开文件窗口可引用文件及整个文件夹；手机和电脑不需要手写服务器绝对路径。

Enter 换行，Ctrl+Enter 发送。文件可以拖入或从原生选择器上传。原生控件用于文本、代码、步骤展开及文件链接；不是网页渲染器，也不声称完整复刻所有 Markdown/CSS 排版。

## 自己构建

在 Windows 安装 .NET SDK 10.0.403 或更新兼容版本后：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-winui.ps1
```

Windows ARM64 可传 `-Runtime win-arm64` 进行本机构建；本次未发布 ARM64 构建入口，也未在 ARM64 Windows 验证。

## 网关更新

`Server/` 是与 iPhone 向后兼容的配套后端，新增 `GET /v1/sessions/{id}/snapshot` 与 `projectSnapshots` 能力标识。Windows 不运行这套 Python 服务；服务器端需部署新版网关才支持项目快照。不会更改 Google 登录、手机密钥或 Cloudflare 隧道。

## 验证与边界

见 [VALIDATION.md](VALIDATION.md)。这台 Linux 上已执行核心与网关测试，此前纯代码版本完成过托管类型编译；新版 App.xaml 生成与完整编译必须在 Windows 设备完成。真实界面也必须在 Windows 设备验证；仓库与 Release 均不把尚未运行的截图检查写成“已通过”。所有流程不使用 GitHub Actions。
