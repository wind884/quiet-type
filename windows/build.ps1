param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory '轻打.exe'
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:anycpu','/langversion:5','/warn:4','/warnaserror+','/codepage:65001',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/win32icon:' + (Join-Path $PSScriptRoot 'app.ico')),
    '/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:Accessibility.dll','/reference:System.Runtime.Serialization.dll',
    ('/resource:' + (Join-Path $PSScriptRoot '../content/lessons.json') + ',QuietType.lessons.json'), ('/out:' + $output))
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output $output
