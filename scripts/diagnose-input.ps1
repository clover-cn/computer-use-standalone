$ErrorActionPreference='Stop'
# 只读查询进程完整性级别，不修改令牌或申请提权。
Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class InputIntegrity {
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr info,int size,out int needed);
 [DllImport("advapi32.dll")]static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
 [DllImport("advapi32.dll")]static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 public static int Level(int pid){
  using(var p=Process.GetProcessById(pid)){
   IntPtr token;if(!OpenProcessToken(p.Handle,8,out token))throw new Exception("token access denied");
   try{int needed;GetTokenInformation(token,25,IntPtr.Zero,0,out needed);IntPtr buffer=Marshal.AllocHGlobal(needed);
    try{if(!GetTokenInformation(token,25,buffer,needed,out needed))throw new Exception("token read failed");IntPtr sid=Marshal.ReadIntPtr(buffer);byte count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)count-1));}
    finally{Marshal.FreeHGlobal(buffer);}}
   finally{CloseHandle(token);}
  }
 }
}
'@
Get-Process mspaint,DesktopHelper -ErrorAction SilentlyContinue | ForEach-Object {
    try { [pscustomobject]@{id=$_.Id;process=$_.ProcessName;integrity=[InputIntegrity]::Level($_.Id)} }
    catch { [pscustomobject]@{id=$_.Id;process=$_.ProcessName;error=$_.Exception.Message} }
} | ConvertTo-Json -Compress
