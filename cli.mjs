import { Backend } from './lib/backend.mjs';
const [command, raw] = process.argv.slice(2);
if (!['doctor','list_windows','observe'].includes(command)) {
  console.error('用法：node cli.mjs doctor | list_windows | observe <窗口编号>'); process.exitCode=1;
} else {
  const backend = new Backend();
  try {
    const args = command==='observe' ? { window_id:Number(raw), include_screenshot:true, include_text:false } : {};
    if (command==='observe' && (!Number.isSafeInteger(args.window_id) || args.window_id<1)) throw new Error('需要有效窗口编号');
    console.log(JSON.stringify(await backend.request(command,args)));
  } catch (e) { console.error(e.message); process.exitCode=1; }
  finally { backend.close(); }
}
