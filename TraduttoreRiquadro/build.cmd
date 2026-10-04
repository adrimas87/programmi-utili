@echo off
rem Compila Traduttore.exe con il compilatore C# incluso in Windows (.NET Framework)
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WINMD=C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd
"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:"%~dp0Traduttore.exe" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%WINMD%" ^
  "%~dp0Traduttore.cs"
