param(
    [string]$PackageName = "com.yanicloud7.error-whisperer",
    [string]$RepositoryName = "Yani-cloud7 VRChat Creator Tools",
    [string]$RepositoryId = "com.yanicloud7.vpm",
    [string]$RepositoryAuthor = "Yani-cloud7",
    [string]$BaseUrl = "https://Yani-cloud7.github.io/vrchat-error-whisperer"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$packageDir = Join-Path $root "Packages\$PackageName"
$packageJsonPath = Join-Path $packageDir "package.json"
$sourceCorpusPath = Join-Path $root "validation\errors\vrchat-error-corpus.json"
$packageCorpusPath = Join-Path $packageDir "Editor\vrchat-error-corpus.json"
$matcherTestPath = Join-Path $root "tools\test-matcher.ps1"
$distDir = Join-Path $root "vpm-repository"
$zipsDir = Join-Path $distDir "packages"

if (-not (Test-Path -LiteralPath $packageJsonPath)) {
    throw "Package manifest not found: $packageJsonPath"
}

if (-not (Test-Path -LiteralPath $sourceCorpusPath)) {
    throw "Source corpus not found: $sourceCorpusPath"
}

Write-Host "Validating matcher fixtures..."
& powershell -NoProfile -ExecutionPolicy Bypass -File $matcherTestPath -CorpusPath $sourceCorpusPath

Copy-Item -LiteralPath $sourceCorpusPath -Destination $packageCorpusPath -Force
$sourceCorpus = Get-Content -LiteralPath $sourceCorpusPath -Raw | ConvertFrom-Json
$packageCorpus = Get-Content -LiteralPath $packageCorpusPath -Raw | ConvertFrom-Json
if ($sourceCorpus.cases.Count -ne $packageCorpus.cases.Count) {
    throw "Package corpus copy mismatch: source=$($sourceCorpus.cases.Count), package=$($packageCorpus.cases.Count)"
}

New-Item -ItemType Directory -Force -Path $zipsDir | Out-Null

$package = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
$zipName = "$($package.name)-$($package.version).zip"
$zipPath = Join-Path $zipsDir $zipName

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("error-whisperer-vpm-" + [guid]::NewGuid().ToString("N"))
$tempPackageDir = Join-Path $tempDir $package.name
New-Item -ItemType Directory -Force -Path $tempPackageDir | Out-Null

try {
    Copy-Item -Path (Join-Path $packageDir "*") -Destination $tempPackageDir -Recurse -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempPackageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force
    }
}

$sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$downloadUrl = "$BaseUrl/packages/$zipName"
$package.url = $downloadUrl

$versionManifest = [ordered]@{}
foreach ($property in $package.PSObject.Properties) {
    $versionManifest[$property.Name] = $property.Value
}
$versionManifest["zipSHA256"] = $sha

$repo = [ordered]@{
    name = $RepositoryName
    id = $RepositoryId
    url = "$BaseUrl/index.json"
    author = $RepositoryAuthor
    packages = [ordered]@{
        $package.name = [ordered]@{
            versions = [ordered]@{
                $package.version = $versionManifest
            }
        }
    }
}

$repo | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $distDir "index.json") -Encoding UTF8

Write-Host "Built VPM repository:"
Write-Host "  $(Join-Path $distDir "index.json")"
Write-Host "  $zipPath"
Write-Host "Upload vpm-repository/index.json and vpm-repository/packages/ to a public host."
Write-Host "Then set the repo URL in VCC to: $BaseUrl/index.json"
