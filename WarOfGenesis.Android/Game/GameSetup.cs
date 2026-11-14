using System.Diagnostics;
using System.IO.Compression;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Droid.Game;

/// <summary>
/// 원본 게임 자리 차리기 — PC 판 <c>duel-dx/System/OriginalGameSetup.cs</c>(파트2 · 파트1 폴더 고르기 창)의 안드로이드 판에서 화면과 상관없는 부분.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>원본 폴더는 <b>고른 자리에서 그대로</b> 읽는다(SD카드여도) — 복사하지 않는다. PC 판처럼 원본 폴더에는 아무것도 쓰지 않는다.</item>
/// <item>zip(원본 폴더를 통째로 누른 것이든 <c>pc-script</c> 가 만든 것이든)은 <b>그 zip 옆에 한 번 풀고</b> 그 안에서
///   <c>TXR\Txr.dat</c>(파트2) · <c>Obss\Obss.idx</c>(파트1)가 든 폴더를 찾는다. <c>assets/</c> 로 시작하는 zip(원작자 추가 자료)은 게임 자료 폴더에 푼다.</item>
/// <item>게임이 쓰는 자리(PC 판 <c>%APPDATA%\DuelDx</c> — 원본에서 꺼내 두는 자료 <c>assets</c> · 세이브 · 설정)는
///   <see cref="DataDir"/> 이다. 원본 코드가 읽는 환경 변수 <c>DUELDX_SAVEDIR</c> 로 건넨다. 원본 폴더도 <c>WAROFGENESIS_ORIGINAL</c> ·
///   <c>WAROFGENESIS_PART1</c> 로 건넨다 — 원본 코드를 안 고친다(docs/iOS-포팅-참고.md i7).</item>
/// </list>
/// 고른 것은 앱 안쪽의 작은 파일(<see cref="StorePath"/>)에 적어 둔다.
/// </remarks>
public static class GameSetup
{
    /// <summary>고른 자리를 적어 두는 파일 — 앱이 정한다(안드로이드: 앱 안쪽 폴더).</summary>
    public static string StorePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "setup.txt");

    /// <summary>그 자리에 쓸 수 없을 때 대신 쓸 곳 — 앱이 정한다(안드로이드: 같은 저장소 안의 앱 전용 폴더 → 앱 안쪽).</summary>
    public static Func<string, string> Fallback { get; set; } = _ => Path.Combine(AppContext.BaseDirectory, "data");

    public static string? Part2 { get; private set; }
    public static string? Part1 { get; private set; }
    public static string? DataDir { get; private set; }

    public static bool HasPart2 => OriginalGame.IsValid(Part2);
    public static bool HasPart1 => Part1Game.IsValid(Part1);

    /// <summary>게임 자료 폴더 이름 — 원본 폴더(들)를 둔 폴더 옆에 만든다.</summary>
    public const string DataFolderName = "WarOfGenesis-data";

    public static void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            foreach (string line in File.ReadAllLines(StorePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq], value = line[(eq + 1)..];
                if (value.Length == 0) continue;
                if (key == "part2") Part2 = value;
                else if (key == "part1") Part1 = value;
                else if (key == "data") DataDir = value;
            }
        }
        catch (IOException) { }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllLines(StorePath, [$"part2={Part2}", $"part1={Part1}", $"data={DataDir}"]);
    }

    /// <summary>
    /// 고른 폴더를 원본 폴더로 쓴다 — 그 폴더 · 그 밑(세 겹까지) · 그 옆 폴더에서 파트2 · 파트1 을 찾는다
    /// (PC 판도 파트2 를 고르면 옆의 파트1 을 찾는다 — <see cref="Part1Game"/> Siblings).
    /// </summary>
    /// <returns>찾은 것을 사람 말로.</returns>
    public static string UseFolder(string folder)
    {
        folder = Path.GetFullPath(folder).TrimEnd('/');
        var (p2, p1) = FindRoots(folder, 3);
        if (p2 == null && p1 == null && Path.GetDirectoryName(folder.TrimEnd('/')) is { Length: > 0 } parent)
            (p2, p1) = FindRoots(parent, 1);   // 원본 폴더 안의 하위 폴더(TXR 따위)를 고른 경우
        if (p2 == null && p1 == null)
            return $"'{folder}' 에서 원본 폴더를 못 찾았습니다 — 파트2 는 TXR\\Txr.dat · Chr · Obs, 파트1 은 Obss\\Obss.idx · Chars 가 든 폴더입니다.";
        // 고른 것 옆에 다른 파트가 있으면 같이 쓴다
        if (p2 != null && p1 == null && Path.GetDirectoryName(p2) is { } d2) p1 = FindRoots(d2, 1).Part1;
        if (p1 != null && p2 == null && Path.GetDirectoryName(p1) is { } d1) p2 = FindRoots(d1, 1).Part2;
        if (p2 != null) Part2 = p2;
        if (p1 != null) Part1 = p1;
        EnsureDataDir();
        Save();
        return string.Join("\n", new[] { p2 != null ? "파트2: " + p2 : null, p1 != null ? "파트1: " + p1 : null }.Where(s => s != null));
    }

    /// <summary>
    /// 런처의 「폴더 고르기」 — 고른 폴더(와 그 밑 세 겹)에서 파트2 · 파트1 원본과 게임 자료 폴더를 <b>안의 파일로</b> 찾는다(폴더 이름은 보지 않는다).
    /// 게임 자료 폴더가 없으면 고른 폴더 안에 새로 만든다.
    /// </summary>
    public static void UseRoot(string folder)
    {
        folder = Path.GetFullPath(folder).TrimEnd('/');
        var (p2, p1) = FindRoots(folder, 3);
        // 원본 폴더 안(TXR 따위)을 골랐으면 위로 올라가며 찾는다
        for (string? up = Path.GetDirectoryName(folder); p2 == null && p1 == null && up is { Length: > 1 }; up = Path.GetDirectoryName(up))
        {
            (p2, p1) = FindRoots(up, 1);
            if (folder.StartsWith(up + "/", StringComparison.Ordinal) && (p2 != null || p1 != null)) break;
        }
        // 찾은 원본 옆에 다른 파트가 있으면 같이(PC 판도 옆 폴더를 찾는다)
        if (p2 != null && p1 == null && Path.GetDirectoryName(p2) is { } d2) p1 = FindRoots(d2, 1).Part1;
        if (p1 != null && p2 == null && Path.GetDirectoryName(p1) is { } d1) p2 = FindRoots(d1, 1).Part2;
        Part2 = p2;
        Part1 = p1;
        // 고른 곳이 원본 폴더이거나 그 안이면, 원본 폴더를 둔 폴더를 기준으로 삼는다 — 원본 폴더에는 아무것도 쓰지 않는다
        foreach (string? root in (string?[])[p2, p1])
            if (root != null && (folder == root || folder.StartsWith(root + "/", StringComparison.Ordinal)))
                folder = Path.GetDirectoryName(root)!;
        string? data = FindDataDir(folder, 3);
        DataDir = data != null && Writable(data) != null ? data : null;
        if (DataDir == null && (p2 != null || p1 != null))
        {
            string? created = Writable(Path.Combine(folder, DataFolderName));
            DataDir = created ?? Fallback(folder);
            Directory.CreateDirectory(DataDir);
        }
        Save();
    }

    /// <summary>
    /// 게임 자료 · 세이브 폴더인가 — 게임이 그 폴더에 쓰는 것(settings.json · keys.json · pad.json · last-part.txt · part1.json · timedhit.json)이나
    /// 원본에서 꺼낸 자료 · 원작자 추가 자료(assets 밑의 effects · part1 · data · .shipped)가 있으면 그렇다고 본다. 원본 게임 폴더는 아니어야 한다.
    /// </summary>
    public static bool LooksLikeDataDir(string dir)
    {
        if (OriginalGame.IsValid(dir) || Part1Game.IsValid(dir)) return false;
        try
        {
            foreach (string f in (string[])["settings.json", "keys.json", "pad.json", "last-part.txt", "part1.json", "timedhit.json"])
                if (File.Exists(Path.Combine(dir, f))) return true;
            string assets = Path.Combine(dir, "assets");
            return Directory.Exists(assets) && (Directory.Exists(Path.Combine(assets, "effects")) || Directory.Exists(Path.Combine(assets, "part1"))
                                                || Directory.Exists(Path.Combine(assets, "data")) || File.Exists(Path.Combine(assets, ".shipped")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>그 폴더와 그 밑 <paramref name="depth"/> 겹에서 게임 자료 폴더를 찾는다(원본 게임 폴더 안으로는 안 들어간다).</summary>
    public static string? FindDataDir(string folder, int depth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            var (dir, d) = queue.Dequeue();
            if (LooksLikeDataDir(dir)) return dir;
            if (d >= depth || OriginalGame.IsValid(dir) || Part1Game.IsValid(dir)) continue;
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    if (!Path.GetFileName(sub).StartsWith('.') && Path.GetFileName(sub) != "assets") queue.Enqueue((sub, d + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>원작자 추가 자료(extras zip 을 푼 것 — assets/part1 · assets/effects/cut) 파일 수.</summary>
    public static int ExtrasCount()
    {
        if (DataDir == null) return 0;
        int n = 0;
        foreach (string sub in (string[])["part1", Path.Combine("effects", "cut")])
        {
            string dir = Path.Combine(DataDir, "assets", sub);
            try { if (Directory.Exists(dir)) n += Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return n;
    }

    /// <summary>그 폴더와 그 밑 <paramref name="depth"/> 겹 안에서 파트2 · 파트1 원본 폴더를 찾는다(먼저 찾은 것).</summary>
    public static (string? Part2, string? Part1) FindRoots(string folder, int depth)
    {
        string? p2 = null, p1 = null;
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0 && (p2 == null || p1 == null))
        {
            var (dir, d) = queue.Dequeue();
            if (p2 == null && OriginalGame.IsValid(dir)) { p2 = dir; continue; }
            if (p1 == null && Part1Game.IsValid(dir)) { p1 = dir; continue; }
            if (d >= depth) continue;
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    if (!Path.GetFileName(sub).StartsWith('.')) queue.Enqueue((sub, d + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return (p2, p1);
    }

    /// <summary>
    /// zip 을 쓴다 — 원작자 추가 자료(<c>assets/…</c>)면 게임 자료 폴더에 풀고, 아니면 zip 옆(못 쓰면 대신 쓸 곳)에 풀어 그 안의 원본 폴더를 쓴다.
    /// 이미 푼 zip(옆에 <c>.done</c> 표시)은 다시 풀지 않는다.
    /// </summary>
    /// <param name="progress">(푼 바이트, 모두 풀 바이트).</param>
    public static string UseZip(string zip, Action<long, long>? progress = null)
    {
        bool extras;
        using (var a = ZipFile.OpenRead(zip))
            extras = a.Entries.Count > 0 && a.Entries.All(e => e.FullName.StartsWith("assets/", StringComparison.Ordinal));
        if (extras)
        {
            string data = EnsureDataDir(Path.GetDirectoryName(zip)!);
            Extract(zip, data, progress);
            Save();
            return $"추가 자료를 풀었습니다: {Path.Combine(data, "assets")}";
        }
        string target = Writable(Path.GetDirectoryName(zip)!) ?? Fallback(zip);
        // 맨 위가 폴더 하나뿐이면(part2/… · G3P2DVD_Win/… 꼴) zip 옆에 그대로 푼다 — 아니면(TXR/… 이 맨 위) zip 이름의 폴더를 만들어 그 안에.
        if (!SingleTopFolder(zip)) target = Path.Combine(target, Path.GetFileNameWithoutExtension(zip));
        Extract(zip, target, progress);
        return UseFolder(target);
    }

    /// <summary>zip 안의 것이 모두 맨 위 폴더 하나 밑에 있나.</summary>
    public static bool SingleTopFolder(string zip)
    {
        using var a = ZipFile.OpenRead(zip);
        var tops = a.Entries.Select(e => e.FullName.Replace('\\', '/')).Select(n => n.IndexOf('/') is var i and > 0 ? n[..i] : "").Distinct().ToList();
        return tops.Count == 1 && tops[0].Length > 0;
    }

    /// <summary>zip 을 <paramref name="to"/> 밑에 푼다(zip 밖으로 나가는 이름은 무시). 다 풀면 <c>to/.done-zip이름</c> 표시.</summary>
    public static void Extract(string zip, string to, Action<long, long>? progress = null)
    {
        string mark = Path.Combine(to, ".done-" + Path.GetFileName(zip));
        if (File.Exists(mark) && File.ReadAllText(mark) == new FileInfo(zip).Length.ToString()) return;
        Directory.CreateDirectory(to);
        string root = Path.GetFullPath(to).TrimEnd('/') + "/";
        var buffer = new byte[1 << 20];
        using var a = ZipFile.OpenRead(zip);
        long total = a.Entries.Sum(e => e.Length), done = 0;
        foreach (var e in a.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            string dest = Path.GetFullPath(Path.Combine(to, e.FullName.Replace('\\', '/')));
            if (!dest.StartsWith(root, StringComparison.Ordinal)) continue;
            if (File.Exists(dest) && new FileInfo(dest).Length == e.Length) { done += e.Length; progress?.Invoke(done, total); continue; }   // 지난번에 끊긴 데서 잇기
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var src = e.Open())
            using (var dst = File.Create(dest))
                for (int n; (n = src.Read(buffer, 0, buffer.Length)) > 0;)
                {
                    dst.Write(buffer, 0, n);
                    done += n;
                    progress?.Invoke(done, total);
                }
        }
        File.WriteAllText(mark, new FileInfo(zip).Length.ToString());
    }

    /// <summary>게임 자료 폴더를 정한다 — 원본 폴더를 둔 폴더(없으면 <paramref name="near"/>) 옆, 못 쓰면 대신 쓸 곳.</summary>
    public static string EnsureDataDir(string? near = null)
    {
        if (DataDir != null && Writable(DataDir) != null) return DataDir;
        string? baseDir = Path.GetDirectoryName((Part2 ?? Part1 ?? near ?? "").TrimEnd('/'));
        if (Part2 == null && Part1 == null && near != null) baseDir = near;
        string? dir = baseDir is { Length: > 0 } ? Writable(Path.Combine(baseDir, DataFolderName)) : null;
        DataDir = dir ?? Fallback(baseDir ?? near ?? "");
        Directory.CreateDirectory(DataDir);
        return DataDir;
    }

    /// <summary>그 폴더에 쓸 수 있으면 그 자리(없으면 만든다), 못 쓰면 null — 작은 파일을 써 보고 지운다.</summary>
    public static string? Writable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "ok");
            bool ok = File.ReadAllText(probe) == "ok";
            File.Delete(probe);
            return ok ? dir : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>원본 코드에 자리를 건넨다 — 게임을 띄우기 전에 부른다.</summary>
    public static void Apply()
    {
        if (Part2 != null) { Environment.SetEnvironmentVariable("WAROFGENESIS_ORIGINAL", Part2); if (HasPart2) OriginalGame.Use(Part2); }
        if (Part1 != null) { Environment.SetEnvironmentVariable("WAROFGENESIS_PART1", Part1); if (HasPart1) Part1Game.Use(Part1); }
        if (DataDir != null) Environment.SetEnvironmentVariable("DUELDX_SAVEDIR", DataDir);
    }

    /// <summary>원본 폴더에서 몇십 MB 를 읽어 읽기 속도(MB/초)를 잰다 — SD카드가 게임에 충분히 빠른지.</summary>
    public static (long Bytes, double Seconds) MeasureRead(string root, long budget = 64L << 20)
    {
        var sw = Stopwatch.StartNew();
        long read = 0;
        var buffer = new byte[1 << 20];
        foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderByDescending(f => new FileInfo(f).Length).Take(64))
        {
            using var s = File.OpenRead(f);
            for (int n; read < budget && (n = s.Read(buffer, 0, buffer.Length)) > 0;) read += n;
            if (read >= budget) break;
        }
        return (read, sw.Elapsed.TotalSeconds);
    }
}
