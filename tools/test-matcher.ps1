param(
    [string]$CorpusPath = (Join-Path $PSScriptRoot "..\validation\errors\vrchat-error-corpus.json"),
    [string]$FixturesPath = (Join-Path $PSScriptRoot "..\validation\errors\matcher-fixtures.json")
)

$ErrorActionPreference = "Stop"

function Normalize-Line {
    param([string]$Line)
    $value = $Line -replace "\(\d+,\d+\)", "(line,column)"
    $value = $value -replace "\bline \d+\b", "line n"
    return ($value -replace "\s+", " ").Trim()
}

function Test-ErrorLine {
    param([string]$Line)
    return $Line -match "(?i)^\[Error\]|^\[Assert\]|^\[Fatal\]|\berror\s+CS\d{4}\b|\bException\b|UnityException|AssetBundle was not built|Building AssetBundle failed|Upload failed|Build failed|\bfails?\b|\bfailure\b"
}

function Test-WarningLine {
    param([string]$Line)
    return $Line -match "(?i)^\[Warning\]|\bwarning\s+CS\d{4}\b|obsolete|deprecated"
}

function Test-EntryStart {
    param([string]$Line)
    return $Line -match "(?i)^\[(Error|Warning|Log|Assert|Fatal)\]"
}

function Get-ConsoleEntries {
    param([string[]]$Lines)
    $entries = @()
    $builder = New-Object System.Text.StringBuilder
    $firstLine = $null
    $kind = "log"

    foreach ($line in $Lines) {
        if (Test-EntryStart -Line $line) {
            if ($null -ne $firstLine -and $builder.Length -gt 0) {
                $entries += [pscustomobject]@{ FirstLine = $firstLine; Text = $builder.ToString(); Kind = $kind }
            }

            [void]$builder.Clear()
            $firstLine = $line
            $kind = if (Test-ErrorLine -Line $line) { "error" } elseif (Test-WarningLine -Line $line) { "warning" } else { "log" }
        }
        elseif ($null -eq $firstLine) {
            $firstLine = $line
            $kind = if (Test-ErrorLine -Line $line) { "error" } elseif (Test-WarningLine -Line $line) { "warning" } else { "log" }
        }

        if ($builder.Length -gt 0) {
            [void]$builder.AppendLine()
        }

        [void]$builder.Append($line)
    }

    if ($null -ne $firstLine -and $builder.Length -gt 0) {
        $entries += [pscustomobject]@{ FirstLine = $firstLine; Text = $builder.ToString(); Kind = $kind }
    }

    return @($entries)
}

