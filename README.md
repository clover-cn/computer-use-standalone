# Windows Computer Use Standalone

通过 MCP 让 AI 客户端读取和操作 Windows 应用，支持窗口截图、控件树、本地 OCR、键鼠输入及连续轨迹绘画。当前版本 **0.4.0** 使用 **STDIO**，由客户端自动启动服务。

这是独立后端，不依赖 Codex 专用运行库。支持 MCP 的客户端均可接入；截图能否进入模型，取决于客户端是否转发 MCP 图片及模型是否支持视觉。

## 功能

| 能力 | 实现与用途 |
|---|---|
| 窗口发现与应用启动 | 列出可见窗口，启动画图、记事本或计算器，尝试激活目标窗口 |
| 窗口截图 | WGC → PrintWindow → 符合条件的可见客户区截图，返回实际后端和回退原因 |
| 控件读取与操作 | Windows UI Automation，支持无需截图的控件操作 |
| 本地中英文 OCR | Tesseract 返回文字、置信度及客户区坐标，不主动上传图像 |
| 局部放大 | 客户区裁剪、1–4 倍放大，工具自动换算图片坐标 |
| 键鼠及绘画 | 点击、文本、组合键、滚动、拖拽、多点连续笔画 |
| 控制提示与取消 | 淡色屏幕边缘、居中提示、Agent 指针、点击反馈、物理 Esc 停止输入 |

截图和控件树互补：标准控件优先用 UIA；内容缺失时结合 OCR 与截图；画布通常需要截图坐标。应用未暴露的控件不会被自动补齐，OCR 文字也不是控件引用。

## 从源码开始：完整安装与运行步骤

适用于第一次从 Git 仓库下载项目的用户。以下命令在 **Windows x64 的 PowerShell** 中执行；除第 1、2 步外，工作目录始终保持为项目根目录。

每一步成功后再进行下一步。构建和下载阶段需要网络；日常使用由 AI 客户端自动启动服务。源码不含 node_modules、原生运行时或 OCR 模型，不能在克隆后直接启动。

### 第 1 步：准备运行环境

安装 Git for Windows 和 Node.js 20 或更高版本，准备一个支持 MCP STDIO 的 AI 客户端。安装完成后重新打开 PowerShell，执行：

```powershell
git --version
node --version
npm --version
```

三个命令都应显示版本，node 主版本需至少为 20。找不到命令时先处理安装或 PATH，再继续。若 PowerShell 提示 npm.ps1 被执行策略阻止，下文的 npm 命令可改用 npm.cmd，无需修改系统执行策略。

Windows 需为 Windows 10 2004 / Windows 11 x64，且桌面已登录、解锁。通常无需管理员启动服务；不要用 AI 的受限 Shell 代替用户普通桌面终端完成首次环境检查。不支持 Linux/macOS、锁屏或安全桌面控制。

### 第 2 步：克隆仓库并进入项目目录

把下面引号内的占位文字替换为真实仓库地址，再执行：

```powershell
git clone "替换为真实仓库地址" computer-use-standalone
Set-Location -LiteralPath ".\computer-use-standalone"
Get-Location
Test-Path .\package.json
```

最后一个命令应返回 True。已经有源码的用户可跳过 git clone，直接进入现有项目目录。本文示例客户端路径使用 `D:\tools\computer-use-standalone`，实际位置不同需自行替换。

### 第 3 步：安装 Node 依赖

```powershell
npm ci --cache .npm-cache --ignore-scripts
```

成功后会生成 node_modules。此步骤安装 package-lock.json 锁定的依赖；遇到网络或安装错误时先解决，不继续启动服务。

### 第 4 步：下载项目使用的 .NET SDK

```powershell
node scripts/setup-sdk.mjs
.\.build\sdk\dotnet.exe --version
```

预期 SDK 版本为 8.0.428。脚本下载 Windows x64 SDK，校验固定 SHA-512，并解压到 .build/sdk。无需另行全局安装 .NET SDK。

### 第 5 步：构建 Windows 原生后端

```powershell
npm run build:helper
Test-Path .\runtime\win-x64-0.3.0-final\DesktopHelper.exe
Test-Path .\runtime\win-x64-0.3.0-final\DesktopHelper.dll
```

构建需从 NuGet 还原依赖，成功后两个 Test-Path 均为 True。输出是自带 .NET 运行时的辅助程序。出现文件占用时，先在客户端正常断开使用该目录的 MCP，再构建，不要强行覆盖运行中的文件。

### 第 6 步：下载中英文 OCR 模型

