# 창세기전3 리메이크 안드로이드판 — PC 에서 폰에 넣을 자료 묶음(zip 하나)을 만드는 스크립트
#
# 쓰는 법: 저장소 맨 위 폴더의 「안드로이드용-자료-만들기.bat」 을 두 번 누른다(이 파일을 직접 고칠 필요 없다).
#   폴더 고르기 창이 뜬다 → 창세기전3 파트1 · 파트2 원본 폴더가 들어 있는 폴더를 고른다.
#   (폴더 이름은 상관없다 — 안의 파일로 파트1 · 파트2 를 알아본다. 파트1 이나 파트2 폴더 하나를 골라도 옆 폴더까지 찾는다.)
#   바탕화면에 「창세기전3-안드로이드-자료.zip」 이 생긴다.
#
# zip 안의 모양(폰에서 이 셋을 한 폴더에 풀고, 앱의 「폴더 고르기」로 그 폴더를 고른다):
#   part1/              파트1 원본 폴더 전체(실행 파일 · dll 은 뺀다 — 게임 코드는 리메이크가 대신한다)
#   part2/              파트2 원본 폴더 전체(같음)
#   WarOfGenesis-data/  게임 자료 폴더 — 원작자 GitHub 릴리즈의 추가 자료(pack.json tier 90 이상: 파트1 그림 · 시디 곡,
#                       기술 컷신)를 assets/ 아래에. 세이브 · 설정도 앱이 여기에 쓴다.
#   사용법.txt
#
# 원본 자료는 저작권이 있다. 만든 zip 은 원본을 가진 본인 기기용이다 — 남에게 나눠 주거나 인터넷에 올리지 않는다.
#
# 사람 손 없이 돌릴 때: -Source <폴더> [-Out <zip 파일>] [-SkipExtras] [-ExtrasLimit N(시험용)]

param(
    [string]$Source = "",
    [string]$Out = "",
    [switch]$SkipExtras,
    [int]$ExtrasLimit = 0
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

# 원본 폴더에서 빼는 것 — 게임 코드는 리메이크가 대신하므로 윈도우 실행 파일은 필요 없다.
$SkipExt = @('.exe', '.dll', '.ocx', '.sys', '.msi', '.com', '.scr')
$SkipName = @('thumbs.db', 'desktop.ini')

$PackUrl = 'https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/'
$OnDemandTier = 90   # duel-dx/System/AssetDownload.cs 의 OnDemandTier 와 같다
$DataName = 'WarOfGenesis-data'

function Say([string]$text) { Write-Host $text }

# 원본 게임 폴더 판별 — 리메이크 코드와 같은 조건
function Test-Part1([string]$root) {   # WarOfGenesis.Assets/Part1Game.cs IsValid
    $root -and (Test-Path -LiteralPath (Join-Path $root 'Obss\Obss.idx') -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $root 'Chars') -PathType Container)
}
function Test-Part2([string]$root) {   # WarOfGenesis.Assets/OriginalGame.cs IsValid
    $root -and (Test-Path -LiteralPath (Join-Path $root 'TXR\Txr.dat') -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $root 'Chr') -PathType Container) -and (Test-Path -LiteralPath (Join-Path $root 'Obs') -PathType Container)
}

function Pick-Folder([string]$title) {
    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = $title
    $dlg.ShowNewFolderButton = $false
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return "" }
    return $dlg.SelectedPath
}

# 그 폴더와 그 아래(3단까지)에서 원본 폴더를 찾는다 — 원본 폴더 안으로는 더 들어가지 않는다.
function Find-Game([string]$top, [scriptblock]$test) {
    $queue = New-Object System.Collections.Generic.Queue[object]
    $queue.Enqueue(@($top, 0))
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue(); $dir = $item[0]; $depth = $item[1]
        if (& $test $dir) { return $dir }
        if ($depth -ge 3 -or (Test-Part1 $dir) -or (Test-Part2 $dir)) { continue }
        try { $subs = Get-ChildItem -LiteralPath $dir -Directory -Force -ErrorAction Stop | Sort-Object Name } catch { continue }
        foreach ($s in $subs) { $queue.Enqueue(@($s.FullName, ($depth + 1))) }
    }
    return $null
}

# 고른 폴더에서 파트1 · 파트2 를 찾는다. 파트1 · 파트2 폴더 하나를 골랐으면 그 위 폴더에서 다시 찾는다(옆 폴더).
function Find-Both([string]$picked) {
    $p1 = Find-Game $picked { param($d) Test-Part1 $d }
    $p2 = Find-Game $picked { param($d) Test-Part2 $d }
    $parent = Split-Path -Parent $picked
    if ((-not $p1 -or -not $p2) -and $parent -and ((Test-Part1 $picked) -or (Test-Part2 $picked))) {
        if (-not $p1) { $p1 = Find-Game $parent { param($d) Test-Part1 $d } }
        if (-not $p2) { $p2 = Find-Game $parent { param($d) Test-Part2 $d } }
    }
    return @($p1, $p2)
}

