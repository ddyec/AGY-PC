# AGY for PC (WinUI 3)

基于 **Microsoft WinUI 3 / Windows App SDK** 构建的原生 Windows 桌面端 Antigravity 客户端。

采用现代 Windows 原生控件与主题系统，支持跟随系统深浅色切换、流式对话、两阶段思考摘要、文件管理，以及集成本地 Windows / WSL / 远程服务器的混合构建工作流。

---

## 主要功能

- **原生 WinUI 3 桌面体验**：
  - 统一工作区顶栏、精致侧边会话栏与居中欢迎页引导卡片，自适应宽屏与窄窗口布局。
  - 完整支持 GFM Markdown 渲染、代码块一键复制与自动换行切换、两阶段思考摘要与工具调用折叠。
  - 支持输入框直接 `Ctrl+V` 粘贴剪贴板图片、拖拽上传文件，以及全窗口无裁切图片缩放预览。
- **模型、策略与文件引用**：
  - 动态同步服务端可用模型列表与对应的思考强度档位。
  - 自由切换 **Restriction（逐项审批）** / **Turbo（自动执行）** 策略，以及“先做计划 / 直接实施”模式。
  - 在输入框输入 `@` 或通过文件面板，可直接引用服务器上的文件或整个文件夹，无需手写绝对路径。
- **混合构建工作流（Hybrid Build）**：
  - 支持将远程服务器上的项目快照一键同步到 Windows 本机独立目录（自动过滤密钥与缓存文件）。
  - 根据项目类型灵活选择执行位置：
    - **Windows 原生程序 / WinUI**：在 Windows 本机通过 PowerShell 编译。
    - **Linux x86_64 项目**：调用本机 WSL 环境编译。
    - **Web / 服务端项目**：直接在远程服务器上执行。
  - 支持将本机或 WSL 的构建日志与产物一键回传至当前对话。
- **网络与安全**：
  - 内置 **DoH（DNS over HTTPS）** 设置（支持 Cloudflare / Google 或系统 DNS）。
  - 服务器地址与连接密钥使用 Windows **DPAPI** 在本机加密存储。

---

## 安装与使用

### 方式一：使用 Windows 构建器一键安装（推荐）
1. 从 [Releases](https://github.com/ddyec/AGY-PC/releases) 下载最新的 `AGY-PC-Windows-Builder.zip` 并解压到普通可写目录。
2. 运行 `AGY-PC-Windows-Builder.exe`。
3. 工具会自动检查并按需配置 .NET SDK，在本地编译生成原生 WinUI 运行包并自动启动客户端（构建产物保存在 `%LOCALAPPDATA%\AGY-PC\WindowsBuilds\` 下）。
4. 启动后填入与手机端相同的服务器地址和连接密钥即可开始使用。

### 方式二：已有环境快速更新
如果此前已通过 Builder 构建过客户端，可下载 `AGY-PC-UI-Update.zip`，完整解压后双击 `Update-UI.cmd`，脚本会复用本地已有的 SDK 与 NuGet 缓存快速编译更新，并保留原有连接配置。

---

## 从源码编译

环境要求：Windows 10/11 x64，安装 **.NET 10 SDK** 与 Windows App SDK 构建依赖。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-winui.ps1
```

如果旧构建目录曾遇到 `PRI175 / PRI277` 资源索引冲突，可运行仓库附带的修复脚本清理重复缓存：

```powershell
powershell -ExecutionPolicy Bypass -File .\repair-pri175.ps1
```

---

## 配套网关与文档

- `Server/` 目录包含与 iPhone / Mac 端兼容的配套网关服务，新增了项目快照同步（`/v1/sessions/{id}/snapshot`）与混合构建支持。
- 更多测试与验证说明见 [VALIDATION.md](VALIDATION.md)。
