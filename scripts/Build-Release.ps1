param([string]$JavaHome=$env:JAVA_HOME,[switch]$Installer)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $env:JAVA_HOME=$JavaHome
    & ./wysicraft-runtime/gradlew.bat -p wysicraft-runtime jar
    if($LASTEXITCODE){throw 'Runtime build failed'}
    $runtimeVersion=[regex]::Match((Get-Content wysicraft-runtime/build.gradle -Raw),"version = '([^']+)'").Groups[1].Value
    $version=[regex]::Match((Get-Content src/Wysicraft.Designer/Wysicraft.Designer.csproj -Raw),'<Version>([^<]+)</Version>').Groups[1].Value
    if(!$version){$version=$runtimeVersion}
    $release=Join-Path $repo "artifacts/Wysicraft-$version"
    & dotnet publish src/Wysicraft.Designer -c Release -r win-x64 --self-contained true -o "$release/Designer"
    if($LASTEXITCODE){throw 'Designer build failed'}
    # Docs is rebuilt from scratch each time: the user manual plus third-party notices, nothing else.
    if(Test-Path "$release/Docs"){ Remove-Item "$release/Docs" -Recurse -Force }
    # Runtime is rebuilt too, so an older runtime JAR from a previous build never ships.
    if(Test-Path "$release/Runtime"){ Remove-Item "$release/Runtime" -Recurse -Force }
    New-Item -ItemType Directory -Force "$release/Runtime","$release/TestEnvironment","$release/Docs"|Out-Null
    Copy-Item "wysicraft-runtime/build/libs/wysicraft-$runtimeVersion.jar" "$release/Runtime/" -Force
    foreach($name in @('gradlew','gradlew.bat','build.gradle','settings.gradle','gradle.properties','gradle','src')) {
        Copy-Item "wysicraft-runtime/$name" "$release/TestEnvironment/" -Recurse -Force
    }
    # Windows app host for "Export → Windows app", with its notice (the WebView2 SDK loader is linked in).
    & "$PSScriptRoot/Build-AppHost.ps1" -Output "$release/Runtime/WysicraftAppHost.exe"
    if($LASTEXITCODE){throw 'App host build failed'}
    # The user manual: one offline HTML page generated from the wiki pages (developer notes in docs/ are not shipped).
    & dotnet run --project tools/Wysicraft.ManualBuilder -c Release -- wiki "$release/Docs/Wysicraft-Manual.html" $version
    if($LASTEXITCODE){throw 'Manual build failed'}
    if(Test-Path "$release/README.md"){ Remove-Item "$release/README.md" -Force }
    Copy-Item LICENSE $release -Force
    & "$PSScriptRoot/Collect-Notices.ps1" -ReleaseDir $release
    Compress-Archive "$release/*" "artifacts/Wysicraft-$version-win-x64.zip" -Force
    if($Installer){ & "$PSScriptRoot/Build-Installer.ps1" -ReleaseDir $release -Version $version }
    Write-Output "Release: $release (editor tests and exports use Runtime/wysicraft-$runtimeVersion.jar)"
} finally {Pop-Location}

