param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin\distribution')
)

$ErrorActionPreference = 'Stop'
$releaseDirectory = Join-Path $OutputDirectory ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $releaseDirectory) { throw "Output already exists: $releaseDirectory" }
$null = New-Item -ItemType Directory -Path $releaseDirectory
$publishDirectory = Join-Path $releaseDirectory 'publish'

dotnet publish (Join-Path $PSScriptRoot 'Wisp.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDirectory 2>&1 |
    Tee-Object -FilePath (Join-Path $releaseDirectory 'publish.log')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$executable = Join-Path $publishDirectory 'Wisp.App.exe'
$firstRun = Join-Path $publishDirectory 'FIRST_RUN.md'
if (!(Test-Path -LiteralPath $executable) -or !(Test-Path -LiteralPath $firstRun)) {
    throw 'Publish did not produce Wisp.App.exe and FIRST_RUN.md.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $executable
if ($signature.Status -ne 'NotSigned') { throw "Expected an unsigned v1 executable; found $($signature.Status)." }
$archive = Join-Path $releaseDirectory 'Wisp-win-x64.zip'
# All runtime content is bundled in the executable. Debug symbols stay in publish/.
Compress-Archive -LiteralPath $executable, $firstRun -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
"$hash  Wisp-win-x64.zip" | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt')
Write-Output "Distribution: $archive"
Write-Output "SHA256: $hash"
