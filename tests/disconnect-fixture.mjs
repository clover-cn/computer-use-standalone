import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';
import { fileURLToPath } from 'node:url';
const client=new Client({name:'disconnect-fixture',version:'1.0.0'});
try {
  await client.connect(new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL('../server.mjs',import.meta.url)),'--allow-input','--all-windows']}));
  const result=await client.callTool({name:'start_control',arguments:{}});
  if(result.isError) throw new Error(result.content[0].text);
  // 故意不调用 end_control，验证客户端关闭连接时的兜底清理。
} finally { await client.close(); }