```powershell
npm run setup:ocr
Test-Path .\runtime\win-x64-0.3.0-final\tessdata\eng.traineddata
Test-Path .\runtime\win-x64-0.3.0-final\tessdata\chi_sim.traineddata
```

两个文件检查应均为 True。脚本使用固定 tessdata_fast 提交，并生成包含来源和 SHA-256 的 manifest.json。

### 第 7 步：准备 OCR 所需的应用本地 VC 运行库

```powershell
node scripts/setup-vclibs.mjs
Test-Path .\runtime\win-x64-0.3.0-final\vclibs-source.json
```

预期文件检查为 True。脚本提取微软 VCLibs Desktop x64 DLL 到原生运行时目录，并记录来源和哈希，不向系统安装包。以后重新构建原生后端后，也应重新执行此步骤。

### 第 8 步：准备第三方许可文件

```powershell
node scripts/setup-licenses.mjs
```

此步骤依赖第 5 步生成的 NuGet 缓存，还会获取上游许可文本。首次构建或准备发布包时执行，并在分发中保留这些许可信息。

### 第 9 步：检查本机后端

```powershell
node cli.mjs doctor
```

输出 JSON 中应重点检查：

```text
ready: true
input_status.normal_desktop_input: true
ocr.available: true
control_cancelled: false
```

如果 normal_desktop_input 为 false，先处理下方“能看不能点”中的完整性问题，再进行输入验收。不要只靠增加 --allow-input 或改成管理员运行掩盖原因。

CLI doctor 仅检查原生后端，不代表后续 AI 客户端启动的另一个实例也具有相同权限。客户端接入后还需再检查一次 MCP doctor。

### 第 10 步：在 AI 客户端配置 STDIO MCP

先获取本机实际入口和 Node 路径：

```powershell
(Resolve-Path .\server.mjs).Path
(Get-Command node).Source
```

在客户端的 MCP 管理中新增 STDIO / 命令类型的服务。支持 mcpServers JSON 配置的客户端可参考：

```json
{
  "mcpServers": {
    "computer-use-standalone": {
      "type": "stdio",
      "command": "node",
      "args": [
        "D:\\tools\\computer-use-standalone\\server.mjs",
        "--allow-input",
        "--all-windows"
      ]
    }
  }
}
```

将 args 第一项替换为刚才打印的 server.mjs 绝对路径。客户端找不到 node 时，将 command 改为打印出的 node.exe 完整路径。JSON 中 Windows 反斜杠需写为双反斜杠。

使用表单配置时：名称填 computer-use-standalone，运行方式选命令 / STDIO，命令填 node，参数分别填脚本绝对路径、--allow-input、--all-windows；环境变量可留空。若参数字段要求 JSON 数组，按上例的 args 数组填写。

保存配置后启用或重新连接 MCP。**由客户端启动 server.mjs，无需手动运行 npm start，也无需另开启动器、填写 URL 或请求头。** 客户端已管理该服务时不要额外重复启动。

### 第 11 步：加入技能说明并检查当前连接

让客户端的技能管理导入本目录 [SKILL.md](SKILL.md)，或让 Agent 在操作前读取其中说明；具体入口取决于客户端。MCP 配置负责提供工具，SKILL 负责指导工具选择、观察验证和收尾。

让 Agent 执行：

> 调用 computer-use-standalone 的 doctor，报告完整性、输入授权范围及取消状态，暂时不要操作鼠标键盘。

所有普通窗口模式下，预期 MCP doctor 报告：

```text
ready: true
host_version: 0.4.0
input_status.normal_desktop_input: true
permissions.input_enabled: true
permissions.scope: all-normal-windows
control_cancelled: false
```

客户端应能发现下文列出的 14 个工具。Node 主机为 0.4.0，原生运行时为 0.3.0，两个版本字段不同属于预期情况。修改启动参数后需重启对应 MCP；仅修改配置不会改变旧进程。

### 第 12 步：完成一次实际使用验证

先用只读任务验证连接：

> 使用 computer-use-standalone 列出当前可见窗口，只报告窗口编号和标题。

确认后，可用一个明确的新画布任务验收输入：

> 打开系统画图，在新建空白画布上画一只简单的小猫。如果已有窗口包含内容，先停止并询问我，不覆盖已有画作。开始时 start_control，每笔后重新观察，结束时 end_control 并确认 control_active:false；不要保存或关闭窗口。

应看到屏幕控制提示及实际绘画结果，结束后提示消失。焦点激活失败时按工具反馈由用户将目标切到前台。normal_desktop_input:true 只是完整性门槛通过，仍需满足目标权限、焦点和遮挡等条件；sent:true 也不代表实际画出了内容。

