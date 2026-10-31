# 第三方与品牌说明

Antigravity 名称及全彩图标属于 Google。本项目为独立客户端，不代表 Google。

`Assets/Mark.png` 沿用此前从 [Google Antigravity Press Assets](https://antigravity.google/press) 下载的官方原图，`Assets/AGY.ico` 仅为同一图案的 Windows 尺寸与容器转换：

- https://antigravity.google/assets/image/brand/antigravity-icon__full-color.png

客户端使用 Microsoft .NET / Windows Desktop Runtime，自包含运行包附有对应的微软 LICENSE 与第三方声明。源码没有其他 NuGet 应用依赖；微软 Windows targeting/runtime 由 SDK 恢复。

## WinUI 3 / Windows App SDK

2.0 使用 Microsoft.WindowsAppSDK 2.6.4 及其 WinUI 组件。Windows 完整发布时从官方 NuGet 获取运行库；相关许可和第三方 NOTICE 一并提供。此版本不再以 WPF 作为当前客户端。
