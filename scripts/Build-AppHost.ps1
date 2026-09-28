# Builds the Windows app host (tools/Wysicraft.AppHost/AppHost.cpp) into artifacts/apphost/WysicraftAppHost.exe.
# Needs Visual Studio's C++ tools. The WebView2 SDK comes from NuGet; the C runtime is linked in (/MT), so the
# host runs on any Windows 10/11 PC without extra installs.
param([string]$Output)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$Output){$Output=Join-Path $repo 'artifacts/apphost/WysicraftAppHost.exe'}
$project=Join-Path $repo 'tools/Wysicraft.AppHost'
& dotnet restore (Join-Path $project 'WebView2Sdk.csproj') -v q
if($LASTEXITCODE){throw 'WebView2 SDK restore failed'}
$sdk=Get-ChildItem (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.web.webview2') -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1
if(!$sdk){throw 'WebView2 SDK not found in the NuGet cache'}
$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs=& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Visual Studio C++ tools not found'}
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$env:PATH+=";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$native=Join-Path $sdk.FullName 'build/native'
$obj=Join-Path $repo 'artifacts/apphost/obj'; New-Item -ItemType Directory -Force $obj,(Split-Path $Output -Parent) | Out-Null
$command="`"$vcvars`" >nul && cl /nologo /O2 /MT /EHsc /std:c++17 /W3 /DUNICODE /D_UNICODE /I`"$native/include`" /Fo`"$obj\\`" `"$project/AppHost.cpp`" /link /SUBSYSTEM:WINDOWS /OUT:`"$Output`" `"$native/x64/WebView2LoaderStatic.lib`" user32.lib gdi32.lib ole32.lib shell32.lib shlwapi.lib advapi32.lib version.lib"
& cmd.exe /d /c $command
if($LASTEXITCODE){throw 'App host build failed'}
# The WebView2 SDK loader is linked in: its license notice travels with the program (and into exported apps).
Copy-Item (Join-Path $project 'NOTICES.txt') (Join-Path (Split-Path $Output -Parent) 'WysicraftAppHost-NOTICES.txt') -Force
Write-Output "App host: $Output (WebView2 SDK $($sdk.Name))"
