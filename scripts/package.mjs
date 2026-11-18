import {cp,mkdir,readFile,writeFile,readdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
const root=path.resolve('dist/computer-use-standalone-0.4.0-win-x64');await mkdir(root,{recursive:true});
for(const name of ['server.mjs','cli.mjs','lib','node_modules','package.json','package-lock.json','SKILL.md','README.md','VERIFICATION.md','licenses','THIRD_PARTY.md']) await cp(name,path.join(root,name),{recursive:true});
await cp('runtime/win-x64-0.3.0-final',path.join(root,'runtime/win-x64-0.3.0-final'),{recursive:true});
// 便携包只暴露已随包提供的运行命令，开发命令在源码目录使用。
const portablePackage=JSON.parse(await readFile('package.json','utf8'));
portablePackage.scripts={start:'node server.mjs',doctor:'node cli.mjs doctor'};
await writeFile(path.join(root,'package.json'),JSON.stringify(portablePackage,null,2));
await writeFile(path.join(root,'mcp-config.example.json'),JSON.stringify({mcpServers:{'computer-use-standalone':{command:'node',args:[path.join(root,'server.mjs'),'--allow-input','--all-windows']}}},null,2));
const manifest=[];
async function visit(dir){for(const file of await readdir(dir,{withFileTypes:true})){const full=path.join(dir,file.name);if(file.isDirectory())await visit(full);else manifest.push({path:path.relative(root,full),sha256:createHash('sha256').update(await readFile(full)).digest('hex')});}}
await visit(path.join(root,'runtime'));
await writeFile(path.join(root,'runtime-manifest.json'),JSON.stringify(manifest,null,2));
console.log(`便携目录已生成：${root}，原生文件 ${manifest.length} 项已记录 SHA-256`);
