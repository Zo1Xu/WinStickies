param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.8 is required.' }
$outputFolder = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$executable = Join-Path $outputFolder 'WinStickies.exe'
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /codepage:65001 "/out:$executable" "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" "/win32icon:$(Join-Path $projectRoot 'assets\WinStickies.ico')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
Write-Output "Built: $executable"
if ($Test) {
    $testFolder = Join-Path $projectRoot 'artifacts\self-test'
    $process = Start-Process -FilePath $executable -ArgumentList @('--self-test', '--data-dir', ('"' + $testFolder + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $testFolder 'test-results.txt')
    if ($process.ExitCode -ne 0) { throw "Tests failed ($($process.ExitCode))." }
}
