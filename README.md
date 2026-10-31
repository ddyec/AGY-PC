# AGY - PC

独立的 Windows 原生 WPF 客户端，沿用 AGY iPhone 1.3.1 的网关与任务逻辑。不是浏览器包装，不要求安装浏览器扩展。版本 **1.0.0**，首次交付 Windows x64 自包含运行包，同时提供完整源码。

## 直接使用

1. 解压 `AGY-PC-1.0.0-win-x64.zip`，运行 `AGY-PC.exe`。运行包自带 .NET，无需另装运行时。
2. 地址填 `https://agy.yectech.uk`，连接密钥填 iPhone 使用的同一个密钥。源码和运行包均不含用户密钥，不需要在电脑端输入 Google 密码。
3. 右侧先选模型，再选它实际支持的思考强度；审批策略和执行方式独立选择，点击“保存配置”。配置、项目文件夹、输出展示和草稿按服务器与账号保存。
4. 中间输入消息，点击“发送”或按 **Ctrl+Enter**。Enter 保留为换行，避免中文输入法选词时误发。可以拖入文件，或点“上传”。
5. 输入 `@` 或点“@ 文件 / 文件夹”打开原生文件窗口。双击进入目录，选中文件夹后点“@ 引用选中项”即可引用整个目录，也可引用整个当前文件夹。手机/电脑都不需要手写服务器路径。
6. 左下“文件与产物”区分工作区、已上传文件和 AGY 产物。可搜索当前目录、按修改时间排序、显示隐藏文件、上传、预览和另存下载。上传本身不发送模型任务。
7. Restriction 会显示工具的实际参数，单次允许或拒绝；Turbo 无逐项确认，仍遵守 AGY 明确禁止的规则。聊天可选原版、步骤摘要、结论优先；工具参数和输出均可展开查看，代码可以复制和切换换行。
8. 发送结果不明确时，使用“核对会话”或“按原编号重试”。断线或重启不会自动重发任务。

已有会话从服务器恢复自己的模型与策略。查看历史不会把旧会话的 Turbo 等配置写成新会话默认值。新账号不会继承另一个账号的 Turbo、草稿或待发送内容；检测到服务器换账号后会要求重新连接。

## 连接与文件边界

客户端使用与 iPhone 相同的 `/v1/` 认证网关、SSE 快照和原生审批桥接，需要已部署 AGY iPhone 1.3 的配套后端。不会在 Windows 端启动第二个 AGY 账号或替换现有服务器服务。服务器工作区继续由网关管理，界面以相对路径展示文件。

文件与目录引用传递完整路径及类型，AGY 按需读取，不递归粘贴内容。单文件上传/下载上限 25 MiB，每批上传/每条消息引用最多 8 项。现有网关的每目录 5000 项限制保留；暂未提供全局递归搜索、目录 ZIP 下载或文件移动/删除。

原生预览支持 Windows 能解码的位图、2 MiB 内的常见文本/源码；PDF、Office 等文件请另存后使用自己的应用打开。HTML/SVG 只作为源码显示，不执行脚本。消息中的 HTTPS 链接由默认浏览器打开，网关密钥不会附加到外部站点。Markdown 提供原文、代码块及文件链接展示，不是完整的浏览器 Markdown/CSS 排版引擎。

## Windows 本机记忆

连接密钥、配置、草稿、待确认请求保存在 `%LOCALAPPDATA%\AGY-PC\state.bin`，用当前 Windows 用户的 DPAPI 加密。运行包可以移动，登录信息跟随本机 Windows 用户；不会把凭据写到 exe 所在目录或明文配置里。读取加密状态失败时不会静默清空待确认请求。

暂存预览位于当前用户临时目录 `AGY-PC`；保存到自己选择的位置后可清理这些暂存副本。下载使用临时文件，完整接收后才替换目标文件。

## 自己编译

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在本目录用 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

ARM64 源码构建入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Runtime win-arm64
```

脚本只在本机执行测试与发布，不使用 GitHub Actions。首次恢复需要从微软 NuGet 下载 Windows 桌面包。源码没有 CI 工作流。

## 验证范围

详见 [VALIDATION.md](VALIDATION.md)。本次在 Linux ARM64 上使用微软 .NET 10 SDK，实际完成 Windows x64 交叉编译与发布，已生成 PE x64 exe。跨平台核心测试和真实网关只读检查已执行。**没有真实 Windows 桌面，尚未验证窗口布局、DPI、输入法、文件选择、DPAPI 和 exe 启动。**目标为 Windows x64（建议 Windows 11）；本交付不是 Windows 真机验收通过的声明，也未进行代码签名。

官方图标直接沿用 Google 原图，仅转换成 Windows ICO。品牌与运行库说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。这是独立客户端，并非 Google 官方 Windows 产品。
