import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import { randomUUID } from 'node:crypto';

export class Backend {
  constructor() {
    if (process.platform !== 'win32') throw new Error('仅支持 Windows 10/11');
    const native = fileURLToPath(new URL('../runtime/win-x64-0.3.0-final/DesktopHelper.exe',import.meta.url));
    if (!existsSync(native)) throw new Error('原生辅助程序缺失，请使用完整便携包或先构建 helper；不会静默回退旧后端');
    this.pending = new Map();
    // 固定程序路径，工具参数只经标准输入传递，不拼接为命令。
    this.child = spawn(native, [], {
      windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.child.stderr.on('data', data => process.stderr.write(data));
    createInterface({ input: this.child.stdout }).on('line', line => {
      let response;
      try { response = JSON.parse(line); } catch { this.fail(new Error('后端输出无效，已停止')); return; }
      const pending = this.pending.get(response.id);
      if (!pending) return;
      clearTimeout(pending.timer); this.pending.delete(response.id);
      if (response.error) pending.reject(new Error(response.error)); else pending.resolve(response.result);
    });
    this.child.on('error', error => this.fail(error));
    this.child.on('exit', () => this.fail(new Error('Windows 后端已退出；操作结果可能未知，请重新观察')));
    this.child.stdin.on('error', error => this.fail(error));
  }
  fail(error) {
    this.closed = true;
    for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(error); }
    this.pending.clear();
    this.child.kill();
  }
  request(op, args = {}) {
    if (this.closed) return Promise.reject(new Error('后端已停止，请重启服务'));
    const id = randomUUID();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => this.fail(new Error('后端超时，已停止；不自动重试输入')), 20000);
      this.pending.set(id, { resolve, reject, timer });
      this.child.stdin.write(`${JSON.stringify({ id, op, args })}\n`);
    });
  }
  close() {
    if (this.closed) return Promise.resolve();
    if (!this.closing) this.closing = this.request('end_control').catch(() => {}).finally(() => this.fail(new Error('服务关闭')));
    return this.closing;
  }
}
