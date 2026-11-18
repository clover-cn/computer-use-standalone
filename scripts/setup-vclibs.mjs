import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import path from 'node:path';
const archive=path.resolve('.build/vclibs.appx'),out=path.resolve('runtime/win-x64-0.3.0-final');await mkdir(out,{recursive:true});
const source='https://download.microsoft.com/download/4/7/c/47c6134b-d61f-4024-83bd-b9c9ea951c25/Microsoft.VCLibs.x64.14.00.Desktop.appx';
let bytes;try{bytes=await readFile(archive);}catch{const r=await fetch(source);if(!r.ok)throw Error(r.status);bytes=Buffer.from(await r.arrayBuffer());await writeFile(archive,bytes);}
const code=`Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip=[IO.Compression.ZipFile]::OpenRead('${archive.replaceAll("'","''")}'); try { foreach($entry in $zip.Entries) { if($entry.FullName -match '^(msvcp140.*|vcruntime140.*|concrt140|vcomp140)\\.dll$') { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path '${out.replaceAll("'","''")}' $entry.FullName),$true) } } } finally { $zip.Dispose() }`;
const result=spawnSync('powershell.exe',['-NoProfile','-Command',code],{windowsHide:true,stdio:'inherit'});if(result.status!==0)throw Error('VC 运行库提取失败');
await writeFile(`${out}/vclibs-source.json`,JSON.stringify({source,sha256:createHash('sha256').update(bytes).digest('hex'),note:'微软原始 VCLibs Desktop 运行库，仅应用本地加载，未安装系统包'},null,2));
