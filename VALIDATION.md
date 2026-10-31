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
