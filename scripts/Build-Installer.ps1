param([Parameter(Mandatory)][string]$ReleaseDir,[string]$Version='1.0.0-rc.1',[int]$Build=0,[switch]$TestInstall)
$ErrorActionPreference='Stop'
$compiler=Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if(!$compiler) {foreach($base in @(${env:ProgramFiles(x86)},$env:ProgramFiles)) {if(Test-Path "$base/Inno Setup 6/ISCC.exe"){$compiler="$base/Inno Setup 6/ISCC.exe";break}}}
if(!$compiler){throw 'Install Inno Setup 6, or add ISCC.exe to PATH, to build the installer.'}
# The installer's file version carries the build number as its fourth part (1.4.0.37).
$numeric=(((($Version -split '-')[0].Split('.') + @('0','0','0'))[0..2]) + @([string]$Build)) -join '.'
$arguments=@('/Qp',"/DReleaseDir=$([IO.Path]::GetFullPath($ReleaseDir))","/DAppVersion=$Version","/DNumericVersion=$numeric")
if($TestInstall){$arguments+='/DTestInstall'}
& $compiler @arguments "$PSScriptRoot/../installer/ArcadiaStudio.iss"
if($LASTEXITCODE){throw 'Installer compilation failed'}
