# 2.0.0-beta.6 · 视觉精修与工作区排版重塑

针对用户提供的 `2.0.0-beta.5` Windows 实机截图（`415.jpg`）暴露出的视觉与排版缺陷进行精修：

- **消除双层顶栏与外层套框**：`DesktopShell` 合并会话标题、任务策略胶囊与项目切换按钮为单一顶栏，移除包裹整个主页面的多余外框。
- **修正欢迎页歪斜与单调长条按钮**：欢迎页顶部采用居中圆角品牌徽章与居中对齐标题/副标题，下方改为三栏并排引导卡片（窄窗口自动切换单列）。
- **统一输入岛工具栏与消除内框**：清除输入框在悬停/聚焦时的内部边框与底线；左侧工具栏统一为 32px 等高胶囊按钮（`附件`、`@ 引用`、`模型 · 强度`），快捷键提示居中。
- **重塑侧栏与代码块控件**：替换刺眼的全宽亮青色 `新建对话` 按钮为带 `Ctrl+N` 提示的圆角卡片按钮；搜索框改用全包围圆角边框；底部增加在线状态圆点卡片；代码块语言标签、换行开关与复制按钮合并至顶栏。
- **验证情况**：Linux 环境下完成 32 项核心回归测试（`AGY.PC.Tests`）通过、WinUI 3 托管 C# 编译（`ResolveReferences,CoreCompile`）零警告通过，并交叉编译 `AGY-PC-Windows-Builder.exe`。实际 Windows 界面渲染效果由 Windows 端运行 `AGY-PC-UI-Update.zip` 或 `AGY-PC-2.0.0-beta.6-Windows-Builder.zip` 生成并查看。

---

# 2.0.0-beta.5 · 主界面布局重写

本次针对用户 Windows 实机截图中的侧栏裁切、巨大空白卡片、输入区松散及功能入口混杂，重写实际 WinUI 页面。新增 DesktopShell，完整侧栏使用 Inline/Overlay SplitView 切换，不再使用 NavigationView 的自动紧凑栏容纳会话列表。聊天、文件、设置与构建页面布局均有实际改动。

- WinUI C# 类型检查通过。临时检查工程的生成契约占位不打包、不运行，不作为 Windows XAML/PRI 编译或界面验收证据。
- 核心/协议本次没有修改，沿用 beta.4 已通过的 32 项核心检查；未重复运行，不新增只映射布局实现的单元测试。
- Windows 本机预览扩大到浅色/深色工作区、聊天、文件、构建、配置及窄窗口，不再只拍登录页。示例来自内存 HttpMessageHandler，显式拒绝真实地址和非 GET 请求，使用隔离状态与示例标签。
- 主界面截图在用户 Windows 更新时实际生成。本 Linux 服务器未运行 Windows 原生窗口，**未完成本版视觉验收**。XAML 正文截图不包含系统标题栏。
- 更新脚本仅备份和修补 AGY-PC 既有构建源码，复用缓存；不会清空连接设置。无 Actions、无服务器或账号变更。

---

# 2.0.0-beta.4 · 标题栏与 DoH

- 用户提供的 beta.3 Windows 实机截图显示 WinUI 登录界面已经启动。本版修复其标题栏保持浅色的问题：正文 ActualTheme 变化时同步 PreferredTheme、标题/按钮颜色及非活动状态；高对比度使用系统默认。
- DoH 应用于整个 GatewayClient：状态、模型、额度、会话、流式消息、上传和下载；HTTPS URI/SNI/证书验证不变。Cloudflare/Google 默认 IP 引导不需要系统 DNS，HTTP 代理连接由代理解析，局域网名称不使用公共 DoH。
- **32 项核心检查通过**。新增覆盖：登录前偏好持久化、旧状态迁移、A/AAAA、IDN、缓存、TTL=0、NXDOMAIN/畸形/超大响应、取消、代理与绕过规则、真实回环 HTTP 连接的 Host/凭据隔离、HTTPS 原始 SNI 与不受信任证书拒绝、真实代理 CONNECT 不进行目标 DoH 查询。
- WinUI C# 类型编译通过，使用临时生成契约占位仅作类型检查，未把占位打包；不代表 Windows XAML/PRI 完整构建通过。PowerShell 脚本通过微软解析器检查；Windows 构建入口交叉编译完成。
- Windows UI 检查新增浅色/深色标题栏颜色属性断言。RenderTargetBitmap 截图只包括正文，不包括原生标题栏；报告明确标注此限制。
- 本版 Windows 原生构建、实际标题栏像素和公网 DoH 在用户网络下的效果仍需用户 Windows 运行更新包验证。回环测试使用隔离 DNS 响应，未读取登录密钥、未发送模型任务；不声称已在 Windows 上验收。本次没有运行 Actions，也未修改后端和账号。

