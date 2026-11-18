# 实现与验证记录

## STDIO 辅助程序文件标签修复（2026-11-18）

进一步只读排查发现，实际 STDIO 启动链为 DeepSeek Harness → Medium Node（8192）→ Low DesktopHelper（4096）。icacls 显示 linshi 目录存在向下继承的 Low Mandatory Level，源码运行时及 0.4.0 便携包中的 DesktopHelper.exe 均继承该标签。先前仅根据后端 Low 状态将原因归为宿主限制，证据不足；本节更正该归因。

用户明确要求修复后，只对这两份 DesktopHelper.exe 设置显式 Medium 标签。未改变父目录、DACL、系统策略、其他版本或原版插件；未重启现有用户进程，未发送输入。修改前路径、ACL 与 SHA-256 记录在 artifacts/integrity-fix-before-20261118-170638.json。两份 EXE 修改后的 SHA-256 均与修改前一致；icacls 均显示 Medium Mandatory Level:(NW)。

修复后在普通桌面权限的测试执行环境中，使用官方 MCP SDK 为源码入口及 0.4.0 便携入口分别建立新的 STDIO 只读连接，均完成握手、发现 14 工具并返回 ready:true、integrity_level:8192、integrity_name:medium、normal_desktop_input:true、control_active:false、control_cancelled:false。这两个验证连接未授予输入权限，因此 permissions.input_enabled:false 是预期结果，不是完整性故障。未以此宣称已完成真实客户端点击或绘画验收；用户须重启自己客户端的旧 MCP，再检查原有输入授权和实际状态。

父目录的 Low 标签保留，重新构建或复制可能重新继承 Low，需在可信部署阶段复查。设置 EXE 的 Medium 标签不会提升原本为 Low 的父进程，真正的宿主限制仍需遵守。README 与 SKILL 已同步更正诊断说明。

## 0.4.0 收敛为 STDIO

用户确认 STDIO 已正常使用，并再次确认删除 HTTP 备用方案。清理源码与 0.4.0 便携目录中的桌面启动器、HTTP 入口、鉴权及会话模块、HTTP 专属测试和本地 HTTP 密钥配置；同步移除启动命令、打包引用和使用说明。服务入口只保留 server.mjs，参数校验后才创建后端，删除仅供多 HTTP 会话共享后端的分支。

原 STDIO 配置直接沿用，无需迁移；此前仍使用 HTTP 的客户端需改为 STDIO。14 个工具、观察消费、Esc 取消锁、断连及错误清理、5 分钟空闲兜底保持原有实现。未重新编译或复制原生运行时，保留两份 EXE 已修复的 Medium 标签；未修改原版插件或用户客户端配置。

2026-11-18 17:35（北京时间）：剩余 20 项回归全部通过，0 失败、0 跳过。源码及便携包分别完成 STDIO 只读握手、14 工具发现，均返回 medium、normal_desktop_input:true、control_active:false。两份原生 EXE 哈希与修复前记录一致，Medium 标签保留；HTTP 凭据目录已删除，当前代码及打包脚本无失效的 HTTP 入口引用。技能格式校验与 upstream 6 项哈希校验通过。源码及便携目录共删除 9 个 HTTP 文件，另删除 2 个本地凭据文件及其空目录，未读取密钥内容。

## 0.3.0 应用启动、连续绘画与权限诊断

新增固定应用启动、窗口激活、多点连续笔画；观察仍为单次消费，整笔开始前校验所有点，执行时检查窗口身份、布局、焦点、遮挡、Esc 和实际鼠标位置，异常时松开左键。启动只接受 paint/notepad/calculator，不接收命令、路径或参数。

系统画图实测 WGC 拒绝访问、PrintWindow 失败，因此增加 screen_region：仅截取完整无遮挡的前台客户区，拒绝越屏、覆盖和捕获保护；不会输出整屏或遮挡窗口内容。WGC 成功帧仍未验证。

定位到当前常驻后端为低完整性（4096），画图为中完整性（8192）。低完整性 MCP 的实际移动未到目标，现已增加执行前完整性比较及鼠标位置回读，避免继续发送无效笔画。doctor.input_status 显式报告这一状态。没有自动提权、修改令牌或加入跨沙箱代理。

用同一个 DesktopHelper.dll 在工具明确授权的正常宿主内，真实完成画图中的 14 笔猫脸，并检查 artifacts/paint-cat.png；不使用图片导入或程序生成图像替代鼠标绘画。普通画图窗口保留，未覆盖用户文件。此次是原生组件真实验收，不能称为低完整性 MCP 完成了整条流程。MCP 需要在宿主允许的正常桌面上下文运行；当前受限宿主的完整自动流程尚未通过。

自动测试另包含固定应用授权和路径校验、裁剪轨迹换算及单次观察、MCP 工具发现，以及测试画布实际收到连续移动和松键事件。旧服务占用文件时采用并排版本目录，不强杀其他客户端后端。原版插件及 upstream 未改动。

## 0.2.0 原生后端、UIA、WGC 与 OCR