function Get-Source([string]$given) {
    $path = $given
    while ($true) {
        if (-not $path) {
            Say "창세기전3 파트1 · 파트2 원본 폴더가 들어 있는 폴더를 골라 주세요."
            $path = Pick-Folder "창세기전3 파트1 · 파트2 원본 폴더가 들어 있는 폴더를 고르세요 (폴더 이름은 상관없습니다)"
            if (-not $path) { throw "폴더를 고르지 않아 그만둡니다." }
        }
        $path = (Resolve-Path -LiteralPath $path).ProviderPath
        $found = Find-Both $path
        if ($found[0] -and $found[1]) { return $found }
        $miss = @()
        if (-not $found[0]) { $miss += '파트1(Obss\Obss.idx 와 Chars 폴더)' }
        if (-not $found[1]) { $miss += '파트2(TXR\Txr.dat 와 Chr · Obs 폴더)' }
        Say "  [!] $path 안에서 $($miss -join ', ') 원본을 못 찾았습니다."
        if ($given) { throw "원본 폴더를 못 찾았습니다: $given" }
        $path = ""
    }
}

$script:zip = $null; $script:zipStream = $null
function Close-Zip {
    if ($script:zip) { $script:zip.Dispose(); $script:zip = $null }
    if ($script:zipStream) { $script:zipStream.Dispose(); $script:zipStream = $null }
}

function Add-ToZip([string]$file, [string]$entryName) {
    $ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    # 이미 눌린 꼴(영상 · mp3 · ogg · zip)은 다시 누르지 않는다 — 시간만 든다
    $level = if (@('.mov', '.bik', '.mp3', '.ogg', '.zip', '.wgm', '.pak', '.cab') -contains $ext) { [System.IO.Compression.CompressionLevel]::NoCompression } else { [System.IO.Compression.CompressionLevel]::Optimal }
    # 백신 검사 따위로 잠깐 잠긴 파일은 조금 기다렸다 다시 해 본다
    for ($try = 1; ; $try++) {
        try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($script:zip, $file, $entryName, $level); break }
        catch [System.IO.IOException] { if ($try -ge 5) { throw }; Start-Sleep -Seconds $try }
    }
}
# 메모리의 바이트를 그대로 zip 에 — 받은 자료를 디스크에 쓰지 않는다(백신이 새 파일을 잡고 있어 「다른 프로세스가 사용 중」이 나던 것)
function Add-BytesToZip([byte[]]$bytes, [string]$entryName) {
    $entry = $script:zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $w = $entry.Open()
    try { $w.Write($bytes, 0, $bytes.Length) } finally { $w.Dispose() }
}
function Add-TextToZip([string]$text, [string]$entryName) {
    Add-BytesToZip ((New-Object System.Text.UTF8Encoding($true)).GetPreamble() + [System.Text.Encoding]::UTF8.GetBytes($text)) $entryName
}

function Zip-GameFolder([string]$root, [string]$prefix) {
    Say ""
    Say "[$prefix] $root 을(를) 담습니다..."
    $files = Get-ChildItem -LiteralPath $root -Recurse -File -Force
    $n = 0; $skipped = 0; $bytes = 0
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/')
        if ($SkipExt -contains $f.Extension.ToLowerInvariant() -or $SkipName -contains $f.Name.ToLowerInvariant()) { $skipped++; continue }
        Add-ToZip $f.FullName ("$prefix/" + ($rel -replace '\\', '/'))
        $n++; $bytes += $f.Length
        if ($n % 500 -eq 0) { Say ("    {0}개 담음 ({1:N0}MB)" -f $n, ($bytes / 1MB)) }
    }
    Say ("  {0}개 담음 ({1:N0}MB), 실행 파일 등 {2}개 뺌" -f $n, ($bytes / 1MB), $skipped)
    return ("{0}: {1}개, {2:N0}MB" -f $prefix, $n, ($bytes / 1MB))
}