### 第 13 步：后续日常使用

首次配置完成后，日常只需打开 AI 客户端并启用此 MCP，无需每次安装依赖、下载 SDK 或打开终端。

移动项目目录后修改 MCP 路径；更新依赖后重新 npm ci；修改原生源码后重新构建并补齐运行库，再重启 MCP。每次重建、复制部署后复查完整性标签。用户按物理 Esc 取消后，只有明确要求恢复时才重启服务。

## 已有便携包：完整运行步骤

便携包已包含 Node 依赖、原生运行时及 OCR 模型，不需要上面的源码构建步骤。

1. 安装 Node.js 20+，重新打开 PowerShell，确认 node --version 显示正确版本；不需要 Git 或 .NET SDK。
2. 将维护者提供的完整便携目录解压到固定位置，保留 node_modules、runtime、lib 等所有随包文件。
3. 在该目录打开 PowerShell，运行 `node cli.mjs doctor`，按源码流程第 9 步检查原生状态。
4. 按源码流程第 10 步配置客户端，脚本路径必须指向解压目录中的 server.mjs。
5. 按第 11 步加入 SKILL 并检查客户端实际连接的 MCP doctor。
6. 按第 12 步验证窗口读取及实际输入，之后按第 13 步日常使用。

不要在便携目录执行源码构建命令；它不包含完整构建脚本和源码。移动目录后需要更新客户端配置路径。

## 可选：调整输入授权范围

| 模式 | server.mjs 后的参数 | 范围 |
|---|---|---|
| 只读，默认 | 无 | 窗口查询、截图、UIA、OCR；不发送输入 |
| 所有普通窗口 | `--allow-input --all-windows` | 保留禁用应用、观察有效期、焦点、遮挡及 Esc 检查 |
| 指定窗口 | `--allow-input --window-id 123456` | 使用真实编号；多个窗口可重复 --window-id |

--all-windows 与 --window-id 不能混用。只读或指定窗口模式的 permissions 不同。窗口重开后编号可能改变。输入授权不代表删除数据、发送消息或付款等业务动作也已获批准。
## 操作流程与 14 个工具

| 工具 | 用途 |
|---|---|
| doctor | 后端、完整性、授权和取消状态 |
| list_windows | 窗口编号、标题和进程 |
| start_control / end_control | 开始提示；结束提示、恢复指针并清除观察 |
| launch_app | 启动 paint、notepad、calculator，要求所有普通窗口授权 |
| observe | 截图、UIA、OCR；返回单次有效的 observation_id |
| element_action | 按当前观察的 element_id 执行 UIA 操作 |
| activate_window | 尝试将刚观察的窗口切到前台 |
| click / type_text / press_key | 点击、字面文本、组合键 |
| scroll / drag | 滚动、拖拽 |
| draw_path | 按住左键沿多个点连续画一笔，最后松开 |

典型流程：`doctor → start_control → list_windows/launch_app → observe → 一个动作 → observe → end_control`。启动请求发出不等于窗口就绪，激活失败时需用户将目标切到前台。

- **观察单次有效**：输入携带最新 observation_id，60 秒内有效且只能使用一次。失败、已使用或窗口变化后重新观察。
- **纯控件操作**：observe 设置 `include_screenshot:false, include_text:true`，再通过 element_action 执行 invoke、select、set_value、toggle、expand、collapse 或 focus。不支持的 Pattern 明确报错，不自动回退点击。
- **截图与坐标**：默认使用原始客户区像素，不含标题栏。局部放大使用 region 与 scale；操作放大图时设 `coordinate_space:"image"`，不自行重复加裁剪偏移。
- **OCR**：显式设 `include_ocr:true`。即使不输出图片，也会本地截图；密集条目和数字识别需要核对。
- **绘画**：draw_path 接收 2–512 个点，时长 200–8000 毫秒；先确认画笔、颜色和画布，每笔消耗一次观察，笔画间重新检查截图。
- **结果验证**：sent:true 只表示输入已发送，不能证明命中了预期对象或业务成功。超时结果未知时不直接重试。

截图 auto 依次尝试 WGC、PrintWindow、screen_region，返回实际后端和回退原因。显式指定后端失败就报错。screen_region 要求前台客户区完整位于单个屏幕内且无遮挡，不输出整张桌面。WGC API 可用不保证每个窗口都可捕获，以实际返回为准。

## 结束控制与边界

任务结束、出错或交还用户前，必须调用 end_control 并确认 control_active:false。5 分钟空闲释放只作异常兜底；常驻连接无法准确判断远端聊天何时结束。

