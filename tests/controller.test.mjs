import test from 'node:test';
import assert from 'node:assert/strict';
import { Controller, keyCodes, parseOptions } from '../lib/controller.mjs';

function fixture(options = { allowInput:true, allowed:new Set([42]) }) {
  const window = {id:42,pid:123,process:'fixture',title:'测试窗口',x:10,y:10,width:200,height:100};
  const calls = [];
  const backend = {request:async (op,args) => {
    calls.push(op); backend.lastArgs=args;
    if(op==='observe') return { window:{...window}, ...(args.include_screenshot ? {image:'png',image_region:{...(args.region??{x:0,y:0,width:200,height:100}),scale:args.scale??1}} : {}) };
    if(op==='inspect') return {...window};
    return {sent:true};
  }};
  let now=1000;
  const controller=new Controller(backend,options,()=>now);
  return {controller,window,calls,backend,advance:()=>{now+=61000;}};
}
const observe = async c => (await c.run('observe',{window_id:42,include_screenshot:true})).observation_id;

test('启动应用不扩大指定窗口授权且拒绝任意命令',async()=>{
  for(const options of [parseOptions([]),parseOptions(['--allow-input','--window-id','42'])]) {
    const f=fixture(options);await assert.rejects(f.controller.run('launch_app',{app:'paint'}),/授权/);
    assert.ok(!f.calls.includes('launch_app'));
  }
  const f=fixture(parseOptions(['--allow-input','--all-windows']));
  await assert.rejects(f.controller.run('launch_app',{app:'cmd.exe'}),/仅允许/);
  await f.controller.run('launch_app',{app:'paint',arguments:'bad'});
  assert.deepEqual(f.backend.lastArgs,{app:'paint'});
});

test('绘画轨迹整笔校验，裁剪坐标换算一次且单次消费观察',async()=>{
  const f=fixture();
  const state=await f.controller.run('observe',{window_id:42,include_screenshot:true,region:{x:30,y:10,width:50,height:30},scale:3});
  const args={window_id:42,observation_id:state.observation_id,coordinate_space:'image',points:[{x:0,y:0},{x:45,y:18},{x:90,y:45}],duration_ms:500};
  await f.controller.run('draw_path',args);
  assert.deepEqual(f.backend.lastArgs.points,[{x:30,y:10},{x:45,y:16},{x:60,y:25}]);
  await assert.rejects(f.controller.run('draw_path',args),/观察/);
  const id=await observe(f.controller);
  await assert.rejects(f.controller.run('draw_path',{window_id:42,observation_id:id,points:[{x:0,y:0},{x:201,y:30}],duration_ms:500}),/超出/);
  assert.equal(f.calls.filter(n=>n==='draw_path').length,1);
  const text=await f.controller.run('observe',{window_id:42,include_screenshot:false});
  await assert.rejects(f.controller.run('draw_path',{...args,observation_id:text.observation_id}),/截图/);
});
test('放大图坐标换算保留裁剪偏移并拒绝图外点击',async()=>{
  const f=fixture();
  const s=await f.controller.run('observe',{window_id:42,include_screenshot:true,region:{x:30,y:10,width:50,height:30},scale:3});
  await f.controller.run('click',{window_id:42,observation_id:s.observation_id,x:45,y:18,coordinate_space:'image'});
  assert.equal(f.backend.lastArgs.x,45); assert.equal(f.backend.lastArgs.y,16);
  const s2=await f.controller.run('observe',{window_id:42,include_screenshot:true,region:{x:30,y:10,width:50,height:30},scale:3});
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:s2.observation_id,x:150,y:18,coordinate_space:'image'}),/裁剪图片/);
});
test('只读不能开始控制；正常结束会清除未用观察',async()=>{
  const ro=fixture(parseOptions([]));
  await assert.rejects(ro.controller.run('start_control'),/未获输入授权/);
  assert.ok(!ro.calls.includes('start_control'));
  const f=fixture(); const id=await observe(f.controller);
  await f.controller.run('end_control');
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1}),/观察/);
});
test('默认只读拒绝输入',async()=>{
  const f=fixture(parseOptions([])); const id=await observe(f.controller);
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1}),/未获/);
  assert.ok(!f.calls.includes('click'));
});
test('窗口授权和组合键校验',()=>{
  assert.throws(()=>parseOptions(['--allow-input']),/window-id/);
  assert.throws(()=>parseOptions(['--window-id','bad']),/有效/);
  assert.deepEqual(keyCodes('CTRL+SHIFT+A'),[17,16,65]);
  assert.throws(()=>keyCodes('WIN+R'),/不支持/);
});
test('所有普通窗口模式需要显式授权且不能混用范围',()=>{
  assert.throws(()=>parseOptions(['--all-windows']),/allow-input/);
  assert.throws(()=>parseOptions(['--allow-input','--all-windows','--window-id','42']),/同时/);
});
test('所有普通窗口授权允许通过控制器，仍消耗观察',async()=>{
  const f=fixture(parseOptions(['--allow-input','--all-windows']));
  const id=await observe(f.controller);
  await f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1});
  assert.equal(f.calls.filter(x=>x==='click').length,1);
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1}),/观察/);
  const status=await f.controller.run('doctor');
  assert.equal(status.permissions.scope,'all-normal-windows');
  assert.equal(status.permissions.input_enabled,true);
});
test('doctor 准确报告只读状态和指定窗口授权',async()=>{
  for(const args of [[],['--allow-input','--window-id','42']]) {
    const f=fixture(parseOptions(args)); const status=await f.controller.run('doctor');
    assert.equal(status.permissions.scope,args.length?'window-ids':'read-only');
    assert.deepEqual(status.permissions.window_ids,args.length?[42]:[]);
  }
});
test('过期观察和窗口移动均拒绝输入',async()=>{
  for(const change of ['expired','moved']) {
    const f=fixture(); const id=await observe(f.controller);
    if(change==='expired') f.advance(); else f.window.x++;
    await assert.rejects(f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1}));
    assert.ok(!f.calls.includes('click'));
  }
});
test('不带截图的观察不能用于坐标输入',async()=>{
  const f=fixture(); const s=await f.controller.run('observe',{window_id:42,include_screenshot:false});
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:s.observation_id,x:1,y:1}),/截图/);
});
test('并发动作只能消费同一观察一次',async()=>{
  const f=fixture(); const id=await observe(f.controller);
  const args={window_id:42,observation_id:id,x:1,y:1};
  const results=await Promise.allSettled([f.controller.run('click',args),f.controller.run('click',args)]);
  assert.equal(results.filter(r=>r.status==='fulfilled').length,1);
  assert.equal(f.calls.filter(n=>n==='click').length,1);
});
test('越界或后端失败后也不能复用观察',async()=>{
  const f=fixture(); const id=await observe(f.controller);
  await assert.rejects(f.controller.run('drag',{window_id:42,observation_id:id,x:1,y:1,to_x:200,to_y:1}),/超出/);
  await assert.rejects(f.controller.run('click',{window_id:42,observation_id:id,x:1,y:1}),/观察/);
});
