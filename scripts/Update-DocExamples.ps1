<#
.SYNOPSIS
    Rewrites the committed outputs of the documentation examples from the current code.

.DESCRIPTION
    Runs every scenario of docs/examples/homelab/scenarios.json through the test
    DocumentationExampleOutputTests with PORTATLAS_UPDATE_DOC_EXAMPLES=1, which replaces
    docs/examples/homelab/output/<scenario>/ with the files the command writes.

    Run it after a reviewed change of an output, or after adding a scenario, then read the
    diff of docs/examples/homelab/output: every change there is a change seen by the readers.
    The tables quoted by the pages are checked by the same test class without this variable.

.EXAMPLE
    ./scripts/Update-DocExamples.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'PortAtlas.Tests/PortAtlas.Tests.csproj'

$env:PORTATLAS_UPDATE_DOC_EXAMPLES = '1'
try {
    dotnet test $project -v q --nologo --filter 'FullyQualifiedName~DocumentationExampleOutputTests.Run_WhenGivenAnExampleScenario'
    if ($LASTEXITCODE -ne 0) {
        throw "A scenario failed (exit code $LASTEXITCODE): its outputs were not replaced."
    }
}
finally {
    Remove-Item Env:PORTATLAS_UPDATE_DOC_EXAMPLES -ErrorAction SilentlyContinue
}

git -C $root status --short -- docs/examples/homelab/output
