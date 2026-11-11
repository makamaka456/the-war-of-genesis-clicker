# 창세기전3 리메이크 안드로이드판 — PC 에서 원본 자료 zip 을 만드는 스크립트
#
# 쓰는 법: 같은 폴더의 「자료-zip-만들기.bat」 을 두 번 누른다(이 파일을 직접 고칠 필요 없다).
#   폴더 고르기 창이 두 번 뜬다 → 파트1 폴더, 파트2 폴더를 고른다.
#   바탕화면의 「창세기전-안드로이드-자료」 폴더에 zip 이 생긴다.
#
# 만드는 것(zip 하나는 1.9GB 를 넘지 않게 나눈다 — GitHub 릴리즈 파일 하나는 2GB 까지):
#   part1-01.zip … : 파트1 원본 폴더 전체를 zip 안의 part1/ 아래에(실행 파일 · dll 은 뺀다)
#   part2-01.zip … : 파트2 원본 폴더 전체를 zip 안의 part2/ 아래에(같음)
#   extras-01.zip … : 원작자 GitHub 릴리즈에서 「게임이 찾을 때 받는 것」(pack.json 의 tier 90 이상 —
#                     파트1 폴더로 못 만드는 그림 · 시디 곡 · 기술 컷신)을 zip 안의 assets/ 아래에.
#                     PC 판이 %APPDATA%\DuelDx\assets 에 받는 것과 같은 자리 꼴이다.
#   목록.txt        : 무엇을 담았는지
#
# 원본 자료는 저작권이 있다. 만든 zip 은 본인 시험 · 본인 기기용으로만 쓰고, 공개 저장소 · 공개 릴리즈에 올리지 않는다.
#
# 사람 손 없이 돌릴 때: -Part1 <폴더> -Part2 <폴더> -Out <폴더> [-SkipExtras] [-ExtrasLimit N(시험용)]

