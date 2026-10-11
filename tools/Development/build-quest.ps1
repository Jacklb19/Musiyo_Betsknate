param(
    [string]$UnityEditor,
    [string]$BuildRoot = (Join-Path $env:LOCALAPPDATA 'MusiyoQuestBuild'),
    [string]$Ref = 'HEAD'
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versionFile = Join-Path $sourceRoot 'ProjectSettings/ProjectVersion.txt'
$editorVersion = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), 'm_EditorVersion: (\S+)').Groups[1].Value
if ($editorVersion -ne '6000.3.25f1') { throw 'This delivery requires Unity 6000.3.25f1.' }
if (-not $UnityEditor) {
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw 'Unity Editor is missing. Set -UnityEditor.' }
$resolvedBuildRoot = [IO.Path]::GetFullPath($BuildRoot)
if ($resolvedBuildRoot.ToCharArray() | Where-Object { [int]$_ -gt 127 }) {
    throw 'Android requires an ASCII build path. Set -BuildRoot to an ASCII path.'
}
$commit = (& git -C $sourceRoot rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the requested Git revision.' }
$sourceChanges = & git -C $sourceRoot status --porcelain -- Assets Packages ProjectSettings
if ($LASTEXITCODE -ne 0) { throw 'Cannot check the source state.' }
if ($sourceChanges) { throw 'Commit the intended source first. The APK is built from versioned files only.' }
$buildDirectory = Join-Path $resolvedBuildRoot ('Source_' + $commit.Substring(0, 12))
$sourceMarker = Join-Path $buildDirectory 'build-source.json'
if (Test-Path -LiteralPath $buildDirectory) {
    if (-not (Test-Path -LiteralPath $sourceMarker)) { throw 'Existing directory is not a managed build copy.' }
    $recordedSource = Get-Content -LiteralPath $sourceMarker -Raw | ConvertFrom-Json
    if ($recordedSource.source_commit -ne $commit) { throw 'Build copy belongs to another revision.' }
} else {
    New-Item -ItemType Directory -Path $buildDirectory | Out-Null
    $archivePath = Join-Path $buildDirectory 'source.zip'
    & git -C $sourceRoot archive --format=zip "--output=$archivePath" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot archive the source revision.' }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $buildDirectory
    @{schema_version=1; source_commit=$commit} | ConvertTo-Json | Set-Content -LiteralPath $sourceMarker -Encoding utf8
}
$lockFile = Join-Path $buildDirectory 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockFile) {
    try {
        $lockProbe = [IO.File]::Open($lockFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $lockProbe.Dispose()
    } catch { throw 'The build copy is open in Unity. Close that Editor first.' }
}
$logPath = Join-Path $buildDirectory 'quest-build.log'
$arguments = @('-batchmode', '-quit', '-projectPath', "`"$buildDirectory`"", '-buildTarget', 'Android',
    '-executeMethod', 'MusiyoBetsknate.Editor.BuildMuseumQuest.Build', '-logFile', "`"$logPath`"")
$buildProcess = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WorkingDirectory $buildDirectory -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
if ($buildProcess.ExitCode -ne 0) { throw "Quest build failed with exit $($buildProcess.ExitCode). Read $logPath" }
$apkPath = Join-Path $buildDirectory 'Build/MuseumQuest/Musiyo-Quest2-prototype.apk'
$manifest = [ordered]@{
    schema_version = 1
    source_commit = $commit
    unity_version = $editorVersion
    apk_sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    physical_headset_verified = $false
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buildDirectory 'quest-build-manifest.json') -Encoding utf8
Write-Output "Quest prototype: $apkPath"
Write-Output 'Physical headset acceptance remains pending. Nothing was installed or published.'
