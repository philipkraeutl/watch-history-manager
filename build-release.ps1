param(
    [string]$Version = "0.1.0.0",
    [string]$Configuration = "Release",
    [string]$ProjectPath = ".\Jellyfin.Plugin.WatchHistoryManager\Jellyfin.Plugin.WatchHistoryManager.csproj",
    [string]$PublishDir = ".\publish",
    [string]$DistDir = ".\dist",
    [switch]$UpdateManifest
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

$RepoRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Get-Location
}

Set-Location $RepoRoot

$ProjectFullPath = Resolve-Path $ProjectPath
$PublishFullPath = Join-Path $RepoRoot $PublishDir
$DistFullPath = Join-Path $RepoRoot $DistDir
$ZipFileName = "watch-history-manager_$Version.zip"
$ZipFullPath = Join-Path $DistFullPath $ZipFileName

Write-Step "Cleaning previous output"

if (Test-Path $PublishFullPath) {
    Remove-Item $PublishFullPath -Recurse -Force
}

if (!(Test-Path $DistFullPath)) {
    New-Item -ItemType Directory -Force -Path $DistFullPath | Out-Null
}

if (Test-Path $ZipFullPath) {
    Remove-Item $ZipFullPath -Force
}

Write-Step "Running dotnet clean"

dotnet clean $ProjectFullPath -c $Configuration

Write-Step "Running dotnet restore"

dotnet restore $ProjectFullPath

Write-Step "Publishing plugin"

dotnet publish $ProjectFullPath -c $Configuration -o $PublishFullPath

Write-Step "Checking required files"

$PluginDll = Join-Path $PublishFullPath "Jellyfin.Plugin.WatchHistoryManager.dll"
$StartPointScript = Join-Path $PublishFullPath "Web\startpoint-button.js"

if (!(Test-Path $PluginDll)) {
    throw "Plugin DLL not found: $PluginDll"
}

if (!(Test-Path $StartPointScript)) {
    throw "startpoint-button.js not found: $StartPointScript"
}

Write-Host "Found plugin DLL: $PluginDll" -ForegroundColor Green
Write-Host "Found startpoint script: $StartPointScript" -ForegroundColor Green

Write-Step "Creating ZIP with Linux-compatible folder paths"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ZipArchive = [System.IO.Compression.ZipFile]::Open(
    $ZipFullPath,
    [System.IO.Compression.ZipArchiveMode]::Create
)

try {
    $PublishRoot = (Resolve-Path $PublishFullPath).Path.TrimEnd("\", "/")

Get-ChildItem -Path $PublishRoot -Recurse -File | ForEach-Object {
    $FilePath = $_.FullName

    $RelativePath = $FilePath.Substring($PublishRoot.Length).TrimStart("\", "/")

    # Important:
    # ZIP entries must use "/" and not "\".
    # Otherwise Linux extracts "Web\startpoint-button.js" as one file name.
    $ZipEntryPath = $RelativePath.Replace("\", "/")

    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $ZipArchive,
        $FilePath,
        $ZipEntryPath,
        [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null
}
}
finally {
    $ZipArchive.Dispose()
}

Write-Step "Validating ZIP structure"

$ZipRead = [System.IO.Compression.ZipFile]::OpenRead($ZipFullPath)

try {
    $Entries = $ZipRead.Entries | ForEach-Object { $_.FullName }

    $BadEntries = $Entries | Where-Object { $_ -like "*\*" }

    if ($BadEntries.Count -gt 0) {
        Write-Host "Invalid ZIP entries found:" -ForegroundColor Red
        $BadEntries | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "ZIP contains Windows backslash paths."
    }

    if ($Entries -notcontains "Web/startpoint-button.js") {
        Write-Host "ZIP entries:" -ForegroundColor Yellow
        $Entries | ForEach-Object { Write-Host $_ }
        throw "ZIP does not contain Web/startpoint-button.js at the expected path."
    }
}
finally {
    $ZipRead.Dispose()
}

Write-Host "ZIP structure is valid." -ForegroundColor Green

Write-Step "Generating MD5 checksum"

$Md5 = (Get-FileHash $ZipFullPath -Algorithm MD5).Hash

Write-Host ""
Write-Host "ZIP:" -ForegroundColor Green
Write-Host $ZipFullPath

Write-Host ""
Write-Host "MD5 checksum:" -ForegroundColor Green
Write-Host $Md5

if ($UpdateManifest) {
    Write-Step "Updating manifest.json"

    $ManifestPath = Join-Path $RepoRoot "manifest.json"

    if (!(Test-Path $ManifestPath)) {
        throw "manifest.json not found: $ManifestPath"
    }

    $Manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $FoundVersion = $false
    $Timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    foreach ($Plugin in $Manifest) {
        foreach ($PluginVersion in $Plugin.versions) {
            if ($PluginVersion.version -eq $Version) {
                $PluginVersion.checksum = $Md5
                $PluginVersion.timestamp = $Timestamp
                $FoundVersion = $true
            }
        }
    }

    if (!$FoundVersion) {
        throw "Version $Version was not found in manifest.json"
    }

    $ManifestJson = $Manifest | ConvertTo-Json -Depth 20
    Set-Content -Path $ManifestPath -Value $ManifestJson -Encoding UTF8

    Write-Host "manifest.json updated." -ForegroundColor Green
}

Write-Step "Done"

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Upload this ZIP to your GitHub release:"
Write-Host "   $ZipFullPath"
Write-Host ""
Write-Host "2. Make sure manifest.json uses this checksum:"
Write-Host "   $Md5"
Write-Host ""
Write-Host "3. Commit and push manifest changes:"
Write-Host "   git add manifest.json"
Write-Host "   git commit -m `"Update plugin release checksum`""
Write-Host "   git push"