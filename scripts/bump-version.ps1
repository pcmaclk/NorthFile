param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be SemVer core format: x.y.z"
}

$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $root "VERSION"
$cargoToml = Join-Path $root "rust_engine\Cargo.toml"
$csproj = Join-Path $root "FileExplorerUI\FileExplorerUI.csproj"

Set-Content -Path $versionFile -Value $Version -Encoding utf8NoBOM

$cargo = Get-Content -Raw -Path $cargoToml
$cargo = [regex]::Replace($cargo, '(?m)^version\s*=\s*".*"$', "version = `"$Version`"")
Set-Content -Path $cargoToml -Value $cargo -Encoding utf8NoBOM

[xml]$xml = Get-Content -Raw -Path $csproj
$pg = $xml.Project.PropertyGroup | Select-Object -First 1

function Set-Or-CreateNode {
    param(
        [xml]$Doc,
        $Group,
        [string]$Name,
        [string]$Value
    )
    $node = $Group.$Name
    if ($null -eq $node) {
        $node = $Doc.CreateElement($Name)
        $node.InnerText = $Value
        [void]$Group.AppendChild($node)
    }
    else {
        $node.InnerText = $Value
    }
}

$parts = $Version.Split(".")
$assemblyVersion = "$($parts[0]).$($parts[1]).0.0"
$fileVersion = "$Version.0"

Set-Or-CreateNode -Doc $xml -Group $pg -Name "Version" -Value $Version
Set-Or-CreateNode -Doc $xml -Group $pg -Name "InformationalVersion" -Value $Version
Set-Or-CreateNode -Doc $xml -Group $pg -Name "FileVersion" -Value $fileVersion
Set-Or-CreateNode -Doc $xml -Group $pg -Name "AssemblyVersion" -Value $assemblyVersion

$xml.Save($csproj)

Write-Output "Version updated to $Version"
Write-Output "Updated:"
Write-Output " - $versionFile"
Write-Output " - $cargoToml"
Write-Output " - $csproj"
