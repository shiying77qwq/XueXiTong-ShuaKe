[CmdletBinding()]
param(
    [string]$ProjectDirectory = $PSScriptRoot,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

function Get-StreamSha256([System.IO.Stream]$Stream) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Get-FileSha256([string]$Path) {
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try { return Get-StreamSha256 $stream }
    finally { $stream.Dispose() }
}

function Assert-RegularItem([System.IO.FileSystemInfo]$Item) {
    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('Reparse points are not allowed in the package source or destination: ' + $Item.FullName)
    }
}

function Get-PackageSource([string]$Root, [string]$RelativePath) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $prefix = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ('Package source escaped the selected project directory: ' + $RelativePath)
    }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.PSIsContainer) { throw ('A required package file is a directory: ' + $path) }
    Assert-RegularItem $item
    $parent = $item.Directory
    while ($null -ne $parent -and $parent.FullName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Assert-RegularItem $parent
        $parent = $parent.Parent
    }
    return $item
}

function Assert-AcceptedReport([string]$Text) {
    $required = '状态：**本次约定范围的模块检查与实机验收通过。**'
    $statusLines = @([System.Text.RegularExpressions.Regex]::Matches($Text, '(?m)^状态：[^\r\n]*\r?$'))
    if ($statusLines.Count -ne 1 -or $statusLines[0].Value.TrimEnd([char]13) -cne $required) {
        throw '拒绝打包：TEST-REPORT.md 尚未达到用户验收条件；必须且只能包含一行“状态：**本次约定范围的模块检查与实机验收通过。**”。'
    }
}

