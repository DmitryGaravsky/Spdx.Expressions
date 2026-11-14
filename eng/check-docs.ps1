#Requires -Version 7.0
[CmdletBinding()]
param([string]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Documentation.ps1"
$root = Split-Path $PSScriptRoot -Parent
Test-DocumentLinks -Root $root
Test-DocumentExamples -Root $root
if ($Package) {
    Test-PackageReadme -Root $root -Package (Resolve-Path -LiteralPath $Package).Path
}
