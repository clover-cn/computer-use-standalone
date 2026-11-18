import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {fileURLToPath} from 'node:url';
import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StdioClientTransport} from '@modelcontextprotocol/sdk/client/stdio.js';

test('原生后端：纯 UIA 操作、失效引用、截图和本地 OCR',{skip:process.platform!=='win32',timeout:40000},async(t)=>{
  const executable=fileURLToPath(new URL('../runtime/win-x64-0.3.0-final/DesktopHelper.exe',import.meta.url));
  const fixture=spawn(executable,['--fixture'],{windowsHide:false,stdio:['pipe','pipe','pipe']});
  const ready=await Promise.race([once(fixture.stdout,'data'),new Promise((_,reject)=>setTimeout(()=>reject(Error('测试窗未启动')),5000))]);
  assert.match(String(ready[0]),/fixture-ready/);
  const client=new Client({name:'helper-test',version:'1'});
  try {
    await client.connect(new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL('../server.mjs',import.meta.url)),'--allow-input','--all-windows'],env:{...process.env,COMPUTER_USE_FEEDBACK:'0'}}));
    const call=async(name,args={})=>{const result=await client.callTool({name,arguments:args});if(result.isError)throw Error(result.content[0].text);return{data:JSON.parse(result.content[0].text),content:result.content};};
    const windows=(await call('list_windows')).data;
    const window=windows.find(w=>w.title==='ComputerUse.TestFixture');assert.ok(window);
    const window_id=window.id;
    const read=async()=>{
      const result=await call('observe',{window_id,include_screenshot:false,include_text:true});
      assert.ok(!result.content.some(c=>c.type==='image'));return result.data;
    };
    const act=async(predicate,action,value)=>{const state=await read();const control=state.controls.find(predicate);assert.ok(control,`控件不存在 ${action}`);return call('element_action',{window_id,observation_id:state.observation_id,element_id:control.element_id,action,...(value!==undefined?{value}:{})});};
    assert.equal((await act(c=>c.automation_id==='edit','set_value','中文 UIA 123')).data.verified,true);
    await act(c=>c.name==='Invoke','invoke');assert.ok((await read()).controls.some(c=>c.name==='Clicked'));
    assert.equal((await act(c=>c.name==='Toggle','toggle')).data.verified,true);
    assert.equal((await act(c=>c.name==='Second','select')).data.verified,true);
    assert.equal((await read()).controls.find(c=>c.name==='Second').selected,true);
    assert.equal((await read()).controls.find(c=>c.name==='Toggle').toggle_state,'On');
    assert.equal((await act(c=>c.name==='Parent','expand')).data.verified,true);
    assert.equal((await read()).controls.find(c=>c.name==='Parent').expand_state,'Expanded');
    assert.equal((await act(c=>c.name==='Parent','collapse')).data.verified,true);
    const state=await read();const edit=state.controls.find(c=>c.automation_id==='edit');
    fixture.stdin.write('replace\n');await new Promise(r=>setTimeout(r,150));
    await assert.rejects(call('element_action',{window_id,observation_id:state.observation_id,element_id:edit.element_id,action:'set_value',value:'must reject'}));
    let image;
    try {image=await call('observe',{window_id,include_screenshot:true,capture_backend:'wgc'});assert.equal(image.data.capture.backend,'wgc');}
    catch(error) {
      if(!/0x80070005/.test(error.message))throw error;
      console.log('WGC 实测被环境拒绝，验证 auto 回退：'+error.message);
      image=await call('observe',{window_id,include_screenshot:true,capture_backend:'auto'});
      assert.equal(image.data.capture.backend,'printwindow');assert.match(image.data.capture.fallback_reason,/0x80070005/);
    }
    assert.ok(image.content.some(c=>c.type==='image'));
    const ocr=await call('observe',{window_id,include_screenshot:false,include_ocr:true,capture_backend:'printwindow',region:{x:0,y:260,width:680,height:100},scale:2});
    assert.match(ocr.data.ocr.text,/HELLO/);assert.ok(ocr.data.ocr.words.some(w=>w.bounds.y>=260));
    await t.test('真实鼠标轨迹到达画布并收到移动与松键事件',async(t)=>{
      const state=(await call('observe',{window_id,include_screenshot:true,capture_backend:'printwindow'})).data;
      try {
        await call('draw_path',{window_id,observation_id:state.observation_id,points:[{x:40,y:380},{x:170,y:365},{x:300,y:390}],duration_ms:600});
      } catch(error) {
        if(/鼠标移动未到达目标位置|无法确认目标窗口拥有前台焦点/.test(error.message)){t.skip('本次执行环境未允许实际鼠标输入：'+error.message);return;}
        throw error;
      }
      await new Promise(r=>setTimeout(r,80));
      assert.ok((await read()).controls.some(c=>/^Stroke:[1-9]/.test(c.name)),'画布必须收到移动及松键事件');
    });
    await call('end_control');
  }finally{await client.close();fixture.stdin.write('close\n');fixture.kill();}
});
