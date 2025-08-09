# Path to AssemblyInfo.cs
$assemblyInfoPath = "Properties\AssemblyInfo.cs"

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

[xml]$csproj = Get-Content -Path "ScreenStateService.csproj"
# Define the namespace manager and add MSBuild namespace
$ns = New-Object System.Xml.XmlNamespaceManager($csproj.NameTable)
$ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")

# Select the RootNamespace node using the namespace prefix
$rootNamespaceNode = $csproj.SelectSingleNode("//msb:RootNamespace", $ns)

# Output the value
$rootNamespace = $rootNamespaceNode.InnerText

# Path to generated properties
$outputPath = "Installer\GeneratedVariables.props"

$content = @"
<Project>
    <PropertyGroup>
        <RootNamespace>$rootNamespace</RootNamespace>
        <ProductVersion>$version</ProductVersion>
    </PropertyGroup>
</Project>
"@

Set-Content -Path $outputPath -Value $content -Encoding UTF8
Write-Host "Generated properties to $outputPath"
