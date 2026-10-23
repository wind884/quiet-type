$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
$directory = Join-Path $PSScriptRoot '../.build/windows-tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$common = @('/nologo','/target:exe','/langversion:5','/codepage:65001','/define:INPUT_TESTS;STORAGE_TESTS','/warn:4','/warnaserror+',
    '/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:Accessibility.dll','/reference:System.Runtime.Serialization.dll',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/win32icon:' + (Join-Path $PSScriptRoot 'app.ico')),
    ('/resource:' + (Join-Path $PSScriptRoot '../content/lessons.json') + ',QuietType.lessons.json'))
$groups = @(
    @{ Name='CoreTests'; Sources=@('Contracts.cs','TypingCore.cs') },
    @{ Name='InputTests'; Sources=@('Contracts.cs','TypingCore.cs','InputSink.cs','PracticeControl.cs') },
    @{ Name='StorageTests'; Sources=@('Contracts.cs','TypingCore.cs','UserStore.cs','Ui.cs','LibraryDialog.cs','HistoryDialog.cs') },
    @{ Name='IndexTests'; Sources=@('Contracts.cs','TypingCore.cs','Ui.cs','ContentIndex.cs') },
    @{ Name='FullCatalogTests'; Sources=@('Contracts.cs','TypingCore.cs','Ui.cs','ContentIndex.cs') },
    @{ Name='DialogLocalizationTests'; Sources=@('Contracts.cs','TypingCore.cs','Ui.cs','UserStore.cs','LibraryDialog.cs','HistoryDialog.cs') },
    @{ Name='ShellTests'; Sources=@('Contracts.cs','TypingCore.cs','InputSink.cs','PracticeControl.cs','Ui.cs','ContentIndex.cs','UserStore.cs','LibraryDialog.cs','HistoryDialog.cs','MainForm.cs') }
)
foreach ($group in $groups) {
    $executable = Join-Path $directory ($group.Name + '.exe')
    $sources = @($group.Sources | ForEach-Object { Join-Path $PSScriptRoot $_ }) + (Join-Path $PSScriptRoot ('tests/' + $group.Name + '.cs'))
    & $compiler @common ('/out:' + $executable) @sources
    if ($LASTEXITCODE -ne 0) { throw ('Compile failed: ' + $group.Name) }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw ('Tests failed: ' + $group.Name) }
}