物理 Esc 停止后续输入并锁定当前实例，恢复指针；Agent 注入的 Esc 不触发此锁。取消后只读仍可用，只有用户明确要求恢复时才能重启服务。Esc 停止本地输入，不会强制终止远端模型生成。

禁止通过本工具操作终端、运行对话框、密码管理器、身份认证、安全或隐私设置等受限界面。服务不会自动提权、解除宿主隔离或执行任意命令。已发送的输入不能撤回，必要的松键清理仍执行。

## 常见问题：能看不能点

先检查当前 MCP doctor，而不是根据另一个 Shell 的权限推断后端状态。

1. permissions.input_enabled:false：当前连接为只读模式，检查启动参数并重新连接。
2. normal_desktop_input:false：区分启动进程令牌与 DesktopHelper.exe 文件标签；Medium Node 启动带 Low 标签的 EXE 也可能得到 Low 后端。
3. control_cancelled:true：用户已按物理 Esc，停止输入并交还用户。
4. 完整性及授权正常仍失败：检查目标是否为更高权限应用，或存在焦点、遮挡、最小化、过期观察等问题。

可只读检查部署文件标签：

```powershell
icacls "D:\tools\computer-use-standalone\runtime\win-x64-0.3.0-final\DesktopHelper.exe"
```

可信部署副本误继承 Low 标签时，应在用户明确授权下针对该文件修复，随后重启 MCP 验证。不要批量修改整个工作区或宿主隔离。重新构建或复制后需复查；Git 不保存 Windows 完整性标签，本机修复不会随提交自动传播。

## 开发验证与生成便携包

完成上述源码初始化后，在项目根目录执行：

```powershell
npm test
npm run package
```

测试覆盖授权、观察消费、坐标换算、UIA 状态回读、OCR、截图和取消清理，并用自建画布验证鼠标轨迹。受限环境可能跳过真实输入子测试，需检查输出；历史验收及局限见 [VERIFICATION.md](VERIFICATION.md)。

打包输出 `dist/computer-use-standalone-0.4.0-win-x64`，其中包括运行依赖、模型、许可证和原生哈希清单。可压缩后作为 Release 附件分发；在目标机器上仍需 Node.js 20+，并重新检查路径与完整性。

维护源码目录；dist 不含完整原生源码、构建脚本和测试，重新打包可能覆盖其中修改，不应作为唯一维护副本。
## Git 提交与发布范围

以本目录作为 Git 仓库根目录，.gitignore 约定如下：

| 分类 | 路径或文件 | 处理方式 |
|---|---|---|
| 源码 | server.mjs、cli.mjs、lib/、native/、helper/ 源文件 | 提交 |
| 通用脚本和测试 | 构建、下载、打包、诊断脚本和 tests/ | 提交 |
| 依赖定义 | package.json、package-lock.json、helper/packages.lock.json、csproj | 提交锁文件，不提交已安装依赖 |
| 文档及许可记录 | README、SKILL、VERIFICATION、THIRD_PARTY、licenses/、配置示例 | 提交；示例使用占位路径 |
| 依赖和缓存 | node_modules/、.npm-cache/、.build/、helper/bin/、helper/obj/ | 忽略，可重新生成 |
| 运行时和便携包 | runtime/、dist/ | 忽略；便携包压缩后作为 Release 附件 |
| 本机数据和备份 | artifacts/、.local/、.env、备份和临时文件 | 忽略，不上传截图、令牌或真实本机配置 |
| 原插件参考 | upstream/、upstream-manifest.json | 忽略，仅作本地参考 |
| 本机专用脚本 | configure-host.mjs、三个 paint 验收脚本 | 忽略，用于本机配置或固定窗口验收 |

运行时中的来源、模型版本、许可证及哈希随便携包一起提供，源码保留重建脚本和相关许可记录。不要直接把整个工作目录打包作为源码提交。

.gitignore 只影响未跟踪文件，不会自动移除已提交文件或清除 Git 历史。提交前检查暂存清单；实际密码和令牌也可能出现在未被忽略的源码中，忽略规则不能替代内容检查。

## 第三方组件与项目许可

第三方来源见 [THIRD_PARTY.md](THIRD_PARTY.md) 和 licenses/。发布便携包时保留 .NET、Tesseract、语言模型、微软应用本地运行库及 Node 依赖随附的许可信息。

本项目目前没有顶层 LICENSE 文件。公开源码前需要由维护者确定自有代码的授权方式；第三方组件的许可证不代表整个项目采用同一授权。
