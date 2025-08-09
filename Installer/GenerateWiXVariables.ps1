# Path to AssemblyInfo.cs
$assemblyInfoPath = "..\Properties\AssemblyInfo.cs"
$projectDir = Resolve-Path ..\

if (-Not (Test-Path $assemblyInfoPath)) {
    Write-Error "AssemblyInfo.cs not found at $assemblyInfoPath"
    exit 1
}

# Read the file
$content = Get-Content $assemblyInfoPath -Raw

# Extract version
if ($content -match 'AssemblyVersion\("([^"]+)"\)') {
    $version = $matches[1]
} else {
    Write-Error "AssemblyVersion not found"
    exit 1
}

# Extract company
if ($content -match 'AssemblyCompany\("([^"]+)"\)') {
    $company = $matches[1]
} else {
    Write-Error "AssemblyCompany not found"
    exit 1
}

# Constants (stable GUIDs)
$upgradeCode = "e3b0c442-98fc-1c14-9afb-c4c929e7d7a1"
$componentGuidService = "f47ac10b-58cc-4372-a567-0e02b2c3d479"
$componentGuidHelper = "a74a41eb-53e4-4fcb-9f5f-5f3c21d2c4d1"

# Path to generated WiX include file
$outputPath = "GeneratedVariables.wxi"

$content = @"
<?xml version='1.0' encoding='UTF-8'?>
<Include>
  <?define ProductVersion="$version" ?>
  <?define Manufacturer="$company" ?>
  <?define UpgradeCode="$upgradeCode" ?>
  <?define ComponentGuidService="$componentGuidService" ?>
  <?define ComponentGuidHelper="$componentGuidHelper" ?>
  <?define UpstreamProjectDir="$($projectDir.ToString().Replace('\','\\'))" ?>
</Include>
"@

Set-Content -Path $outputPath -Value $content -Encoding UTF8
Write-Host "Generated WiX variables to $outputPath"
