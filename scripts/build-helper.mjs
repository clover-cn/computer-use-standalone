import {mkdir} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import path from 'node:path';
const root=path.resolve('.build'),tmp=path.join(root,'tmp');await mkdir(tmp,{recursive:true});
const env={...process.env,DOTNET_CLI_HOME:path.join(root,'dotnet-home'),NUGET_PACKAGES:path.join(root,'nuget'),TEMP:tmp,TMP:tmp,
  NUGET_HTTP_CACHE_PATH:path.join(root,'nuget-http'),NUGET_SCRATCH:path.join(root,'nuget-scratch'),
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE:'1',DOTNET_CLI_TELEMETRY_OPTOUT:'1',DOTNET_ADD_GLOBAL_TOOLS_TO_PATH:'0',DOTNET_GENERATE_ASPNET_CERTIFICATE:'false',MSBUILDDISABLENODEREUSE:'1'};
const child=spawn(path.join(root,'sdk','dotnet.exe'),['publish','helper/DesktopHelper.csproj','-c','Release','-o','runtime/win-x64-0.3.0-final','--self-contained','true','--source','https://api.nuget.org/v3/index.json','-p:NuGetAudit=false','-m:1'],{env,stdio:'inherit',windowsHide:true});
child.on('exit',code=>process.exitCode=code??1);
