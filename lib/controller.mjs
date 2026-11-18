import { randomUUID } from 'node:crypto';

const actionNames = new Set(['click', 'type_text', 'press_key', 'scroll', 'drag', 'draw_path', 'activate_window', 'element_action']);
export function parseOptions(args) {
  const allowed = new Set(); let allowInput = false; let allWindows = false;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--allow-input') allowInput = true;
    else if (args[i] === '--all-windows') allWindows = true;
    else if (args[i] === '--window-id') {
      const id = Number(args[++i]);
      if (!Number.isSafeInteger(id) || id < 1) throw new Error('--window-id 必须为有效窗口编号');
      allowed.add(id);
    } else throw new Error(`未知参数：${args[i]}`);
  }
  if (allWindows && !allowInput) throw new Error('--all-windows 必须与 --allow-input 一起使用');
  if (allWindows && allowed.size) throw new Error('--all-windows 与 --window-id 不能同时使用');
  if (allowInput && !allWindows && !allowed.size) throw new Error('开启输入时必须用 --window-id 或 --all-windows 指定授权范围');
  return { allowInput, allowed, allWindows };
}

export function keyCodes(chord) {
  const names = { CTRL:17, CONTROL:17, ALT:18, SHIFT:16, ENTER:13, RETURN:13, TAB:9,
    ESC:27, ESCAPE:27, SPACE:32, BACKSPACE:8, DELETE:46, HOME:36, END:35,
    LEFT:37, UP:38, RIGHT:39, DOWN:40, PAGEUP:33, PAGEDOWN:34 };
  const parts = chord.toUpperCase().split('+');
  if (!parts.length || parts.length > 5) throw new Error('按键组合无效');
  const result = parts.map(p => {
    if (names[p]) return names[p];
    if (/^[A-Z0-9]$/.test(p)) return p.charCodeAt(0);
    if (/^F([1-9]|1[0-2])$/.test(p)) return 111 + Number(p.slice(1));
    throw new Error(`不支持的按键：${p}`);
  });
  if (new Set(result).size !== result.length) throw new Error('按键重复');
  return result;
}

export class Controller {
  constructor(backend, options, now = Date.now) {
    this.backend = backend; this.options = options; this.now = now;
    this.snapshots = new Map(); this.queue = Promise.resolve();
  }
  run(name, args = {}) {
    // 所有观察和输入串行执行，避免并发调用交错使用同一快照。
    const next = this.queue.then(() => this.execute(name, args));
    this.queue = next.catch(() => {});
    return next;
  }
  async execute(name, args) {
    if(name==='launch_app') {
      if(!this.options.allowInput || !this.options.allWindows) throw new Error('启动应用需要 --allow-input --all-windows 授权；指定窗口模式不能扩大授权范围');
      if(!['paint','notepad','calculator'].includes(args.app)) throw new Error('仅允许启动 paint、notepad、calculator，不接受命令、路径或参数');
      this.snapshots.clear();
      return this.backend.request(name,{app:args.app});
    }
    if (name === 'start_control') {
      if (!this.options.allowInput) throw new Error('当前服务未获输入授权，不能开始控制会话');
      return this.backend.request(name);
    }
    if (name === 'end_control') {
      this.snapshots.clear();
      return this.backend.request(name);
    }
    if (name === 'doctor') return {
      ...await this.backend.request(name),
      permissions: {
        input_enabled: this.options.allowInput,
        scope: !this.options.allowInput ? 'read-only' : this.options.allWindows ? 'all-normal-windows' : 'window-ids',
        window_ids: [...this.options.allowed],
      },
    };
    if (name === 'list_windows') return this.backend.request(name);
    if (name === 'observe') {
      this.snapshots.delete(args.window_id);
      const state = await this.backend.request('observe', args);
      const observation_id = randomUUID();
      this.snapshots.set(args.window_id, { id: observation_id, at: this.now(), window: state.window, visual: !!state.image, region:state.image_region, elements:new Set((state.controls??[]).map(e=>e.element_id)) });
      return { ...state, observation_id, coordinates: '默认使用原始客户区像素；裁剪放大图可指定 coordinate_space=image，由工具换算，勿重复加偏移', expires_in_seconds: 60 };
    }
    if (!actionNames.has(name)) throw new Error('未知工具');
    if (!this.options.allowInput || (!this.options.allWindows && !this.options.allowed.has(args.window_id)))
      throw new Error('当前窗口未获启动配置的输入授权；请用 doctor 查看权限，并由用户配置 --allow-input --all-windows 或 --window-id');
    const observation = this.snapshots.get(args.window_id);
    // 输入请求无论成功与否都消耗快照，失败后必须重新观察。
    this.snapshots.delete(args.window_id);
    if (!observation || observation.id !== args.observation_id || this.now()-observation.at > 60000)
      throw new Error('观察已过期或已使用，请重新 observe');
    if (['click','scroll','drag','draw_path'].includes(name) && !observation.visual) throw new Error('坐标操作必须先获取截图');
    if (name==='element_action' && !observation.elements.has(args.element_id)) throw new Error('控件不属于当前观察，请重新 observe');
    const current = await this.backend.request('inspect', { window_id: args.window_id });
    for (const k of ['id','pid','process','title','x','y','width','height'])
      if (current[k] !== observation.window[k]) throw new Error('窗口身份、位置、尺寸或标题发生变化，请重新观察');
    if (args.coordinate_space === 'image') {
      const region=observation.region;
      if (!region) throw new Error('该观察没有图片坐标信息，请重新截图');
      const convert=(x,y)=>{
        if(!Number.isInteger(x)||!Number.isInteger(y)||x<0||y<0||x>=region.width*region.scale||y>=region.height*region.scale)
          throw new Error('坐标超出裁剪图片');
        return {x:region.x+Math.floor(x/region.scale),y:region.y+Math.floor(y/region.scale)};
      };
      if(name==='draw_path') args={...args,points:args.points.map(p=>convert(p.x,p.y))};
      else {
      const start=convert(args.x,args.y);
      if (name==='drag') { const end=convert(args.to_x,args.to_y); args={...args,to_x:end.x,to_y:end.y}; }
      args={...args,...start};
      }
    }
    const point = (x,y) => {
      if (!Number.isInteger(x) || !Number.isInteger(y) || x<0 || y<0 || x>=current.width || y>=current.height)
        throw new Error('坐标超出目标客户区');
    };
    if (['click','scroll','drag'].includes(name)) point(args.x,args.y);
    if (name === 'drag') point(args.to_x,args.to_y);
    if(name==='draw_path') {
      if(!Array.isArray(args.points)||args.points.length<2||args.points.length>512)throw new Error('轨迹需要 2–512 个点');
      if(!Number.isInteger(args.duration_ms)||args.duration_ms<200||args.duration_ms>8000)throw new Error('轨迹时长必须为 200–8000 毫秒');
      for(const p of args.points)point(p.x,p.y);
    }
    if (name === 'press_key') args = { ...args, keys: keyCodes(args.key) };
    const result=await this.backend.request(name,args);
    if (name==='element_action') return result;
    return { sent: true, verified: false, next: '请重新 observe 检查界面；输入事件成功不代表应用完成了任务' };
  }
}
