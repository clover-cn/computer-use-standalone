import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import { Backend } from './lib/backend.mjs';
import { Controller, parseOptions } from './lib/controller.mjs';

const options = parseOptions(process.argv.slice(2));
const backend = new Backend();
const controller = new Controller(backend, options);
const server = new McpServer({ name: 'computer-use-standalone', version: '0.4.0' });
const window_id = z.number().int().positive().safe();
const observation_id = z.string().uuid();
const x = z.number().int().nonnegative(), y = x;
const coordinate_space=z.enum(['client','image']).default('client');
const action = { window_id, observation_id };
function register(name, description, inputSchema, readOnly = false) {
  server.registerTool(name, { description, inputSchema,
    annotations: { readOnlyHint: readOnly, destructiveHint: !readOnly, openWorldHint: !readOnly } }, async args => {
    try {
      const result = await controller.run(name,args);
      const { image, ...metadata } = result;
      if(name==='doctor')metadata.host_version='0.4.0';
      const content = [{ type:'text', text: JSON.stringify(Array.isArray(result) ? result : metadata) }];
      if (image) content.push({ type:'image', mimeType:'image/png', data:image });
      return { content };
    } catch (error) {
      // 出错立即释放桌面；传输未关闭也不能把提示层留给用户。
      await controller.run('end_control').catch(()=>{});
      return { isError:true, content:[{ type:'text', text:error.message }] };
    }
  });
}
register('doctor','检查本地 Windows 后端是否就绪，不读取窗口画面。',{},true);
register('start_control','在用户授权的电脑操作任务开始时调用：显示持续屏幕渐变、居中 Esc 提示及 Agent 指针。不能解除用户 Esc 取消锁。',{});
register('end_control','任务结束或用户要求停止时调用：结束控制会话，隐藏屏幕渐变并恢复指针；清除观察。Esc 取消锁不会被清除。',{});
register('list_windows','列出当前可见且未最小化的窗口，返回窗口编号、标题和进程。',{},true);
register('launch_app','启动系统画图(paint)、记事本(notepad)或计算器(calculator)，返回实际窗口。需要所有普通窗口授权，不接受任意命令。窗口可能已存在，必须观察并确认内容后再操作。',{
  app:z.enum(['paint','notepad','calculator']),
});
register('observe','读取指定窗口客户区截图或控件文本；截图不包含整个桌面。返回单次有效观察编号。',{
  window_id, include_screenshot:z.boolean().default(true), include_text:z.boolean().default(false),
  region:z.object({x,y,width:z.number().int().positive().max(10000),height:z.number().int().positive().max(10000)}).optional(),
  scale:z.number().int().min(1).max(4).default(1),
  capture_backend:z.enum(['auto','wgc','printwindow','screen_region']).default('auto'),
  include_ocr:z.boolean().default(false),uia_view:z.enum(['control','raw']).default('control'),
},true);
register('element_action','操作当前观察内的真实 UIA 控件，支持纯文字观察。操作后核对返回状态及业务结果；不支持的 Pattern 不自动回退点击。',{
  ...action,element_id:z.string().min(1),action:z.enum(['invoke','select','set_value','toggle','expand','collapse','focus']),value:z.string().max(10000).optional(),
});
register('activate_window','将刚观察的已授权窗口切到前台，失败明确报错；可使用不带截图的观察，之后重新截图。',action);
register('click','点击刚观察的客户区坐标。仅限启动时授权的窗口，操作后必须重新观察。',{
  ...action,x,y,coordinate_space,button:z.enum(['left','right']).default('left'),
});
register('type_text','向当前焦点输入字面文本。先观察确认编辑框焦点，控制键使用 press_key。',{
  ...action,text:z.string().min(1).max(10000).refine(t=>!/[\x00-\x1f\x7f]/.test(t),'控制字符请使用 press_key'),
});
register('press_key','发送组合键，例如 CTRL+A、ENTER、SHIFT+TAB；不支持 Windows 键。',{
  ...action,key:z.string().min(1).max(80),
});
register('scroll','在客户区坐标处滚动；delta 为 120 的倍数，正数向上，负数向下。',{
  ...action,x,y,coordinate_space,delta:z.number().int().min(-2400).max(2400).multipleOf(120),
});
register('drag','在目标客户区内拖动；起终点均为最新截图中的像素坐标。',{
  ...action,x,y,coordinate_space,to_x:x,to_y:y,
});
register('draw_path','按住鼠标左键沿多个点连续绘制一笔，随后松开。用于画图和画布，必须根据截图确认工具、颜色和路径区域；每笔后重新观察。Esc、遮挡、焦点或窗口改变时中止并松键。',{
  ...action,points:z.array(z.object({x,y})).min(2).max(512),coordinate_space,
  duration_ms:z.number().int().min(200).max(8000).default(1200),
});
let closed;
let released;
const release = () => released ??= backend.close();
const close = () => closed ??= release().finally(()=>server.close());
const previousOnClose=server.server.onclose;
server.server.onclose = () => { previousOnClose?.(); void release(); };

process.on('SIGINT', () => void close());
process.on('SIGTERM', () => void close());
process.stdin.on('end', () => backend.close());
process.stdin.on('close', () => backend.close());
process.stdout.on('error', () => backend.close());
await server.connect(new StdioServerTransport());