已使用项目内官方 .NET SDK 8.0.428 构建 Windows x64 自包含辅助程序。Node MCP 入口保持 server.mjs，运行时改为 DesktopHelper.exe，不再静默加载旧 PowerShell 后端；native/bridge.ps1 仅留作旧版源码参考，原生输入/提示源文件仍用于回归测试。

17 项测试通过：原授权、观察消费与取消/清理检查；自建测试窗无需截图完成 UIA 值设置、按钮调用、切换、选择、树展开及状态回读；控件替换后旧引用被拒绝；中英文 OCR 从同次局部截图得到文本、置信度和正确客户区位置。测试仅修改自建测试窗。

WGC 已编译，但此环境 CreateForWindow 实测返回 E_ACCESSDENIED (0x80070005)。显式 WGC 返回错误，auto 明确回退 PrintWindow 并返回原因；没有获得 WGC 成功帧，因此 GPU 内容、真实 WGC 像素映射、HDR、混合 DPI 等仍需在允许捕获的目标环境验收，不能声称已全部验证。

源码构建遇到受限 .NET 加载器初始化失败及用户缓存目录不可写；构建在获准执行环境中进行，SDK、NuGet、HTTP 缓存及临时文件均已配置在 .build。运行原生辅助程序在此受限 shell 中也可能 DLL 初始化失败，外部 MCP 服务需正常用户权限环境。没有修改系统捕获权限。

OCR 使用固定版本 NuGet Tesseract 5.2.0 和固定提交 tessdata_fast 中英文模型；模型、微软应用本地 VC 运行库以及便携目录的来源与哈希均有记录。后端未要求系统 .NET SDK，但未在全新 Windows 虚拟机上测试；当前测试机器已有开发工具，不能据此宣称所有干净系统已验收。

便携目录包含 Node 依赖、原生运行时、OCR 模型、应用本地 VC DLL、许可证和 SHA-256 清单，不包含原版 upstream 或构建 SDK。搬迁后需修改 MCP 服务路径，仍要求 Node.js 20+。

## 空闲阈值恢复与技能收尾要求

按用户纠正，将空闲兜底恢复为 300 秒（5 分钟），不以短超时替代任务收尾。SKILL 明确要求在最终回复、结束任务、等待接管或出错退出前调用 end_control，并检查 control_active:false；失败时停止输入并报告未完成关闭。断连和错误清理保留。

原生测试断言阈值为 300 秒，并仅在测试内将最近活动时间模拟为 301 秒前，验证超时清理及指针恢复，无需实际等待 5 分钟。下方 8 秒描述属于被替换版本的历史记录。

## 视觉减淡、遗漏收尾与放大定位修正

去掉指针底圈；边缘渐变从 90px 降到 28px，单边 alpha 从 85/95 降至 24/28。预览和实际屏幕使用同一个渲染函数。

旧实现把 5 分钟空闲当成取消，无法及时处理常驻 MCP 连接上的任务收尾遗漏。现在 8 秒空闲即正常结束会话并恢复指针；工具执行错误、MCP 传输关闭、标准输入 end/close 和标准输出错误均触发清理。物理 Esc 取消锁仍保留。无法在宿主不通知结束且保持 MCP 连接时精确判断远端任务完成，不能承诺与聊天结束同时清除。

observe 增加区域裁剪和整数倍放大，坐标输入工具增加显式 image/client 坐标空间，按同一观察的原点与倍数换算。不新增 UIA 内容、不访问 DSH 内部接口，不声称可判断错误点击的业务语义。

新增与更新的测试覆盖局部放大 PNG 的尺寸与颜色、图片坐标换算与越界拒绝、渐变范围及透明度、指针外无底圈、真实 8 秒空闲后的指针恢复，以及真实 MCP 客户端开始控制后故意省略 end_control 直接断连的清理路径。未点击用户应用。

## 持续会话、Esc 退出及 Agent 指针（2026-11-18）

替换此前的瞬时客户区蒙版。当前实现使用每像素透明分层窗显示各屏幕边缘渐变和顶部居中提示，工作区保持透明；增加 start_control/end_control 会话工具和物理 Esc 低级键盘钩子。钩子只识别物理 Esc，不记录其他按键，不把 SendInput 的注入 Esc 当成用户取消。

取消状态在每次输入及移动前检查；松开清理继续允许。取消锁不会被观察、正常结束、下一次 start_control 清除。输入工具默认自动开始会话，技能要求显式开始并在任务末尾正常结束。长时间空闲自动取消，多实例通过命名互斥锁避免同时占用桌面。

正常会话暂时替换普通/文本/链接三类系统指针，结束后用先前备份恢复；另有独立进程监听后端退出，在异常结束时尝试重载 Windows 主题指针。异常退出的恢复守护代码已加入，尚未用强制杀进程方式端到端验证。

