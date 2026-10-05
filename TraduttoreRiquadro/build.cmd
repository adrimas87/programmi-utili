@echo off
rem Compila Traduttore.exe con il compilatore C# incluso in Windows (.NET Framework)
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
rem usa la versione piu' recente del Windows SDK installata
for /d %%d in ("C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.*") do if exist "%%d\Windows.winmd" set WINMD=%%d\Windows.winmd
"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:"%~dp0Traduttore.exe" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%WINMD%" ^
  "%~dp0Traduttore.cs"
