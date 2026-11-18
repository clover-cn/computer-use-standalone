import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

for (const name of ['capture','feedback','session']) {
  test(`Windows 原生验证：${name}`,{skip:process.platform!=='win32'},()=>{
    const root=fileURLToPath(new URL('.',import.meta.url));
    const source=readFileSync(new URL(`./${name}.ps1`,import.meta.url),'utf8');
    const executable=`${process.env.SystemRoot}\\System32\\WindowsPowerShell\\v1.0\\powershell.exe`;
    const result=spawnSync(executable,['-NoLogo','-NoProfile','-NonInteractive','-Command',
      `$TestRoot='${root.replaceAll("'","''")}'; ${source}`],{windowsHide:true,encoding:'utf8',timeout:20000});
    assert.equal(result.status,0,result.stderr || String(result.error));
    assert.match(result.stdout,/PASS:/);
  });
}