原生测试验证：渐变透明度与居中提示像素；超过旧 0.9 秒后会话持续存在；同一回调路径区分物理/注入 Esc；取消后原生点击、输入与再次开始均被拒绝；真实桌面会话不抢焦点；三类系统指针确实改变并在正常结束后逐一恢复原图像。没有向用户应用发送键盘或鼠标输入，没有实际按硬件 Esc。

离线预览 artifacts/session-preview.png 使用虚构背景和实际渲染函数，未读取用户桌面内容。参考微软官方文档：

- https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow

## 早期瞬时提示验证记录（已被上述会话方案替换）

新增独立 WinForms 提示线程，显示客户区淡色蒙版、边框、动作说明和点击圆环；默认启用，可通过 `COMPUTER_USE_FEEDBACK=0` 关闭。禁用窗口输入、设置禁激活和透明样式，并通过原生命中测试跳过提示层，避免干扰原有遮挡校验。

`npm test` 共 13 项通过。新增屏幕外原生测试验证提示层尺寸、边框与中心标记像素、禁输入状态、前台焦点不变以及超时自动隐藏；客户区截图测试再次通过。没有向用户窗口发出输入。未对真实应用逐项验证点击、拖拽时的合成效果，也未验证独占全屏场景。

实现依据为微软官方的 WindowFromPoint 和扩展窗口样式说明：

- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint
- https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles

## 输入授权更新

用户明确选择允许所有普通应用窗口后，新增 `--allow-input --all-windows` 模式。无参数仍保持只读；指定窗口授权继续可用。`doctor` 新增实际权限返回，便于识别旧进程或错误配置。

本机 Codex 的 `mcp_servers.computer-use-standalone` 已更新为所有普通窗口授权，Magpie 中独立技能的 SKILL.md 和 README.md 已同步。修改前分别保留旁路备份。原始插件及 upstream 副本未修改。配置更新不改变旧进程的内存状态，需要客户端重新连接/重启 MCP 服务；没有终止用户进程。

新增测试验证全部窗口模式的显式授权要求、互斥参数、观察校验继续生效，以及真实 MCP 只读/全部窗口两种启动模式的 doctor 权限。测试不对用户窗口发出输入。其他 AI 客户端需要采用更新后的 `mcp-config.example.json`，不会自动继承 Codex 配置。

验证时间：2026-11-17 09:09 UTC。

## 执行计划与结果

1. 研究：读取原技能、接口、运行和确认文档；确认原插件不含可独立分发的执行后端。
2. 复制：完整复制 6 个原始文件至 `upstream/`，保存 SHA-256 清单。全部改动位于当前工作目录的新目录中。
3. 实现：编写独立 Windows 后端、官方 SDK stdio MCP 服务、跨客户端技能及接入配置；不修改用户客户端配置。
4. 验证：完成以下自动检查，保留未验证范围。

## 已验证

- `npm test`：7 项通过，覆盖默认只读、授权名单、快捷键校验、观察过期、窗口移动、截图前置要求、单次观察消费、并发动作串行化、越界拒绝、MCP 握手与工具发现、后端编译及错误返回。
- `node cli.mjs doctor`：独立后端启动成功，不导入 Codex 运行库。
- `tests/capture.ps1`：自建屏幕外 WinForms 测试窗，捕获 180×100 客户区 PNG；中心像素 RGB 为预期的 (20,100,200)。测试没有截取用户窗口，没有发出键鼠输入。
- 对 `upstream-manifest.json` 的 6 个条目逐一校验：原目录当前内容及副本内容均与初始 SHA-256 一致。
- 技能入口包含名称、描述、操作约束和可解析的本地文档引用；当前 Python 入口不可用，未运行 skill-creator 的 Python 校验器。

## 未验证与差异

- 未对用户已有窗口进行实际点击、打字或拖拽；输入路径经过编译和模拟控制器验证，没有经过真实应用端到端验证。
- 未验证真实欢乐斗地主或其他 GPU 游戏；PrintWindow 返回成功也可能得到空白、黑屏或陈旧内容，需要人工/视觉核实。
- UI Automation 文本读取代码已加载和编译，未针对真实应用逐项验证。
- 未验证第三方 AI 客户端的配置 UI 和图片转发；已通过官方 MCP SDK 客户端测试。
- 本实现不等同于原版 WGC 截图，不提供原版应用安装列表、控件索引操作、文档全文和中断 hooks。

## 环境问题处理

- 系统默认 npm 缓存目录不可写：改为当前项目 `.npm-cache`，未提升权限。
- 系统 Windows PowerShell 的文件执行策略拒绝未签名 `.ps1`：后端采用 `-Command` 加载本地固定脚本内容。未修改执行策略、未添加 Bypass 参数；不支持禁止 PowerShell/动态 C# 的组织策略环境。
- 未遇到 HTTP 429、5xx 或网络超时，无网络退避重试。

## 官方依据

- Context7：`/modelcontextprotocol/typescript-sdk`，查询得到 1.32.2 文档示例；实际安装版本以 `package-lock.json` 为准。
- https://github.com/modelcontextprotocol/typescript-sdk/blob/1.32.2/docs/server.md
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