function Get-Suspects {
    param([string[]]$Texts)
    $suspects = @()
    foreach ($text in $Texts) {
        if ($null -eq $text) { $text = "" }
        foreach ($match in [regex]::Matches($text, "(?im)^Context: (.+)$")) {
            $value = $match.Groups[1].Value.Trim()
            if ($value -and $suspects -notcontains $value) { $suspects += $value }
        }

        foreach ($match in [regex]::Matches($text, "Assets[\\/][^\r\n:]+?\.(asset|prefab|unity|cs|shader|cginc)", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $value = $match.Value.Trim()
            if ($value -and $suspects -notcontains $value) { $suspects += $value }
        }
    }

    return @($suspects | Select-Object -First 4)
}

$stopWords = @(
    "the", "and", "that", "this", "with", "from", "into", "your", "you", "are", "was", "were",
    "but", "not", "can", "cannot", "after", "before", "then", "than", "only", "even", "still",
    "when", "where", "while", "because", "unity", "vrchat", "build", "error", "failed", "fails"
)

function Get-Tokens {
    param([string]$Value)
    if ($null -eq $Value) { $Value = "" }
    $tokens = [regex]::Matches($Value.ToLowerInvariant(), "[a-z0-9_./#-]{3,}") | ForEach-Object { $_.Value }
    return @($tokens | Where-Object { $stopWords -notcontains $_ } | Select-Object -Unique)
}

function Get-SignalScore {
    param([string]$Signal, [string]$TextLower)
    if ($null -eq $Signal) { $Signal = "" }
    $signalLower = ($Signal.ToLowerInvariant()).Trim()
    if (-not $signalLower) { return 0 }
    if ($TextLower.Contains($signalLower)) { return 100 }

    $tokens = @(Get-Tokens $signalLower)
    if ($tokens.Count -eq 0 -or $tokens.Count -le 2) { return 0 }

    $hits = @($tokens | Where-Object { $TextLower.Contains($_) }).Count
    $ratio = $hits / [float]$tokens.Count
    if ($ratio -ge 0.85 -and $hits -ge 3) { return 55 + [math]::Round($ratio * 25) }
    if ($ratio -ge 0.65 -and $hits -ge 4) { return 30 + [math]::Round($ratio * 20) }
    return 0
}

function Get-LineSignalScore {
    param([string]$Line, [string]$Signal)
    if ($null -eq $Line) { $Line = "" }
    if ($null -eq $Signal) { $Signal = "" }
    $lineLower = $Line.ToLowerInvariant()
    $signalLower = ($Signal.ToLowerInvariant()).Trim()
    if (-not $signalLower) { return 0 }
    if ($lineLower.Contains($signalLower) -or $signalLower.Contains($lineLower)) { return 100 }

    $tokens = @(Get-Tokens $signalLower)
    if ($tokens.Count -eq 0) { return 0 }

    $hits = @($tokens | Where-Object { $lineLower.Contains($_) }).Count
    $ratio = $hits / [float]$tokens.Count
    if ($ratio -ge 0.65 -and $hits -ge 3) { return [math]::Round($ratio * 80) }
    return 0
}

function Test-RegexSignal {
    param([string]$Pattern, [string]$Value)
    if ([string]::IsNullOrEmpty($Pattern) -or [string]::IsNullOrEmpty($Value)) { return $false }
    try {
        return $Value -match "(?i)$Pattern"
    }
    catch {
        return $false
    }
}

function Get-Lane {
    param($Case)
    if ($Case.caseType -eq "upload-support") {
        return "support"
    }
    if ($Case.severity -eq "blocker" -and $Case.caseType -eq "console-error") {
        return "fix"
    }
    if ($Case.caseType -eq "warning") { return "warning" }
    if (@("workflow", "knowledge", "ux") -contains $Case.caseType) { return "related" }
    if ($Case.severity -eq "info") { return "later" }
    if (@("optimization", "visual-polish", "creator-workflow") -contains $Case.category) { return "later" }
    return "then"
}

function Get-Priority {
    param($Case, [int]$Score)
    $severityWeight = if ($Case.severity -eq "blocker") { 100 } elseif ($Case.severity -eq "warning") { 60 } else { 30 }
    $caseTypeWeight = switch ($Case.caseType) {
        "console-error" { 40; break }
        "upload-support" { 40; break }
        "runtime-behavior" { 5; break }
        "warning" { -15; break }
        "workflow" { -45; break }
        "knowledge" { -35; break }
        "ux" { -35; break }
        default { 0 }
    }
    $categoryWeight = switch ($Case.category) {
        "upload-readiness" { 18; break }
        "build-export" { 16; break }
        "udon-compile" { 14; break }
        "udon-import" { 12; break }
        "network-sync" { 8; break }
        "interaction-wiring" { 6; break }
        "optimization" { -8; break }
        "creator-workflow" { -10; break }
        "visual-polish" { -14; break }
        "vrchat-knowledge" { -6; break }
        default { 0 }
    }
    return $severityWeight + $caseTypeWeight + $categoryWeight + $Score + [int]$Case.priorityBoost
}

function Test-Strictness {
    param($Case, [int]$TotalScore, [bool]$HasExactSignal)
    $strictness = if ($Case.matchStrictness) { $Case.matchStrictness } else { "strong" }
    if ($strictness -eq "exact") { return $HasExactSignal }
    if ($strictness -eq "related") { return $TotalScore -ge 80 }
    return $TotalScore -ge 65
}

function Invoke-Match {
    param($CorpusCases, [string]$LogText)
    $lines = @($LogText -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $entries = @(Get-ConsoleEntries -Lines $lines)
    $errorEntries = @($entries | Where-Object { $_.Kind -eq "error" })
    $warningEntries = @($entries | Where-Object { $_.Kind -eq "warning" })

    if ($lines.Count -gt 0 -and $errorEntries.Count -eq 0) {
        return [pscustomobject]@{
            ErrorCount = 0
            WarningCount = $warningEntries.Count
            RootCauseCount = 0
            Findings = @()
        }
    }

    $textLower = (($errorEntries | ForEach-Object { $_.Text }) -join "`n").ToLowerInvariant()
    $errorGroups = @($errorEntries | Group-Object { Normalize-Line $_.FirstLine } | ForEach-Object {
        [pscustomobject]@{ Line = $_.Group[0].FirstLine; Count = $_.Count; Suspects = @(Get-Suspects -Texts @($_.Group | ForEach-Object { $_.Text })) }
    })

    $findings = foreach ($case in $CorpusCases) {
        if (-not $case.rawSignals -or $case.rawSignals.Count -eq 0) { continue }

        $matchedCount = 0
        $bestScore = 0
        $hasExact = $false
        foreach ($signal in $case.rawSignals) {
            $score = Get-SignalScore -Signal $signal -TextLower $textLower
            if ($score -le 0) { continue }
            $matchedCount++
            if ($score -gt $bestScore) { $bestScore = $score }
            if ($score -ge 100) { $hasExact = $true }
        }

        if ($case.regexSignals) {
            foreach ($signal in $case.regexSignals) {
                if (-not (Test-RegexSignal -Pattern $signal -Value $textLower)) { continue }
                $matchedCount++
                $bestScore = [math]::Max($bestScore, 100)
                $hasExact = $true
            }
        }

        if ($matchedCount -eq 0) { continue }

        $totalScore = [math]::Min(100, $bestScore + ([math]::Max(0, $matchedCount - 1) * 12))
        if (-not (Test-Strictness $case $totalScore $hasExact)) { continue }

        $evidenceCount = 0
        $suspectCount = 0
        foreach ($group in $errorGroups) {
            $lineBest = 0
            foreach ($signal in $case.rawSignals) {
                $lineBest = [math]::Max($lineBest, (Get-LineSignalScore -Line $group.Line -Signal $signal))
            }
            if ($case.regexSignals) {
                foreach ($signal in $case.regexSignals) {
                    if (Test-RegexSignal -Pattern $signal -Value $group.Line) {
                        $lineBest = [math]::Max($lineBest, 100)
                    }
                }
            }
            if ($lineBest -gt 0) {
                $evidenceCount++
                $suspectCount += @($group.Suspects).Count
            }
        }

        [pscustomobject]@{
            Id = $case.id
            CaseType = $case.caseType
            Lane = Get-Lane $case
            Priority = Get-Priority $case $totalScore
            Score = $totalScore
            EvidenceCount = $evidenceCount
            SuspectCount = $suspectCount
        }
    }

    $ordered = @($findings | Sort-Object Priority -Descending | Select-Object -First 8)
    return [pscustomobject]@{
        ErrorCount = $errorEntries.Count
        WarningCount = $warningEntries.Count
        RootCauseCount = $ordered.Count
        Findings = $ordered
    }
}

$corpus = Get-Content -LiteralPath $CorpusPath -Raw | ConvertFrom-Json
$fixtures = Get-Content -LiteralPath $FixturesPath -Raw | ConvertFrom-Json

$failures = New-Object System.Collections.Generic.List[string]

if (-not $corpus.cases -or $corpus.cases.Count -eq 0) {
    throw "Corpus contains no cases: $CorpusPath"
}

$ids = @($corpus.cases | ForEach-Object { $_.id })
$uniqueIds = @($ids | Sort-Object -Unique)
if ($ids.Count -ne $uniqueIds.Count) {
    $failures.Add("Corpus ids are not unique.")
}

foreach ($case in $corpus.cases) {
    if (-not $case.caseType) { $failures.Add("$($case.id) missing caseType") }
    if (-not $case.matchStrictness) { $failures.Add("$($case.id) missing matchStrictness") }
    if (-not $case.rawSignals -or $case.rawSignals.Count -eq 0) { $failures.Add("$($case.id) has no rawSignals") }
}

foreach ($fixture in $fixtures.fixtures) {
    $result = Invoke-Match -CorpusCases $corpus.cases -LogText $fixture.input
    $expected = $fixture.expected
    $top = @($result.Findings | Select-Object -First 1)

    if ($null -ne $expected.errorCount -and $result.ErrorCount -ne [int]$expected.errorCount) {
        $failures.Add("$($fixture.id): expected errorCount $($expected.errorCount), got $($result.ErrorCount)")
    }

    if ($null -ne $expected.rootCauseCount -and $result.RootCauseCount -ne [int]$expected.rootCauseCount) {
        $failures.Add("$($fixture.id): expected rootCauseCount $($expected.rootCauseCount), got $($result.RootCauseCount)")
    }

    if ($expected.PSObject.Properties.Name -contains "topFindingId") {
        $actualTop = if ($top.Count -gt 0) { $top[0].Id } else { $null }
        if ($actualTop -ne $expected.topFindingId) {
            $failures.Add("$($fixture.id): expected topFindingId '$($expected.topFindingId)', got '$actualTop'")
        }
    }

    if ($expected.caseType -and $top.Count -gt 0 -and $top[0].CaseType -ne $expected.caseType) {
        $failures.Add("$($fixture.id): expected caseType $($expected.caseType), got $($top[0].CaseType)")
    }

    if ($expected.lane -and $top.Count -gt 0 -and $top[0].Lane -ne $expected.lane) {
        $failures.Add("$($fixture.id): expected lane $($expected.lane), got $($top[0].Lane)")
    }

    if ($expected.minEvidenceSuspects -and $top.Count -gt 0 -and $top[0].SuspectCount -lt [int]$expected.minEvidenceSuspects) {
        $failures.Add("$($fixture.id): expected at least $($expected.minEvidenceSuspects) evidence suspect(s), got $($top[0].SuspectCount)")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Matcher regression failures:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Matcher fixtures passed: $($fixtures.fixtures.Count)"
Write-Host "Corpus cases: $($corpus.cases.Count)"