function Get-BytesSha256([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

# 받아서 메모리로 — sha256 이 주어지면 맞을 때까지(4번) 다시 받는다
function Download-Bytes([string]$url, [string]$sha256 = '') {
    for ($try = 1; $try -le 4; $try++) {
        try {
            $bytes = (New-Object Net.WebClient).DownloadData($url)
            if (-not $sha256 -or (Get-BytesSha256 $bytes) -eq $sha256) { return ,$bytes }
            throw "받은 파일이 깨졌습니다: $url"
        }
        catch { if ($try -eq 4) { throw }; Say "    다시 받기($try): $url"; Start-Sleep -Seconds ([math]::Pow(2, $try)) }
    }
}

function Zip-Extras {
    Say ""
    Say "[$DataName] 원작자 GitHub 릴리즈에서 추가 자료를 받습니다 (약 800MB, 인터넷 필요)..."
    $manifestBytes = Download-Bytes ($PackUrl + 'assets-base-1/pack.json')
    $pack = [System.Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    $want = @($pack.files | Where-Object { $_.tier -ge $OnDemandTier -and $_.name -notmatch '\.\.' })
    if ($ExtrasLimit -gt 0) { $want = @($want | Select-Object -First $ExtrasLimit) }
    $total = ($want | Measure-Object -Property size -Sum).Sum
    Say ("  {0}개, {1:N0}MB" -f $want.Count, ($total / 1MB))
    $n = 0; $got = 0
    foreach ($e in $want) {
        $bytes = Download-Bytes ($PackUrl + $e.release + '/' + $e.asset) $e.sha256
        Add-BytesToZip $bytes ("$DataName/assets/" + $e.name)
        $n++; $got += $e.size
        if ($n % 50 -eq 0) { Say ("    {0}/{1} ({2:N0}/{3:N0}MB)" -f $n, $want.Count, ($got / 1MB), ($total / 1MB)) }
    }
    return ("{0}: 추가 자료 {1}개, {2:N0}MB" -f $DataName, $n, ($got / 1MB))
}

$Guide = @"
창세기전3 리메이크 안드로이드판 — 폰에 넣을 자료

1. 이 zip 을 폰으로 옮겨, 안의 part1 · part2 · WarOfGenesis-data 세 폴더를 한 폴더에 그대로 풉니다.
   (내장 저장소나 SD카드 어디든 됩니다. 예: SD카드/Games/창세기전3/)
2. 안드로이드판 앱(APK)을 깔고 켭니다 — 「모든 파일 접근」을 허용해 주세요.
3. 「폴더 고르기」로 1번에서 푼 폴더(세 폴더가 들어 있는 폴더)를 고르고 「게임 시작」.

세이브 · 설정은 WarOfGenesis-data 폴더에 쌓입니다. 앱을 지웠다 다시 깔아도 이 폴더가 있으면 그대로 이어집니다.
원본 게임 자료는 저작권이 있습니다 — 원본을 가진 본인 기기에서만 쓰고, 남에게 나눠 주거나 인터넷에 올리지 마세요.
"@

try {
    Say "=== 창세기전3 리메이크 안드로이드판 — 폰에 넣을 자료 묶음 만들기 ==="
    $found = Get-Source $Source
    $Part1 = $found[0]; $Part2 = $found[1]
    if (-not $Out) { $Out = Join-Path ([Environment]::GetFolderPath('Desktop')) '창세기전3-안드로이드-자료.zip' }
    $outDir = Split-Path -Parent $Out
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $Out = [System.IO.Path]::GetFullPath($Out)
    # 원본 폴더 안에 만들면 만드는 zip 을 스스로 담게 된다
    foreach ($g in @($Part1, $Part2)) {
        if ($Out.StartsWith($g.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "만들 zip 이 원본 폴더 안입니다: $Out" }
    }
    Say "파트1: $Part1"
    Say "파트2: $Part2"
    Say "만들 파일: $Out"
    Say "(원본 크기에 따라 10~30분쯤 걸립니다. 이 창을 닫지 마세요.)"

    $temp = "$Out.만드는중"
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    $script:zipStream = [System.IO.File]::Open($temp, [System.IO.FileMode]::CreateNew)
    $script:zip = New-Object System.IO.Compression.ZipArchive($script:zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add((Zip-GameFolder $Part1 'part1'))
    $lines.Add((Zip-GameFolder $Part2 'part2'))
    if (-not $SkipExtras) { $lines.Add((Zip-Extras)) }
    else { Add-TextToZip "앱이 이 폴더를 게임 자료 폴더로 씁니다(세이브 · 설정).`r`n" "$DataName/자료-폴더.txt" }
    Add-TextToZip ($Guide + "`r`n담은 것($(Get-Date -Format 'yyyy-MM-dd')):`r`n" + ($lines -join "`r`n") + "`r`n") '사용법.txt'
    Close-Zip

    if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }
    Move-Item -LiteralPath $temp -Destination $Out

    Say ""
    Say "=== 끝 ==="
    foreach ($l in $lines) { Say "  $l" }
    Say ("만든 파일: {0}  ({1:N0}MB)" -f $Out, ((Get-Item -LiteralPath $Out).Length / 1MB))
    Say "zip 안의 「사용법.txt」 대로 폰에 넣어 주세요."
    exit 0
}
catch {
    Close-Zip
    if ($temp -and (Test-Path -LiteralPath $temp)) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    Say ""
    Say "[오류] $($_.Exception.Message)"
    Say "이 창의 글을 사진으로 찍어 물어봐 주세요."
    exit 1
}
