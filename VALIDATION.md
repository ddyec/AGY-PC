# AGY - PC 1.0.0 验证记录

- 构建主机：Linux ARM64，微软官方 .NET SDK 10.0.403，Windows Desktop / .NET runtime 10.0.14。
- 原生客户端：WPF，无 WebView、Electron 或浏览器包装。C# 核心与 Windows 界面实际编译通过。
- Windows x64 自包含发布：`dotnet publish src/AGY.PC.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`。PE 头为 AMD64 `0x8664`。
- 19 项跨平台可执行测试通过：HTTPS/凭据校验、Windows 文件名、整个目录相对标签、模型/思考强度回退、持久化失败不覆盖、账号隔离、UTF-8/SSE 分帧、不完整 SSE、HTTP 502/重定向错误、下载字节一致、上传中文文件名与字节一致、超限上传不出网、审批归属与单次决策、配置与输出模式重启恢复、旧历史不覆盖默认值、首次新对话不继承旧 Turbo、目录引用只传路径、丢失回包后核对不重发、明确重试保持请求 ID、磁盘写失败不发送、服务器换账号阻止操作。部分检查组合在一个测试中。
- 使用本客户端相同的 .NET 网络代码，对现有公网网关只读检查成功：status、13 个模型变体 / 5 个模型组、2 个额度组、文件浏览。未输出密钥或文件内容，未对真实账号发送模型任务。
- 使用假网关验证会话发送及审批逻辑；未声称已执行真实账号的任务/审批端到端测试。
- 未在 Windows 上运行：exe 启动、窗口布局与 DPI、DPAPI、拖放、剪贴板、图片解码、原生打开/保存对话框及中文输入法仍需 Windows 实测。ARM64 Windows 提供构建入口，此次只发布 x64。
- 未推送代码、未创建 GitHub 仓库或启动 Actions；未重启、修改或切换线上 AGY 服务及账号。Windows 客户端复用现有入口。
