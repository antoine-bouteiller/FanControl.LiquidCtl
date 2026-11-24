# Publishes a GitHub release from conventional commits: main gets stable versions, develop gets -beta.N.
param([switch]$DryRun)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
# Windows runners decode native output with a legacy code page, which mangles commit messages.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$branch = if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { git branch --show-current }
$prerelease = switch ($branch) { "main" { $false } "develop" { $true } }
if ($null -eq $prerelease) {
    Write-Host "Releases are only published from main and develop, not '$branch'."
    exit 0
}

$releases = @(git tag --merged HEAD --list "v*" |
    ForEach-Object {
        if ($_ -match '^v(\d+\.\d+\.\d+)(?:-beta\.(\d+))?$') {
            [pscustomobject]@{ Tag = $_; Core = [version]$Matches[1]; Beta = $Matches[2] ? [int]$Matches[2] : $null }
        }
    } |
    Where-Object { $prerelease -or $null -eq $_.Beta } |
    Sort-Object Core, { $_.Beta ?? [int]::MaxValue } -Descending)
$last = $releases[0]
$stable = ($releases | Where-Object { $null -eq $_.Beta } | Select-Object -First 1).Core

$log = (git log --format="%H%x1f%B%x1e" "$($last.Tag)..HEAD") -join "`n"
$commits = @($log -split "`u{1e}" | Where-Object { $_.Trim() } | ForEach-Object {
    $hash, $message = $_.Trim() -split "`u{1f}", 2
    $header = ($message -split "`n", 2)[0]
    $reverts = $message -match '(?m)^This reverts commit (?<sha>[0-9a-f]{40})' ? $Matches.sha : $null
    if ($header -notmatch '^(?<type>\w+)(?:\((?<scope>[^)]*)\))?(?<bang>!)?: (?<subject>.+)$') {
        if ($reverts) { [pscustomobject]@{ Hash = $hash; Type = "revert"; Reverts = $reverts } }
        return
    }
    $commit = [pscustomobject]@{
        Hash = $hash; Type = $Matches.type; Scope = $Matches.scope; Subject = $Matches.subject
        Breaking = $null; Reverts = $reverts
    }
    if ($message -match '(?ms)^BREAKING[ -]CHANGE: (.+)') { $commit.Breaking = $Matches[1].Trim() }
    elseif ($header -match '^\w+(\([^)]*\))?!') { $commit.Breaking = $commit.Subject }
    $commit
})

# A commit reverted before release never shipped, so it and its revert are dropped.
$inRange = $commits.Hash
$cancelled = $commits | Where-Object { $_.Reverts -in $inRange } | ForEach-Object { $_.Hash; $_.Reverts }
$commits = @($commits | Where-Object Hash -notin $cancelled)

$bump = ($commits | ForEach-Object {
    if ($_.Breaking) { 3 }
    elseif ($_.Type -eq "feat") { 2 }
    elseif ($_.Type -in "fix", "perf", "revert" -or ($_.Type -eq "build" -and $_.Scope -eq "deps")) { 1 }
    else { 0 }
} | Measure-Object -Maximum).Maximum
if (-not $bump) {
    Write-Host "No release-worthy commits since $($last.Tag)."
    exit 0
}

$next = switch ($bump) {
    3 { [version]::new($stable.Major + 1, 0, 0) }
    2 { [version]::new($stable.Major, $stable.Minor + 1, 0) }
    1 { [version]::new($stable.Major, $stable.Minor, $stable.Build + 1) }
}
$version = if (-not $prerelease) { "$next" }
    elseif ($null -ne $last.Beta -and $last.Core -ge $next) { "$($last.Core)-beta.$($last.Beta + 1)" }
    else { "$next-beta.1" }
$tag = "v$version"

$repoUrl = gh repo view --json url -q .url
function Format-Entry($group, $text) {
    $scope = $group[0].Scope ? "**$($group[0].Scope):** " : ""
    $links = ($group | ForEach-Object { "[$($_.Hash.Substring(0, 7))]($repoUrl/commit/$($_.Hash))" }) -join ", "
    "* $scope$text ($links)"
}

# Renovate commit subject, e.g. "update dependency msgspec ( 0.21.1 ➜ 0.22.0 )".
$renovateBump = '(?i)^(?<dep>update .+?) \( (?<from>\S+) ➜ \S+ \)'

$sections = [ordered]@{
    "feat" = "🎉 New Features"
    "fix" = "🐛 Bug Fixes"
    "build(deps)" = "📦 Dependency Updates"
    "docs" = "📚 Documentation"
}
$notes = @("## [$version]($repoUrl/compare/$($last.Tag)...$tag) ($((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')))")
$breaking = @($commits | Where-Object Breaking)
if ($breaking) {
    $notes += "", "### ⚠ BREAKING CHANGES", ""
    $notes += $breaking | ForEach-Object { Format-Entry @($_) $_.Breaking }
}
foreach ($key in $sections.Keys) {
    $entries = @($commits |
        Where-Object { $_.Subject -and ($_.Type -eq $key -or "$($_.Type)($($_.Scope))" -eq $key) } |
        Group-Object { $_.Subject -match $renovateBump ? "$($_.Scope) $($Matches.dep)" : $_.Hash } |
        ForEach-Object {
            # git log lists newest first, so the range spans from the oldest bump to the newest.
            $from = [regex]::Match($_.Group[-1].Subject, $renovateBump).Groups["from"].Value
            [pscustomobject]@{
                Scope = $_.Group[0].Scope
                Subject = $from ? ($_.Group[0].Subject -replace '\( \S+ ➜', "( $from ➜") : $_.Group[0].Subject
                Commits = $_.Group
            }
        } |
        Sort-Object Scope, Subject)
    if ($entries) {
        $notes += "", "### $($sections[$key])", ""
        $notes += $entries | ForEach-Object { Format-Entry $_.Commits $_.Subject }
    }
}
$notes = $notes -join "`n"

if ($DryRun) {
    Write-Host "Would publish $tag$($prerelease ? ' (prerelease)' : '')`n"
    Write-Host $notes
    exit 0
}

& "$PSScriptRoot/release.ps1"

# Checked after the slow build so a push during it hands the release to the newer run.
$remoteHead = (-split (git ls-remote origin "refs/heads/$branch"))[0]
if ($remoteHead -ne (git rev-parse HEAD)) {
    Write-Host "Skipping release: HEAD is no longer the tip of origin/$branch."
    exit 0
}

$notesFile = New-TemporaryFile
Set-Content -Path $notesFile -Value $notes
gh release create $tag FanControl.Liquidctl.zip --target (git rev-parse HEAD) --title $tag --notes-file $notesFile @($prerelease ? "--prerelease" : @())