function Assert-ProofHash([string]$Text, [string]$Label, [string]$ExpectedHash) {
    $headers = @([System.Text.RegularExpressions.Regex]::Matches($Text, '(?m)^EXE SHA256:[^\r\n]*\r?$'))
    if ($headers.Count -ne 1) {
        throw ('拒绝打包：' + $Label + ' 必须恰有一行 EXE SHA256: <64hex>，实际找到 ' + $headers.Count + ' 行。')
    }
    $header = [System.Text.RegularExpressions.Regex]::Match($headers[0].Value, '^EXE SHA256: ([0-9A-Fa-f]{64})\r?$')
    if (-not $header.Success) {
        throw ('拒绝打包：' + $Label + ' 的 EXE SHA256 行格式无效，必须是64位十六进制。')
    }
    if (-not [string]::Equals($header.Groups[1].Value, $ExpectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ('拒绝打包：' + $Label + ' 的 EXE SHA256 与当前根目录 EXE 不一致。')
    }
}

function New-StudyPackage([string]$SourceDirectory) {
    $project = Get-Item -LiteralPath $SourceDirectory -Force
    if (-not $project.PSIsContainer) { throw 'ProjectDirectory must identify an existing directory.' }
    Assert-RegularItem $project
    $root = [System.IO.Path]::GetFullPath($project.FullName).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $parent = [System.IO.Directory]::GetParent($root)
    if ($null -eq $parent) { throw 'The project must have an output parent directory.' }
    Assert-RegularItem $parent

    $reportFile = Get-PackageSource $root 'TEST-REPORT.md'
    $report = [System.IO.File]::ReadAllText($reportFile.FullName, [System.Text.Encoding]::UTF8)
    Assert-AcceptedReport $report
    $executable = Get-PackageSource $root 'StudyVideoHelper.exe'
    $expectedHash = Get-FileSha256 $executable.FullName
    $proofFiles = @('TEST-REPORT.md', 'tests\real-verification.log', 'tests\validation-current.log')
    foreach ($relative in $proofFiles) {
        $proofFile = Get-PackageSource $root $relative
        $proof = [System.IO.File]::ReadAllText($proofFile.FullName, [System.Text.Encoding]::UTF8)
        Assert-ProofHash $proof $relative $expectedHash
    }

    # This is the complete allowlist. Do not enumerate the project recursively.
    $relativeFiles = @(
        'StudyVideoHelper.exe', 'README.md', 'TEST-REPORT.md', 'build.ps1', 'package.ps1', 'preview-v03.png',
        'tests\CoreTests.cs', 'tests\SessionTests.cs', 'tests\RealCourseTests.cs',
        'tests\VerificationTests.cs', 'tests\BoundedReadTests.cs', 'tests\WindowsSmoke.cs', 'tests\GuiTests.cs',
        'tests\validation-current.log', 'tests\real-verification.log', 'tests\package-selftest.log'
    )
    $csharpDirectory = Get-Item -LiteralPath (Join-Path $root 'src') -Force
    if (-not $csharpDirectory.PSIsContainer) { throw 'The src directory is missing.' }
    Assert-RegularItem $csharpDirectory
    $sourceFiles = @(Get-ChildItem -LiteralPath $csharpDirectory.FullName -Filter '*.cs' -File | Sort-Object Name)
    if ($sourceFiles.Count -eq 0) { throw 'The src directory contains no C# source files.' }
    foreach ($file in $sourceFiles) { $relativeFiles += ('src\' + $file.Name) }

    $files = @()
    foreach ($relative in $relativeFiles) {
        $item = Get-PackageSource $root $relative
        $entry = 'chaoxing-video-helper/' + $relative.Replace('\', '/')
        if ($entry.Contains('../') -or $entry.Contains('/..') -or $entry.StartsWith('/')) {
            throw ('Invalid package entry: ' + $entry)
        }
        $files += [PSCustomObject]@{ Path = $item.FullName; Entry = $entry }
    }
    $expectedEntries = @($files.Entry | Sort-Object)
    if (@($expectedEntries | Select-Object -Unique).Count -ne $files.Count) { throw 'Duplicate package entries were generated.' }

    $destination = [System.IO.Path]::GetFullPath((Join-Path $root 'chaoxing-video-helper-v0.3.zip'))
    if (-not [string]::Equals([System.IO.Path]::GetDirectoryName($destination), $root,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The ZIP destination escaped the project directory.'
    }
    if (Test-Path -LiteralPath $destination) {
        $existing = Get-Item -LiteralPath $destination -Force
        Assert-RegularItem $existing
        if ($existing.PSIsContainer) { throw 'The ZIP destination is a directory.' }
    }
    $temporary = Join-Path $root ('chaoxing-video-helper-v0.3.' + [Guid]::NewGuid().ToString('N') + '.tmp.zip')
    try {
        $output = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new($output, [System.IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                foreach ($file in $files) {
                    $entry = $archive.CreateEntry($file.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
                    $entryStream = $entry.Open()
                    try {
                        $sourceStream = [System.IO.File]::Open($file.Path, [System.IO.FileMode]::Open,
                            [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                        try { $sourceStream.CopyTo($entryStream) }
                        finally { $sourceStream.Dispose() }
                    }
                    finally { $entryStream.Dispose() }
                }
            }
            finally { $archive.Dispose() }
        }
        finally { $output.Dispose() }

        # Verify the completed temporary archive before replacing an existing deliverable.
        $inputZip = [System.IO.File]::OpenRead($temporary)
        try {
            $check = [System.IO.Compression.ZipArchive]::new($inputZip, [System.IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                $actualEntries = @($check.Entries.FullName | Sort-Object)
                if ($actualEntries.Count -ne $expectedEntries.Count -or
                    @(Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $actualEntries -CaseSensitive).Count -ne 0) {
                    throw 'The completed ZIP entries differ from the explicit allowlist.'
                }
                $packedExecutable = $check.GetEntry('chaoxing-video-helper/StudyVideoHelper.exe')
                $packedStream = $packedExecutable.Open()
                try { $packedHash = Get-StreamSha256 $packedStream }
                finally { $packedStream.Dispose() }
                if ($packedHash -ne $expectedHash -or (Get-FileSha256 $executable.FullName) -ne $expectedHash) {
                    throw 'The packaged executable SHA256 does not match the unchanged root executable.'
                }
                foreach ($relative in $proofFiles) {
                    $proofEntry = $check.GetEntry('chaoxing-video-helper/' + $relative.Replace('\', '/'))
                    $proofStream = $proofEntry.Open()
                    $reader = [System.IO.StreamReader]::new($proofStream, [System.Text.Encoding]::UTF8, $true)
                    try { $packedProof = $reader.ReadToEnd() }
                    finally { $reader.Dispose() }
                    Assert-ProofHash $packedProof ('ZIP/' + $relative) $expectedHash
                    if ($relative -eq 'TEST-REPORT.md') { Assert-AcceptedReport $packedProof }
                }
            }
            finally { $check.Dispose() }
        }
        finally { $inputZip.Dispose() }

        if (Test-Path -LiteralPath $destination) {
            $existing = Get-Item -LiteralPath $destination -Force
            Assert-RegularItem $existing
            if ($existing.PSIsContainer) { throw 'The ZIP destination changed to a directory.' }
            [System.IO.File]::Replace($temporary, $destination, [System.Management.Automation.Language.NullString]::Value)
        }
        else { [System.IO.File]::Move($temporary, $destination) }

        return [PSCustomObject]@{ Package = $destination; Entries = $files.Count; ExecutableSHA256 = $expectedHash }
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            $temporaryItem = Get-Item -LiteralPath $temporary -Force
            Assert-RegularItem $temporaryItem
            if (-not $temporaryItem.PSIsContainer -and
                [string]::Equals($temporaryItem.DirectoryName, $root, [System.StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $temporaryItem.FullName -Force
            }
        }
    }
}

function Invoke-PackageSelfTest {
    # All writes stay inside a fresh, explicitly selected temporary fixture directory.
    $fixtureBase = Join-Path ([System.IO.Path]::GetTempPath()) ('study-helper-package-tests-' + [Guid]::NewGuid().ToString('N'))
    $fixture = Join-Path $fixtureBase 'chaoxing-video-helper'
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'src') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'tests') -Force
    $testLog = Join-Path $fixtureBase 'selftest-results.log'
    $utf8 = New-Object System.Text.UTF8Encoding($true)
    $assert = { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
    $pass = { param([string]$Message) [System.IO.File]::AppendAllText($testLog, ('PASS ' + $Message + [Environment]::NewLine), $utf8); Write-Output ('PASS ' + $Message) }
    $required = @('StudyVideoHelper.exe', 'README.md', 'build.ps1', 'package.ps1', 'preview-v03.png',
        'tests\CoreTests.cs', 'tests\SessionTests.cs', 'tests\RealCourseTests.cs', 'tests\VerificationTests.cs',
        'tests\BoundedReadTests.cs', 'tests\WindowsSmoke.cs', 'tests\GuiTests.cs', 'tests\validation-current.log',
        'tests\real-verification.log', 'tests\package-selftest.log', 'src\Fixture.cs')
    foreach ($file in $required) { [System.IO.File]::WriteAllText((Join-Path $fixture $file), ('fixture ' + $file), $utf8) }
    $reportPath = Join-Path $fixture 'TEST-REPORT.md'
    $acceptedStatus = '状态：**本次约定范围的模块检查与实机验收通过。**'
    $initialExecutableHash = Get-FileSha256 (Join-Path $fixture 'StudyVideoHelper.exe')
    $writeProofs = {
        param([string]$Hash)
        [System.IO.File]::WriteAllText($reportPath, ($acceptedStatus + "`r`nEXE SHA256: " + $Hash + "`r`n"), $utf8)
        foreach ($relative in @('tests\real-verification.log', 'tests\validation-current.log')) {
            [System.IO.File]::WriteAllText((Join-Path $fixture $relative), ("EXE SHA256: " + $Hash + "`r`nfixture " + $relative + "`r`n"), $utf8)
        }
    }
    $rejectPreservingZip = {
        param([string]$ExpectedReason, [string]$Message)
        $before = Get-FileSha256 $fixtureZip
        $rejected = $false
        try { $null = New-StudyPackage $fixture }
        catch { $rejected = $_.Exception.Message.Contains($ExpectedReason) }
        & $assert ($rejected -and (Get-FileSha256 $fixtureZip) -eq $before) $Message
        & $assert (@(Get-ChildItem -LiteralPath $fixture -Filter '*.tmp.zip' -File).Count -eq 0) 'A rejected proof left a temporary ZIP behind.'
    }
    [System.IO.File]::WriteAllText($reportPath, '状态：**尚未达到用户验收条件。**', $utf8)
    $fixtureZip = Join-Path $fixture 'chaoxing-video-helper-v0.3.zip'
    try {
        $blocked = $false
        try { $null = New-StudyPackage $fixture }
        catch { $blocked = $_.Exception.Message.Contains('尚未达到用户验收条件') }
        & $assert ($blocked -and -not (Test-Path -LiteralPath $fixtureZip)) 'Unaccepted fixture was packaged.'
        & $pass 'unaccepted report refuses packaging without creating a ZIP'

        & $writeProofs $initialExecutableHash
        $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'logs') -Force
        $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'bin') -Force
        foreach ($ignored in @('logs\private.log', 'probe.txt', 'tests\WindowInspector.cs', 'tests\CoreTests.exe', 'bin\StudyVideoHelper.exe')) {
            [System.IO.File]::WriteAllText((Join-Path $fixture $ignored), 'must never be packaged', $utf8)
        }
        $result = New-StudyPackage $fixture
        & $assert ($result.Entries -eq 17 -and $result.ExecutableSHA256 -eq (Get-FileSha256 (Join-Path $fixture 'StudyVideoHelper.exe'))) 'Allowlist or executable hash verification failed.'
        $fixtureStream = [System.IO.File]::OpenRead($fixtureZip)
        try {
            $fixtureArchive = [System.IO.Compression.ZipArchive]::new($fixtureStream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                & $assert ($null -ne $fixtureArchive.GetEntry('chaoxing-video-helper/tests/CoreTests.cs') -and
                    $null -ne $fixtureArchive.GetEntry('chaoxing-video-helper/src/Fixture.cs') -and
                    $null -ne $fixtureArchive.GetEntry('chaoxing-video-helper/tests/validation-current.log') -and
                    $null -ne $fixtureArchive.GetEntry('chaoxing-video-helper/tests/real-verification.log') -and
                    $null -ne $fixtureArchive.GetEntry('chaoxing-video-helper/tests/package-selftest.log')) 'The ZIP did not preserve normal forward-slash source, test and proof-log paths.'
                foreach ($entry in $fixtureArchive.Entries) {
                    & $assert ($entry.FullName.StartsWith('chaoxing-video-helper/') -and -not $entry.FullName.Contains('\') -and
                        -not $entry.FullName.Contains('private') -and -not $entry.FullName.Contains('WindowInspector') -and
                        -not $entry.FullName.Contains('/bin/') -and -not $entry.FullName.Contains('/probe') -and
                        -not $entry.FullName.EndsWith('CoreTests.exe')) 'A forbidden file or malformed entry was packaged.'
                }
            }
            finally { $fixtureArchive.Dispose() }
        }
        finally { $fixtureStream.Dispose() }
        & $pass 'accepted fixture contains exactly the allowlist and preserves executable SHA256'

        $originalZipHash = Get-FileSha256 $fixtureZip
        [System.IO.File]::WriteAllText((Join-Path $fixture 'StudyVideoHelper.exe'), 'replacement fixture executable', $utf8)
        $currentExecutableHash = Get-FileSha256 (Join-Path $fixture 'StudyVideoHelper.exe')
        & $writeProofs $currentExecutableHash
        $replacement = New-StudyPackage $fixture
        & $assert ((Get-FileSha256 $fixtureZip) -ne $originalZipHash -and
            $replacement.ExecutableSHA256 -eq (Get-FileSha256 (Join-Path $fixture 'StudyVideoHelper.exe'))) 'Existing ZIP was not replaced with the verified new fixture.'
        & $pass 'existing same-name ZIP is replaced only after the new archive verifies'

        foreach ($unknownStatus in @('状态：**仍在验证。**', '没有验收状态行。',
            ('说明：' + $acceptedStatus), ($acceptedStatus + "`r`n状态：**仍在验证。**"))) {
            [System.IO.File]::WriteAllText($reportPath, ($unknownStatus + "`r`nEXE SHA256: " + $currentExecutableHash + "`r`n"), $utf8)
            & $rejectPreservingZip '尚未达到用户验收条件' 'An unknown, missing, unanchored or ambiguous status replaced the valid ZIP.'
        }
        & $writeProofs $currentExecutableHash
        & $pass 'unknown missing unanchored and ambiguous acceptance states preserve the previous ZIP'

        foreach ($staleProof in @('TEST-REPORT.md', 'tests\real-verification.log', 'tests\validation-current.log')) {
            $proofPath = Join-Path $fixture $staleProof
            $oldProof = [System.IO.File]::ReadAllText($proofPath, [System.Text.Encoding]::UTF8)
            [System.IO.File]::WriteAllText($proofPath, $oldProof.Replace($currentExecutableHash, $initialExecutableHash), $utf8)
            & $rejectPreservingZip $staleProof ('A stale executable hash in ' + $staleProof + ' replaced the valid ZIP.')
            & $writeProofs $currentExecutableHash
            & $pass ('stale executable hash in ' + $staleProof + ' preserves the previous ZIP')
        }

        [System.IO.File]::WriteAllText($reportPath, ($acceptedStatus + "`r`n"), $utf8)
        & $rejectPreservingZip 'TEST-REPORT.md' 'A missing report SHA256 header replaced the valid ZIP.'
        & $writeProofs $currentExecutableHash
        & $pass 'missing report executable hash is rejected without replacing the previous ZIP'

        [System.IO.File]::WriteAllText((Join-Path $fixture 'tests\real-verification.log'),
            ("EXE SHA256: " + $currentExecutableHash + "`r`nEXE SHA256: " + $currentExecutableHash + "`r`n"), $utf8)
        & $rejectPreservingZip 'tests\real-verification.log' 'Duplicate real-verification SHA256 headers replaced the valid ZIP.'
        & $writeProofs $currentExecutableHash
        & $pass 'duplicate real-verification executable hashes preserve the previous ZIP'

        [System.IO.File]::WriteAllText((Join-Path $fixture 'tests\validation-current.log'), "EXE SHA256: not-a-sha256`r`n", $utf8)
        & $rejectPreservingZip 'tests\validation-current.log' 'A malformed validation SHA256 header replaced the valid ZIP.'
        & $writeProofs $currentExecutableHash
        & $pass 'malformed validation executable hash preserves the previous ZIP'

        $preservedZipHash = Get-FileSha256 $fixtureZip
        Remove-Item -LiteralPath (Join-Path $fixture 'tests\GuiTests.cs') -Force
        $missingRejected = $false
        try { $null = New-StudyPackage $fixture } catch { $missingRejected = $true }
        & $assert ($missingRejected -and (Get-FileSha256 $fixtureZip) -eq $preservedZipHash) 'Missing source changed the previous valid ZIP.'
        & $assert (@(Get-ChildItem -LiteralPath $fixture -Filter '*.tmp.zip' -File).Count -eq 0) 'A temporary ZIP was left behind.'
        & $pass 'missing required source preserves the previous ZIP and leaves no temporary archive'

        $escaped = $false
        try { $null = Get-PackageSource $fixture '..\outside.txt' }
        catch { $escaped = $_.Exception.Message.Contains('escaped the selected project directory') }
        & $assert $escaped 'The selected project boundary accepted a parent-directory source.'
        & $pass 'source path validation rejects traversal outside the selected project directory'

        Write-Output ('PACKAGE SELFTEST RESULT 12 passed; fixture and log: ' + $fixtureBase)
    }
    catch {
        [System.IO.File]::AppendAllText($testLog, ('FAIL ' + $_.Exception.Message + [Environment]::NewLine), $utf8)
        throw
    }
}

if ($SelfTest) { Invoke-PackageSelfTest; return }
New-StudyPackage $ProjectDirectory