参考：[微软标题栏 API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowtitlebar)、[Cloudflare DoH](https://developers.cloudflare.com/1.1.1.1/encryption/dns-over-https/make-api-requests/)、[Google DNS JSON API](https://developers.google.com/speed/public-dns/docs/doh/json)。

---

# 2.0.0-beta.3 · 启动入口与诊断

- 用户 Windows 日志确认 beta.2 已完成 publish，随后启动退出 -1073741189（0xC000027B）。这说明是启动故障；该状态码不能唯一确定 XAML、资源、环境中的哪一处异常。
- 改用标准 App.xaml 资源加载和 InitializeComponent，让 Windows XAML 编译器生成应用元数据链；不再靠纯代码添加 XamlControlsResources。
- 发布时确认应用 PRI 和 App.xbf 生成并复制；启动前检查框架 PRI、WinUI DLL、应用依赖及品牌资源。保留 beta.2 的图片单次收录修复。
- 启动阶段标记、COM/XAML 原始异常、AppDomain/任务异常写入本机日志。未知 XAML 错误不被设为 Handled；不把异常吞掉假装成功。
- Windows 构建入口失败时自动采集发布清单与应用事件，并打印可读十六进制状态码；不收集内存转储、注册表、凭据或环境变量全集，不自动上传诊断。
- 核心可执行回归 **24 项通过**，包含缺失启动资源和退出码格式检查。新增 PowerShell 脚本通过微软解析器语法检查。MSBuild 评估确认 App.xaml 恰好 1 个 ApplicationDefinition，两份品牌资源仍各 1 项。
- 此版本开始使用 Windows XAML 编译器。Linux 不能执行该原生步骤，因此本次不再把之前纯代码 WinUI 的 Compile 成功当作 beta.3 完整编译证据。**beta.3 的 XAML 编译、完整发布和原生启动尚待 Windows 设备重跑。**构建入口自身在 Linux 上完成 Windows x64 交叉编译。
- 网关后端与 Google 账号未改动；23 项服务器测试为此前记录，本次未重复运行。没有运行 GitHub Actions。

参考：Microsoft UI Reactor 对无 XAML 应用的元数据链说明：
https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor/Hosting/ReactorApp.cs

---

# 2.0.0-beta.2 · PRI175 修复

- 根据真实 Windows 日志，完整构建已通过 C# 编译，失败点为 WinUI PRI 资源索引的重复 `Files/Assets/AGY.ico`。
- 在 SDK 10.0.403 / Windows App SDK 2.6.4 的实际 MSBuild 资源图复现：ICO 为 2 项，PNG 为 3 项。根因是 WinUI / .NET 的默认资源通配符与显式 Content Include 叠加。
- 修复先移除 Assets 的默认 Content/None 项，再统一显式收录，保留 EXE 图标嵌入及输出/发布复制。x64、ARM64 的发布配置均验证为每个资源恰好 1 项。
- 在 C# 编译 / PRI 前增加重复项检查；通过注入重复图标验证构建会主动失败。未关闭 PRI 生成，也未移除官方图标或 Logo。
- 托管 WinUI 编译及资源图检查在 Linux 上执行；真实 Windows MakePRI 和启动仍需用户设备重跑，不能宣称此次已在 Windows 全量通过。
- 附带修复现有失败目录的 PowerShell 脚本：备份项目、去除重复、仅清理该 WinUI 项目的 bin/obj，复用 SDK 与 NuGet 缓存重新发布，并运行原有截图检查。该脚本未在这台 Linux 上执行。

---

# 2.0.0-beta.1 验证

## 本次实际完成

- Microsoft Windows App SDK 2.6.4 的真实 WinUI 3 工程，移除当前 WPF 前端，不是 WPF 换名称。
- Linux ARM64 / .NET SDK 10.0.403 上：22 项核心测试通过；23 项 Python 网关回归通过。
- 核心新增覆盖：项目 ZIP 提取、中文及二进制保留、不覆盖已有目录、拒绝穿越/符号链接/大小写冲突/Windows 设备文件名；阻止在 ARM Linux 上误跑 Windows 或服务器目标。
- `dotnet msbuild ... -t:Compile -p:WindowsAppSDKSelfContained=false` 完成 WinUI 托管代码类型编译。此操作只检查 C# 编译，不是最终 Windows 打包。
- 完整 WinUI 构建实际在本机失败于 Windows SDK 的 `mt.exe` / `MakePri.exe` 平台工具；没有把禁用这些步骤后的文件作为可分发 WinUI exe。
- Windows x64 本机构建入口完成交叉编译。入口自身在 Windows 上的 SDK 安装、完整 WinUI 编译、UI 截图尚未在本服务器执行。

## 尚未完成，必须在 Windows 节点执行

- WinUI 完整打包、激活、DPAPI、原生选择器、DPI/中文输入法与系统主题运行检查。
- 真正的 `--ui-check` 会生成浅色/深色登录页 PNG 与报告；只有用户 Windows 构建节点实际执行后才会存在。截图生成不是全部交互验收。
- PowerShell 实际构建、WSL 发行版调用、x86_64 架构验证与 WSL 进程组取消路径的真实执行。
- 从服务器下载项目到 Windows 的端到端文件同步与产物回传。

混合工作流是明确确认后执行的本机 / WSL 构建及服务器 AGY 会话委派，不是无人值守跨设备调度器。未增加自动执行本机命令的远程接口。


## 服务器实际验证

配套项目同步接口已在空闲网关部署。公网 status 和 `/snapshot` 返回 200；临时空会话导出本项目 ZIP，验证目录排除与完整性后已删除。没有向真实账号发送模型任务，没有改变 Google 登录、连接密钥或隧道。
