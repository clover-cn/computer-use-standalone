# 第三方组件

- .NET 8.0.34 自包含运行库：微软，MIT 及附带第三方条款；见 runtime/win-x64-0.3.0-final 的 LICENSE、ThirdPartyNotices 文件。
- Microsoft.Windows.SDK.NET.Ref 10.0.19041.56：微软 Windows SDK 投影，来源 api.nuget.org，原始包元数据保留于 licenses。
- Tesseract .NET 5.2.0：Charles Weld，Apache-2.0；https://github.com/charlesw/tesseract。原包包含 Tesseract 5.2.0 / Leptonica 原生 DLL，附带上游许可与原包元数据供核对。
- tessdata_fast：Tesseract 官方项目，Apache-2.0；语言模型 LICENSE 与固定提交及 SHA-256 保留在 tessdata 目录。
- Microsoft VCLibs Desktop x64：微软可再分发运行库，适用微软许可；官方原始下载位置和 SHA-256 记录在 vclibs-source.json。DLL 仅应用本地使用，未安装系统包。
- Node.js MCP SDK / Zod 等：各包 LICENSE 保留在 node_modules 中，精确依赖版本见 package-lock.json。

原版 OpenAI computer-use 插件的 Proprietary 材料仅保留于本地 upstream，不包含在新增便携发布目录中。
