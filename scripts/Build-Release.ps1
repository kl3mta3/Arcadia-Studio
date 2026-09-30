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
    $release=Join-Path $repo "artifacts/ArcadiaStudio-$version"
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
    # butler, itch.io's uploader (MIT), for File → Publish to itch.io: a fixed version from itch.io's broth, checked by
    # its SHA-256 and kept in artifacts/butler between builds. Only butler.exe ships (the 7-Zip DLLs beside it upstream
    # aren't needed to push).
    $butlerVersion='15.31.0'; $butlerZipSha='92e42f011db049128583ac88258d3309d00c69018d2a48b378c7eb5709d9efde'
    $butlerZip="artifacts/butler/butler-$butlerVersion-windows-amd64.zip"
    New-Item -ItemType Directory -Force 'artifacts/butler' | Out-Null
    if(!(Test-Path $butlerZip)){ Invoke-WebRequest "https://broth.itch.zone/butler/windows-amd64/$butlerVersion/archive/default" -OutFile $butlerZip -UseBasicParsing }
    if((Get-FileHash $butlerZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $butlerZipSha){ Remove-Item $butlerZip -Force; throw "butler $butlerVersion doesn't match its SHA-256; the download was removed. Build again to fetch it afresh." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if(Test-Path "$release/Butler"){ Remove-Item "$release/Butler" -Recurse -Force }
    New-Item -ItemType Directory -Force "$release/Butler" | Out-Null
    $zip=[System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $butlerZip))
    try { [System.IO.Compression.ZipFileExtensions]::ExtractToFile(($zip.Entries | Where-Object { $_.FullName -eq 'butler.exe' }), "$release/Butler/butler.exe", $true) } finally { $zip.Dispose() }
    # The same butler for development runs (the editor looks in artifacts/butler too).
    Copy-Item "$release/Butler/butler.exe" 'artifacts/butler/butler.exe' -Force
    Copy-Item 'docs/third-party/butler-LICENSE.txt' "$release/Butler/LICENSE.txt" -Force
    # The user manual: one offline HTML page generated from the wiki pages (developer notes in docs/ are not shipped).
    & dotnet run --project tools/Wysicraft.ManualBuilder -c Release -- wiki "$release/Docs/Arcadia-Studio-Manual.html" $version
    if($LASTEXITCODE){throw 'Manual build failed'}
    if(Test-Path "$release/README.md"){ Remove-Item "$release/README.md" -Force }
    Copy-Item LICENSE $release -Force
    & "$PSScriptRoot/Collect-Notices.ps1" -ReleaseDir $release
    Compress-Archive "$release/*" "artifacts/ArcadiaStudio-$version-win-x64.zip" -Force
    # Checksums for the in-app updater (upload each .sha256 with its file to the GitHub release; GitHub's own digest is
    # used when present, these otherwise).
    function Write-Checksum($file) { $h=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(); Set-Content "$file.sha256" "$h  $(Split-Path $file -Leaf)" -Encoding ascii }
    Write-Checksum "artifacts/ArcadiaStudio-$version-win-x64.zip"
    # Arcadia trusts official web runtime builds by their SHA-256. Exports write the runtime byte-for-byte from this file
    # (PublishChecks checks that), so this is the hash to add under Setup → Publishing → Trusted Wysicraft runtime builds.
    $runtimeHash=(Get-FileHash src/Wysicraft.Web/wysicraft-web.js -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content "artifacts/ArcadiaStudio-$version-runtime-SHA256.txt" "$runtimeHash  wysicraft/wysicraft-web.js (Arcadia Studio $version web runtime)" -Encoding ascii
    Write-Output "Web runtime SHA-256: $runtimeHash"
    if($Installer){ & "$PSScriptRoot/Build-Installer.ps1" -ReleaseDir $release -Version $version; Write-Checksum "artifacts/ArcadiaStudio-$version-Setup.exe" }
    Write-Output "Release: $release (editor tests and exports use Runtime/wysicraft-$runtimeVersion.jar)"
} finally {Pop-Location}

