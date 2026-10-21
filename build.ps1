param([switch]$Test, [switch]$WindowsSmoke)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$references = @(
    '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    ('/r:' + (Join-Path $wpf 'UIAutomationClient.dll')),
    ('/r:' + (Join-Path $wpf 'UIAutomationTypes.dll')),
    ('/r:' + (Join-Path $wpf 'WindowsBase.dll'))
)
$sources = @(
    (Join-Path $PSScriptRoot 'src\StudyCore.cs'),
    (Join-Path $PSScriptRoot 'src\VideoSession.cs'),
    (Join-Path $PSScriptRoot 'src\WindowsApp.cs'),
    (Join-Path $PSScriptRoot 'src\TransitionVerification.cs'),
    (Join-Path $PSScriptRoot 'src\BoundedRead.cs'),
    (Join-Path $PSScriptRoot 'src\NativeAccessibility.cs')
)
$binary = Join-Path $PSScriptRoot 'StudyVideoHelper.exe'
& $compiler /nologo /codepage:65001 /optimize+ /target:winexe ('/out:' + $binary) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output ('Built: ' + $binary)
if ($Test) {
    $tests = Join-Path $PSScriptRoot 'tests\CoreTests.exe'
    & $compiler /nologo /codepage:65001 /target:exe ('/out:' + $tests) /r:System.Core.dll `
        $sources[0] $sources[1] $sources[3] $sources[4] (Join-Path $PSScriptRoot 'tests\CoreTests.cs') `
        (Join-Path $PSScriptRoot 'tests\SessionTests.cs') (Join-Path $PSScriptRoot 'tests\RealCourseTests.cs') `
        (Join-Path $PSScriptRoot 'tests\VerificationTests.cs') (Join-Path $PSScriptRoot 'tests\BoundedReadTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $tests
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($WindowsSmoke) {
    $smoke = Join-Path $PSScriptRoot 'tests\WindowsSmoke.exe'
    & $compiler /nologo /codepage:65001 /target:exe /main:StudyVideoHelper.WindowsSmoke ('/out:' + $smoke) @references @sources `
        (Join-Path $PSScriptRoot 'tests\WindowsSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Windows smoke compilation failed.' }
    & $smoke
    if ($LASTEXITCODE -ne 0) { throw 'Windows smoke failed.' }
    $guiTests = Join-Path $PSScriptRoot 'tests\GuiTests.exe'
    & $compiler /nologo /codepage:65001 /target:exe /main:StudyVideoHelper.GuiTests ('/out:' + $guiTests) @references @sources `
        (Join-Path $PSScriptRoot 'tests\GuiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'GUI test compilation failed.' }
    & $guiTests
    if ($LASTEXITCODE -ne 0) { throw 'GUI tests failed.' }
}
