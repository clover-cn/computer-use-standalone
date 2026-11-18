import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

for (const mode of ['read-only','all-normal-windows']) {
test(`真实 MCP 握手及权限检查：${mode}`, {skip:process.platform!=='win32'}, async()=>{
  const flags=mode==='read-only'?[]:['--allow-input','--all-windows'];
  const transport = new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL('../server.mjs',import.meta.url)),...flags]});
  const client = new Client({name:'standalone-test',version:'1.0.0'});
  try {
    await client.connect(transport);
    const tools=await client.listTools();
    assert.equal(tools.tools.length,14);
    const status=await client.callTool({name:'doctor',arguments:{}});
    assert.equal(JSON.parse(status.content[0].text).ready,true);
    assert.equal(typeof JSON.parse(status.content[0].text).input_status.normal_desktop_input,'boolean');
    assert.equal(typeof JSON.parse(status.content[0].text).visual_feedback,'boolean');
    assert.equal(JSON.parse(status.content[0].text).control_active,false);
    const ended=await client.callTool({name:'end_control',arguments:{}});
    assert.equal(JSON.parse(ended.content[0].text).control_active,false);
    assert.equal(JSON.parse(status.content[0].text).permissions.scope,mode);
    const blocked=await client.callTool({name:'click',arguments:{window_id:1,observation_id:'00000000-0000-4000-8000-000000000000',x:0,y:0}});
    assert.equal(blocked.isError,true);
    assert.match(blocked.content[0].text,mode==='read-only'?/未获/:/观察/);
  } finally { await client.close(); }
});
}
