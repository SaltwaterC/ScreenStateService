# GenerateWiXVariables.ps1 (PowerShell 5.1 compatible, robust, now with TargetFramework)
param(
  [string]$Project = '..\ScreenStateService.csproj',
  [string]$Out = 'GeneratedVariables.wxi',
  [string]$UpstreamProjectDir = $null
)

$ErrorActionPreference = 'Stop'

# Resolve script dir
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path) }

# Resolve csproj path
if (-not [System.IO.Path]::IsPathRooted($Project)) {
  $projPath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $Project))
} else {
  $projPath = $Project
}
if (-not (Test-Path -LiteralPath $projPath)) {
  throw "Couldn't find csproj at '$projPath'"
}

# Resolve output path
if (-not [System.IO.Path]::IsPathRooted($Out)) {
  $outPath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $Out))
} else {
  $outPath = $Out
}

# Resolve upstream project dir
if (-not $UpstreamProjectDir) {
  $UpstreamProjectDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
} elseif (-not [System.IO.Path]::IsPathRooted($UpstreamProjectDir)) {
  $UpstreamProjectDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $UpstreamProjectDir))
}

[xml]$csproj = Get-Content -LiteralPath $projPath

function Get-MsBuildProperty([xml]$xml, [string]$name) {
  $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$name']")
  if ($node -and $node.InnerText) { return $node.InnerText.Trim() }
  return $null
}

function First-NonEmpty([string[]]$values) {
  foreach ($v in $values) {
    if ($null -ne $v -and $v.Trim().Length -gt 0) { return $v.Trim() }
  }
  return $null
}

function To-MsiVersion([string]$v) {
  if (-not $v) { return '1.0.0' }
  $parts = ($v -split '[^\d]') | Where-Object { $_ -ne '' }
  if ($parts.Count -eq 0) { return '1.0.0' }
  $parts = $parts | Select-Object -First 3
  while ($parts.Count -lt 3) { $parts += '0' }
  ($parts -join '.')
}

# Read values
$companyRaw = First-NonEmpty @(
  (Get-MsBuildProperty $csproj 'Company'),
  (Get-MsBuildProperty $csproj 'Manufacturer'),
  (Get-MsBuildProperty $csproj 'AssemblyCompany'),
  (Get-MsBuildProperty $csproj 'AssemblyName')
)

$rawVersion = First-NonEmpty @(
  (Get-MsBuildProperty $csproj 'ProductVersion'),
  (Get-MsBuildProperty $csproj 'AssemblyVersion'),
  (Get-MsBuildProperty $csproj 'FileVersion'),
  (Get-MsBuildProperty $csproj 'Version'),
  (Get-MsBuildProperty $csproj 'VersionPrefix')
)
$productVersion = To-MsiVersion $rawVersion

# --- TargetFramework (robust) ---
$targetFramework = Get-MsBuildProperty $csproj 'TargetFramework'
if (-not $targetFramework) {
    $tfms = Get-MsBuildProperty $csproj 'TargetFrameworks'
    if ($tfms) {
        $targetFramework = ($tfms -split ';')[0]
    }
}

# Escape backslashes for WiX
$escapedUpstream = $UpstreamProjectDir -replace '\\','\\'

# Ensure output directory exists
$outDir = [System.IO.Path]::GetDirectoryName($outPath)
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
  New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Write the .wxi
$content = @"
<?xml version='1.0' encoding='UTF-8'?>
<Include>
  <?define ProductVersion="$productVersion" ?>
  <?define Manufacturer="$companyRaw" ?>
  <?define UpstreamProjectDir="$escapedUpstream" ?>
  <?define TargetFramework="$targetFramework" ?>
</Include>
"@

Set-Content -LiteralPath $outPath -Value $content -Encoding UTF8

Write-Host "Generated WiX variables to $outPath"
Write-Host "  ProductVersion    = $productVersion   (from '$rawVersion')"
Write-Host "  Manufacturer      = $companyRaw"
Write-Host "  UpstreamProjectDir= $UpstreamProjectDir"
Write-Host "  TargetFramework   = $targetFramework"
