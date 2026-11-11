using System.IO.Compression;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Droid.Game;

/// <summary>
/// 폰에 넣은 원본 자료 자리 — 내장 저장소의 <c>WarOfGenesis</c> 폴더.
/// </summary>
/// <remarks>
/// PC 의 <c>pc-script/자료-zip-만들기.bat</c> 이 만든 zip(<c>part1-NN.zip</c> · <c>part2-NN.zip</c> · <c>extras-NN.zip</c>)을
/// 이 폴더에 넣으면 처음 켤 때 풀어 아래 꼴로 차린다(풀고 나면 zip 은 지워도 된다).
/// <code>
/// WarOfGenesis/part1/   파트1 원본 폴더(Obss\Obss.idx · Chars …)  → Part1Game 이 읽는다
/// WarOfGenesis/part2/   파트2 원본 폴더(TXR\Txr.dat · Chr · Obs …) → OriginalGame 이 읽는다
/// WarOfGenesis/assets/  원작자 릴리즈에서 받는 「찾을 때 받는 것」 → PC 판 %APPDATA%\DuelDx\assets 자리
/// </code>
/// zip 을 안 쓰고 폴더를 바로 넣어도 된다(이름만 part1 · part2 로).
/// </remarks>
public static class DataFolder
{
    /// <summary>자료 폴더 — 시험기는 <c>WAROFGENESIS_DATA</c> 로 바꾼다.</summary>
    public static string Root { get; set; } =
        Environment.GetEnvironmentVariable("WAROFGENESIS_DATA") is { Length: > 0 } dir ? dir : "/storage/emulated/0/WarOfGenesis";

    public static string Part1 => Path.Combine(Root, "part1");
    public static string Part2 => Path.Combine(Root, "part2");
    public static string Extras => Path.Combine(Root, "assets");

    public static bool HasPart1 => Part1Game.IsValid(Part1);
    public static bool HasPart2 => OriginalGame.IsValid(Part2);

    /// <summary>폴더에 놓인 아직 안 푼 zip — 풀면 zip 이름 옆에 <c>.done</c> 표시를 남긴다.</summary>
    public static IReadOnlyList<string> PendingZips()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.EnumerateFiles(Root, "*.zip")
            .Where(z => Path.GetFileName(z).StartsWith("part1-") || Path.GetFileName(z).StartsWith("part2-") || Path.GetFileName(z).StartsWith("extras-"))
            .Where(z => !File.Exists(z + ".done"))
            .OrderBy(z => z, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 남은 zip 을 모두 푼다. zip 안의 자리(<c>part1/…</c> · <c>part2/…</c> · <c>assets/…</c>)를 그대로 <see cref="Root"/> 밑에 둔다.
    /// </summary>
    /// <param name="progress">(지금 zip 이름, 푼 바이트, 모두 풀 바이트).</param>
    public static void ExtractPending(Action<string, long, long>? progress = null)
    {
        var zips = PendingZips();
        long total = 0, done = 0;
        foreach (string z in zips)
            using (var a = ZipFile.OpenRead(z)) total += a.Entries.Sum(e => e.Length);
        string root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var buffer = new byte[1 << 20];
        foreach (string z in zips)
        {
            string name = Path.GetFileName(z);
            using (var a = ZipFile.OpenRead(z))
                foreach (var e in a.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;
                    string to = Path.GetFullPath(Path.Combine(Root, e.FullName));
                    if (!to.StartsWith(root, StringComparison.Ordinal)) continue;   // zip 밖으로 나가는 이름은 무시
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    using (var src = e.Open())
                    using (var dst = File.Create(to))
                        for (int n; (n = src.Read(buffer, 0, buffer.Length)) > 0;)
                        {
                            dst.Write(buffer, 0, n);
                            done += n;
                            progress?.Invoke(name, done, total);
                        }
                }
            File.WriteAllText(z + ".done", DateTime.Now.ToString("s"));
        }
    }
}
