param([Parameter(Mandatory)][string]$ReleaseDir)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$destination=Join-Path $ReleaseDir 'Docs/third-party'
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item "$repo/docs/third-party/*" $destination -Force
$assets=Get-Content "$repo/src/Wysicraft.Designer/obj/project.assets.json" -Raw | ConvertFrom-Json
$packageRoot=$assets.packageFolders.psobject.Properties.Name | Select-Object -First 1
$inventory=[Collections.Generic.List[string]]::new()
$inventory.Add('Third-party components distributed with Arcadia Studio. License files retain their upstream terms. No Minecraft game files are included.')
foreach($entry in $assets.libraries.psobject.Properties | Where-Object {$_.Value.type -eq 'package'}) {
    $base=Join-Path $packageRoot $entry.Value.path
    [xml]$spec=Get-Content (Get-ChildItem $base -Filter '*.nuspec' | Select-Object -First 1).FullName
    $license=$spec.package.metadata.license.InnerText
    $inventory.Add("$($entry.Name) — $license — $($spec.package.metadata.projectUrl)")
    foreach($file in Get-ChildItem $base -File | Where-Object {$_.Name -match '(?i)license|notice'}) {
        Copy-Item $file.FullName (Join-Path $destination (($entry.Name -replace '/','-')+'-'+$file.Name)) -Force
    }
}
foreach($framework in $assets.project.frameworks.psobject.Properties.Value) {
    foreach($dependency in $framework.downloadDependencies) {
        if($dependency.name -notmatch '\.Runtime\.win-x64$'){continue}
        $version=($dependency.version.Trim('[',']') -split ',')[0].Trim()
        $base=Join-Path $packageRoot ($dependency.name.ToLower()+'/'+$version)
        $inventory.Add("$($dependency.name)/$version — .NET runtime license and third-party notices")
        foreach($file in Get-ChildItem $base -File | Where-Object {$_.Name -match '(?i)license|notice'}) {
            Copy-Item $file.FullName (Join-Path $destination ($dependency.name+'-'+$version+'-'+$file.Name)) -Force
        }
    }
}
$inventory.Add('Bundled GraalVM libraries 24.1.2: polyglot, js-language, regex, truffle-api, collections, nativeimage, word, shaded icu4j. See Graal*, Truffle* and SDK* notices. Upstream: oracle/graal and oracle/graaljs, tag vm-24.1.2.')
Copy-Item "$repo/tools/Wysicraft.AppHost/NOTICES.txt" (Join-Path $destination 'Microsoft.Web.WebView2-NOTICES.txt') -Force
$inventory.Add('Microsoft.Web.WebView2 SDK loader (BSD-3-Clause), linked into Runtime/WysicraftAppHost.exe and copied into exported Windows apps with its notice.')
$inventory.Add('Basic Pitch note detection model (Spotify AB, Apache-2.0), embedded in Wysicraft.Core.dll for turning recordings into notes. See BasicPitch-LICENSE.txt.')
$inventory.Add('Demucs v4 (htdemucs) instrument splitter (Meta Platforms, MIT), not bundled: downloaded once on request from huggingface.co/StemSplitio/htdemucs-onnx into the user''s app data folder.')
$inventory.Add('GeneralUser GS SoundFont v2.0.3 (S. Christian Collins, GeneralUser GS License v2.0: free to use and redistribute), bundled as Designer/SoundFonts/GeneralUser GS.sf2. See GeneralUserGS-LICENSE.txt.')
$inventory.Add('Optional SoundFonts, not bundled: downloaded on request into the user''s app data folder. FluidR3 GM (Frank Wen, MIT), FatBoy (CC BY-SA 3.0), Arachno SoundFont (Maxime Abbey, free for non-commercial use).')
$inventory.Add('Gradle wrapper/distribution: Apache-2.0; Gradle distribution is downloaded separately by Test in Minecraft.')
$inventory.Add('Microsoft package license expressions above use the MIT text included in the .NET LICENSE files; their additional notices are copied individually.')
$inventory | Set-Content (Join-Path $destination 'COMPONENTS.txt')
