# FFmpeg (LGPL build) and the speech models that come with Arcadia Studio, fetched once into artifacts/ffmpeg and
# artifacts/speech and checked by SHA-256 (a file that doesn't match is removed). Development runs use them there;
# Build-Release.ps1 copies them into the release (FFmpeg/ and Speech/ beside Designer/).
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    function Get-Checked($url,$file,$sha) {
        if((Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sha){ return }
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        Write-Output "Downloading $(Split-Path $file -Leaf)…"
        Invoke-WebRequest $url -OutFile "$file.part" -UseBasicParsing
        if((Get-FileHash "$file.part" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha){ Remove-Item "$file.part" -Force; throw "$(Split-Path $file -Leaf) doesn't match its SHA-256; the download was removed. Run again to fetch it afresh." }
        Move-Item "$file.part" $file -Force
    }

    # FFmpeg 9.0, BtbN's LGPL shared build (no GPL parts: no x264/x265). Only ffmpeg.exe and its libraries ship.
    $ffmpegName='ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0'
    $ffmpegZip="artifacts/ffmpeg/$ffmpegName.zip"
    Get-Checked "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/$ffmpegName.zip" $ffmpegZip '7157177b8a6cb2174c1650ba8c71b363f2c78cba5330f88c4c02cf5b2b880646'
    if(!(Test-Path 'artifacts/ffmpeg/bin/ffmpeg.exe') -or ((Get-Content 'artifacts/ffmpeg/VERSION' -ErrorAction SilentlyContinue) -ne $ffmpegName)){
        if(Test-Path 'artifacts/ffmpeg/bin'){ Remove-Item 'artifacts/ffmpeg/bin' -Recurse -Force }
        New-Item -ItemType Directory -Force 'artifacts/ffmpeg/bin' | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip=[System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $ffmpegZip))
        try {
            foreach($entry in $zip.Entries){
                $name=$entry.Name
                if($entry.FullName -match '/bin/' -and ($name -eq 'ffmpeg.exe' -or $name -like '*.dll')){ [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $repo "artifacts/ffmpeg/bin/$name"),$true) }
                elseif($entry.FullName -match '^[^/]+/LICENSE\.txt$'){ [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $repo 'artifacts/ffmpeg/bin/LICENSE.txt'),$true) }
            }
        } finally { $zip.Dispose() }
        Set-Content 'artifacts/ffmpeg/VERSION' $ffmpegName -Encoding ascii
    }
    Copy-Item 'docs/third-party/FFmpeg-SOURCE.txt' 'artifacts/ffmpeg/bin/SOURCE.txt' -Force

    # Kokoro 82M v1.0 (Apache-2.0), half precision: the same speed as full precision on a CPU at half the size.
    Get-Checked 'https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx' 'artifacts/speech/kokoro-fp16.onnx' '027a25b14aef7d3ae57fd09301ebefbec868e79d55213d07e4f3af442f5ba352'
    # Whisper tiny (MIT), multilingual.
    Get-Checked 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin' 'artifacts/speech/whisper/ggml-tiny.bin' 'be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21'
    # The Kokoro v1.0 voices, from the KokoroSharp package (the Chinese-model voices in voices-zh are left out).
    $assets=Get-Content 'src/Wysicraft.Designer/obj/project.assets.json' -Raw | ConvertFrom-Json
    $packageRoot=$assets.packageFolders.psobject.Properties.Name | Select-Object -First 1
    $kokoro=$assets.libraries.psobject.Properties | Where-Object { $_.Name -like 'KokoroSharp/*' } | Select-Object -First 1
    if(!$kokoro){ throw 'Restore src/Wysicraft.Designer first (dotnet restore): the voices come from the KokoroSharp package.' }
    $voices=Join-Path $packageRoot "$($kokoro.Value.path)/content/voices"
    New-Item -ItemType Directory -Force 'artifacts/speech/voices' | Out-Null
    Copy-Item "$voices/*.npy" 'artifacts/speech/voices/' -Force
    Copy-Item 'docs/third-party/Kokoro-LICENSE.txt' 'artifacts/speech/LICENSE-Kokoro.txt' -Force
    Write-Output "Media tools ready: artifacts/ffmpeg/bin, artifacts/speech ($((Get-ChildItem 'artifacts/speech/voices' -Filter *.npy).Count) voices)"
} finally { Pop-Location }
