param(
    [int]$Iterations = 3,
    [string]$OutJson = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$workDir = Join-Path $repoRoot '.local\benchmark'
if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }
New-Item -ItemType Directory -Path $workDir | Out-Null

# Build compiler once in Release mode
& dotnet build (Join-Path $repoRoot 'src\Rukbat\Rukbat.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
$rukbatExe = Join-Path $repoRoot 'src\Rukbat\bin\Release\net8.0\rukbat.exe'

# Create sample 500-row CSV for csv-filter
$csvInput = Join-Path $workDir 'people.csv'
$csvOutput = Join-Path $workDir 'people_out.csv'
$csvLines = New-Object 'System.Collections.Generic.List[string]'
$csvLines.Add('name,enabled,note')
for ($i = 1; $i -le 500; $i++) {
    $enabled = if ($i % 2 -eq 0) { 'true' } else { 'false' }
    $csvLines.Add("User$i,$enabled,initial")
}
[System.IO.File]::WriteAllLines($csvInput, $csvLines, [System.Text.UTF8Encoding]::new($false))

# Create focused synthetic benchmarks
$cloneLoopSource = Join-Path $workDir 'bench_clone_loop.rukbat'
@"
struct Item {
    id: int
    name: string
    tags: string[]
}

fn read_name(item: Item): string {
    return item.name
}

fn make_item(id: int): Item {
    return Item {
        id: id,
        name: "item",
        tags: ["a", "b", "c"]
    }
}

const config := Item {
    id: 1,
    name: "cfg",
    tags: ["prod", "win", "ps51"]
}

const items := [
    Item { id: 1, name: "one", tags: ["x", "y"] },
    Item { id: 2, name: "two", tags: ["y", "z"] },
    Item { id: 3, name: "three", tags: ["z", "w"] }
]

total := 0
i := 0
while i < 400 {
    for it in items {
        n := read_name(it)
        if n == config.name {
            total = total + 1
        }
        total = total + it.tags.length
    }
    fresh := make_item(i)
    total = total + fresh.id
    i = i + 1
}
print(total)
"@ | Set-Content -Path $cloneLoopSource -Encoding UTF8

$textLightSource = Join-Path $workDir 'bench_text_light.rukbat'
@"
import text
import encoding
import json

struct Payload {
    name: string
    count: int
}

p := json.parse<Payload>('{"name":"rukbat","count":42}')
i := 0
hits := 0
while i < 200 {
    s := p.name + "-" + text.from_int(p.count + i)
    if text.contains(s, "rukbat") && text.starts_with(s, "rukbat-") {
        hits = hits + 1
    }
    i = i + 1
}
print(hits)
"@ | Set-Content -Path $textLightSource -Encoding UTF8

$cases = @(
    @{ Name = 'http-json (cold)'; Source = (Join-Path $repoRoot 'examples\http-json.rukbat'); Args = @(); ExpectExit = 1 },
    @{ Name = 'csv-filter (500 rows)'; Source = (Join-Path $repoRoot 'examples\csv-filter\main.rukbat'); Args = @($csvInput, $csvOutput, 'utf-8'); ExpectExit = 0 },
    @{ Name = 'text-fields'; Source = (Join-Path $repoRoot 'examples\text-fields.rukbat'); Args = @(); ExpectExit = 0 },
    @{ Name = 'value-semantics'; Source = (Join-Path $repoRoot 'examples\value-semantics.rukbat'); Args = @(); ExpectExit = 0 },
    @{ Name = 'bench_clone_loop'; Source = $cloneLoopSource; Args = @(); ExpectExit = 0 },
    @{ Name = 'bench_text_light'; Source = $textLightSource; Args = @(); ExpectExit = 0 }
)

function Measure-MedianMs {
    param(
        [string]$Shell,
        [string]$ScriptPath,
        [string[]]$ScriptArgs,
        [int]$Count,
        [int]$ExpectExit
    )
    $times = New-Object 'System.Collections.Generic.List[double]'
    for ($n = 0; $n -lt $Count; $n++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $Shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ScriptPath @ScriptArgs *> $null
        $sw.Stop()
        if ($LASTEXITCODE -ne $ExpectExit) {
            throw "Benchmark '$ScriptPath' on '$Shell' exited with $LASTEXITCODE (expected $ExpectExit)."
        }
        $times.Add($sw.Elapsed.TotalMilliseconds)
    }
    $sorted = $times | Sort-Object
    return [Math]::Round($sorted[[int]($sorted.Count / 2)], 1)
}

$results = @()
foreach ($case in $cases) {
    $safeName = ($case.Name -replace '[^a-zA-Z0-9_-]', '_')
    $copiedSource = Join-Path $workDir ($safeName + '.rukbat')
    if ($case.Source -ne $copiedSource) {
        Copy-Item -Path $case.Source -Destination $copiedSource -Force
    }
    $outPs1 = Join-Path $workDir ($safeName + '.ps1')
    & $rukbatExe build $copiedSource | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rukbat build failed for $($case.Source)" }

    $content = [System.IO.File]::ReadAllText($outPs1)
    $bytes = (Get-Item $outPs1).Length
    $lines = ($content -split "`n").Count
    $cloneCount = ([regex]::Matches($content, '\$script:__rukbat_clone')).Count

    $ps51Ms = Measure-MedianMs -Shell 'powershell' -ScriptPath $outPs1 -ScriptArgs $case.Args -Count $Iterations -ExpectExit $case.ExpectExit
    $pwshMs = Measure-MedianMs -Shell 'pwsh' -ScriptPath $outPs1 -ScriptArgs $case.Args -Count $Iterations -ExpectExit $case.ExpectExit

    $results += [pscustomobject]@{
        Case       = $case.Name
        Lines      = $lines
        Bytes      = $bytes
        CloneRefs  = $cloneCount
        PS51_ms    = $ps51Ms
        PWSH7_ms   = $pwshMs
    }
}

$results | Format-Table -AutoSize
if ($OutJson -ne '') {
    $results | ConvertTo-Json -Depth 4 | Set-Content -Path $OutJson -Encoding UTF8
}