param(
    [string]$Part1 = "",
    [string]$Part2 = "",
    [string]$Out = "",
    [switch]$SkipExtras,
    [int]$ExtrasLimit = 0,
    [long]$MaxZipBytes = 1900MB
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

function Get-Folder([string]$given, [string]$name, [scriptblock]$test, [string]$hint) {
    $path = $given
    while (-not (& $test $path)) {
        if ($path) { Say "  [!] 이 폴더는 $name 원본 폴더가 아닙니다: $path  ($hint 가 있어야 합니다)" }
        if ($given) { throw "$name 폴더가 맞지 않습니다: $given" }
        Say "$name 원본 게임 폴더를 골라 주세요 ($hint 가 든 폴더)"
        $path = Pick-Folder "창세기전3 $name 원본 게임 폴더를 고르세요 ($hint 가 든 폴더)"
        if (-not $path) { throw "$name 폴더를 고르지 않아 그만둡니다." }
    }
    return (Resolve-Path -LiteralPath $path).ProviderPath
}

# 나눠 담는 zip — 하나가 MaxZipBytes 를 넘기 전에 다음 번호로 넘어간다(파일 하나를 쪼개지는 않는다).
$script:zip = $null; $script:zipStream = $null; $script:zipNo = 0; $script:zipPrefix = ''
$script:made = New-Object System.Collections.Generic.List[string]
function Open-NextZip {
    Close-Zip
    $script:zipNo++
    $file = Join-Path $Out ('{0}-{1:D2}.zip' -f $script:zipPrefix, $script:zipNo)
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    $script:zipStream = [System.IO.File]::Open($file, [System.IO.FileMode]::CreateNew)
    $script:zip = New-Object System.IO.Compression.ZipArchive($script:zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    $script:made.Add($file)
    Say "  → $(Split-Path -Leaf $file)"
}
function Close-Zip {
    if ($script:zip) { $script:zip.Dispose(); $script:zip = $null }
    if ($script:zipStream) { $script:zipStream.Dispose(); $script:zipStream = $null }
}
function Start-Zips([string]$prefix) { Close-Zip; $script:zipPrefix = $prefix; $script:zipNo = 0; Open-NextZip }
function Make-Room([long]$size) {
    if ($script:zipStream.Length -gt 0 -and $script:zipStream.Length + $size -gt $MaxZipBytes) { Open-NextZip }
}
function Add-ToZip([string]$file, [string]$entryName) {
    Make-Room (Get-Item -LiteralPath $file).Length
    $ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    # 이미 눌린 꼴(영상 · mp3 · ogg · zip)은 다시 누르지 않는다 — 시간만 든다
    $level = if (@('.mov', '.bik', '.mp3', '.ogg', '.zip', '.wgm') -contains $ext) { [System.IO.Compression.CompressionLevel]::NoCompression } else { [System.IO.Compression.CompressionLevel]::Optimal }
    # 백신 검사 따위로 잠깐 잠긴 파일은 조금 기다렸다 다시 해 본다
    for ($try = 1; ; $try++) {
        try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($script:zip, $file, $entryName, $level); break }
        catch [System.IO.IOException] { if ($try -ge 5) { throw }; Start-Sleep -Seconds $try }
    }
    $script:zipStream.Flush()
}
# 메모리의 바이트를 그대로 zip 에 — 받은 자료를 디스크에 쓰지 않는다(백신이 새 파일을 잡고 있어 「다른 프로세스가 사용 중」이 나던 것)
function Add-BytesToZip([byte[]]$bytes, [string]$entryName) {
    Make-Room $bytes.Length
    $entry = $script:zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $w = $entry.Open()
    try { $w.Write($bytes, 0, $bytes.Length) } finally { $w.Dispose() }
    $script:zipStream.Flush()
}

function Zip-GameFolder([string]$root, [string]$prefix, $list) {
    Say ""
    Say "[$prefix] $root 을(를) 담습니다..."
    Start-Zips $prefix
    $files = Get-ChildItem -LiteralPath $root -Recurse -File -Force
    $n = 0; $skipped = 0; $bytes = 0
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/')
        if ($SkipExt -contains $f.Extension.ToLowerInvariant() -or $SkipName -contains $f.Name.ToLowerInvariant()) {
            $skipped++; $list.Add("  뺌  $prefix/$($rel -replace '\\','/')"); continue
        }
        Add-ToZip $f.FullName ("$prefix/" + ($rel -replace '\\', '/'))
        $n++; $bytes += $f.Length
        if ($n % 500 -eq 0) { Say ("    {0}개 담음 ({1:N0}MB)" -f $n, ($bytes / 1MB)) }
    }
    Close-Zip
    Say ("  {0}개 담음 ({1:N0}MB), 실행 파일 등 {2}개 뺌" -f $n, ($bytes / 1MB), $skipped)
    $list.Add(("{0}: {1}개, {2:N0}MB (원본 {3})" -f $prefix, $n, ($bytes / 1MB), $root))
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

function Zip-Extras($list) {
    Say ""
    Say "[extras] 원작자 GitHub 릴리즈에서 「찾을 때 받는 자료」를 받습니다 (약 800MB, 인터넷 필요)..."
    $old = Join-Path $Out '_받는중'   # 옛 판이 남긴 임시 폴더
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue }
    $manifestBytes = Download-Bytes ($PackUrl + 'assets-base-1/pack.json')
    $pack = [System.Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
    $want = @($pack.files | Where-Object { $_.tier -ge $OnDemandTier -and $_.name -notmatch '\.\.' })
    if ($ExtrasLimit -gt 0) { $want = @($want | Select-Object -First $ExtrasLimit) }
    $total = ($want | Measure-Object -Property size -Sum).Sum
    Say ("  {0}개, {1:N0}MB" -f $want.Count, ($total / 1MB))
    Start-Zips 'extras'
    $n = 0; $got = 0
    foreach ($e in $want) {
        $bytes = Download-Bytes ($PackUrl + $e.release + '/' + $e.asset) $e.sha256
        Add-BytesToZip $bytes ('assets/' + $e.name)
        $n++; $got += $e.size
        if ($n % 50 -eq 0) { Say ("    {0}/{1} ({2:N0}/{3:N0}MB)" -f $n, $want.Count, ($got / 1MB), ($total / 1MB)) }
    }
    Close-Zip
    # 어느 판의 목록으로 받았는지 남긴다(앱 · 시험기가 같은 목록인지 볼 수 있게)
    $list.Add(("extras: {0}개, {1:N0}MB (pack.json sha256 {2})" -f $n, ($got / 1MB), (Get-BytesSha256 $manifestBytes)))
}

# 다 만든 단계는 표시 파일(.part1.done 따위)을 남겨, 다시 실행하면 건너뛴다(같은 원본 폴더 · zip 이 그대로 있을 때만).
function Run-Stage([string]$prefix, [string]$key, [scriptblock]$work, $detail) {
    $mark = Join-Path $Out ".$prefix.done"
    $zips = @(Get-ChildItem -LiteralPath $Out -Filter "$prefix-*.zip" -File)
    if ((Test-Path -LiteralPath $mark) -and $zips.Count -gt 0) {
        $saved = [System.IO.File]::ReadAllLines($mark)
        $names = (@($zips | ForEach-Object { $_.Name }) | Sort-Object) -join '|'
        if ($saved.Count -ge 2 -and $saved[0] -eq $key -and $saved[1] -eq "zips=$names") {
            Say ""
            Say "[$prefix] 지난번에 이미 만들었습니다 — 건너뜁니다 (다시 만들려면 $prefix-*.zip 을 지우세요)"
            foreach ($z in $zips) { $script:made.Add($z.FullName) }
            foreach ($line in ($saved | Select-Object -Skip 2)) { $detail.Add($line) }
            return
        }
    }
    foreach ($z in $zips) { Remove-Item -LiteralPath $z.FullName -Force }
    if (Test-Path -LiteralPath $mark) { Remove-Item -LiteralPath $mark -Force }
    $mine = New-Object System.Collections.Generic.List[string]
    & $work $mine
    foreach ($line in $mine) { $detail.Add($line) }
    $names = (@(Get-ChildItem -LiteralPath $Out -Filter "$prefix-*.zip" -File | ForEach-Object { $_.Name }) | Sort-Object) -join '|'
    [System.IO.File]::WriteAllLines($mark, [string[]](@($key, "zips=$names") + $mine), (New-Object System.Text.UTF8Encoding($false)))
}

try {
    Say "=== 창세기전3 리메이크 안드로이드판 — 원본 자료 zip 만들기 ==="
    $Part1 = Get-Folder $Part1 '파트1' { param($p) Test-Part1 $p } 'Obss\Obss.idx 와 Chars 폴더'
    $Part2 = Get-Folder $Part2 '파트2' { param($p) Test-Part2 $p } 'TXR\Txr.dat 와 Chr · Obs 폴더'
    if (-not $Out) { $Out = Join-Path ([Environment]::GetFolderPath('Desktop')) '창세기전-안드로이드-자료' }
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    $Out = (Resolve-Path -LiteralPath $Out).ProviderPath
    Say "파트1: $Part1"
    Say "파트2: $Part2"
    Say "만들 곳: $Out"

    $list = New-Object System.Collections.Generic.List[string]
    $list.Add("창세기전3 리메이크 안드로이드판 원본 자료 — $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
    $list.Add("본인 시험 · 본인 기기용. 공개 저장소 · 공개 릴리즈에 올리지 마세요.")
    $list.Add("")
    $detail = New-Object System.Collections.Generic.List[string]

    Run-Stage 'part1' $Part1 { param($l) Zip-GameFolder $Part1 'part1' $l } $detail
    Run-Stage 'part2' $Part2 { param($l) Zip-GameFolder $Part2 'part2' $l } $detail
    if (-not $SkipExtras) { Run-Stage 'extras' "limit=$ExtrasLimit" { param($l) Zip-Extras $l } $detail }

    foreach ($d in $detail) { if (-not $d.StartsWith('  ')) { $list.Add($d) } }
    $list.Add("")
    $list.Add("만든 zip:")
    foreach ($z in $script:made) { $list.Add(("  {0}  {1:N0}MB" -f (Split-Path -Leaf $z), ((Get-Item -LiteralPath $z).Length / 1MB))) }
    $list.Add("")
    foreach ($d in $detail) { if ($d.StartsWith('  ')) { $list.Add($d) } }
    [System.IO.File]::WriteAllLines((Join-Path $Out '목록.txt'), $list, (New-Object System.Text.UTF8Encoding($true)))

    Say ""
    Say "=== 끝 ==="
    foreach ($z in $script:made) { Say ("  {0}  {1:N0}MB" -f (Split-Path -Leaf $z), ((Get-Item -LiteralPath $z).Length / 1MB)) }
    Say "폴더: $Out"
    exit 0
}
catch {
    Close-Zip
    Say ""
    Say "[오류] $($_.Exception.Message)"
    Say "이 창의 글을 사진으로 찍어 Claude 에게 보여 주세요."
    exit 1
}
