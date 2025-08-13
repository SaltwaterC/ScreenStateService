# GenerateInstallerProps.ps1  (PowerShell 5.1)
param(
  [string]$Project = 'ScreenStateService.csproj',
  [Parameter(Mandatory = $false)]
  [string]$Out = ''
)

if(!$Out) {
    throw "Please pass -Out."
}

$ErrorActionPreference = 'Stop'

# Resolve script directory
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Path $MyInvocation.MyCommand.Path -Parent }

# Resolve csproj
if (-not (Test-Path -LiteralPath $Project)) {
  $projPath = Join-Path $scriptDir $Project
} else {
  $projPath = (Resolve-Path -LiteralPath $Project).Path
}

# ---- NEW: validate and resolve $Out ----
if ([string]::IsNullOrWhiteSpace($Out)) {
  throw "Please pass -Out or keep the default."
}

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) {
  $Out
} else {
  Join-Path -Path $scriptDir -ChildPath $Out
}

# Ensure output directory exists
$outDir = [System.IO.Path]::GetDirectoryName($outPath)
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
  New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

Write-Host "Reading project: $projPath"

if (-not (Test-Path -LiteralPath $projPath)) {
  throw "Couldn't find csproj at $projPath"
}

[xml]$csproj = Get-Content -LiteralPath $projPath

function Get-MsBuildProperty([xml]$xml, [string]$name) {
  $n = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$name']")
  if ($n -and $n.InnerText) { return $n.InnerText.Trim() }
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

$rootNamespaceRaw = Get-MsBuildProperty $csproj 'RootNamespace'
$assemblyNameRaw  = Get-MsBuildProperty $csproj 'AssemblyName'
$asmVerRaw        = Get-MsBuildProperty $csproj 'AssemblyVersion'
$fileVerRaw       = Get-MsBuildProperty $csproj 'FileVersion'
$verRaw           = Get-MsBuildProperty $csproj 'Version'
$verPrefixRaw     = Get-MsBuildProperty $csproj 'VersionPrefix'

Write-Host "Found:"
Write-Host "  RootNamespace = '$rootNamespaceRaw'"
Write-Host "  AssemblyName  = '$assemblyNameRaw'"
Write-Host "  AssemblyVersion = '$asmVerRaw'"
Write-Host "  FileVersion     = '$fileVerRaw'"
Write-Host "  Version         = '$verRaw'"
Write-Host "  VersionPrefix   = '$verPrefixRaw'"

$rootNamespace  = First-NonEmpty @($rootNamespaceRaw, $assemblyNameRaw, 'ScreenStateService')
$rawVersion     = First-NonEmpty @($asmVerRaw, $fileVerRaw, $verRaw, $verPrefixRaw, '1.0.0')
$productVersion = To-MsiVersion $rawVersion

# Write props
$content = @"
<Project>
  <PropertyGroup>
    <RootNamespace>$rootNamespace</RootNamespace>
    <ProductVersion>$productVersion</ProductVersion>
  </PropertyGroup>
</Project>
"@

Set-Content -LiteralPath $outPath -Value $content -Encoding UTF8
Write-Host "Generated properties to $outPath"
Write-Host "  RootNamespace  = $rootNamespace"
Write-Host "  ProductVersion = $productVersion   (derived from '$rawVersion')"
