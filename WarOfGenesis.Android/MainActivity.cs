using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using WarOfGenesis.Assets;
using WarOfGenesis.Droid.Game;
using Environment = System.Environment;
using Path = System.IO.Path;

namespace WarOfGenesis.Droid;

/// <summary>
/// 1단계 골격 — 자료 폴더를 차리고(zip 풀기) 원본 자료를 PC 판 읽개로 한 번 읽어 본다. 게임 화면(duel-dx)은 다음 단계에서 붙인다.
/// </summary>
/// <remarks>
/// 화면은 PC 판과 같은 길로 그린다: CPU 가 <c>uint[]</c> 판(<see cref="FrameView.Pixels"/>, ARGB)에 그리고, 판을 비율 그대로 늘려 띄운다.
/// </remarks>
[Activity(Label = "창세기전3", MainLauncher = true, Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation)]
public class MainActivity : Activity
{
    private FrameView _view = null!;
    private readonly List<string> _lines = [];
    private Thread? _worker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _view = new FrameView(this, 640, 480);
        SetContentView(_view);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!StorageAccess.Granted(this)) { Show(["창세기전3 안드로이드판", "", "원본 자료 폴더를 읽으려면 「모든 파일 접근」을 허용해 주세요."]); StorageAccess.Ask(this); return; }
        if (_worker is { IsAlive: true }) return;
        _worker = new Thread(Prepare) { IsBackground = true, Name = "prepare" };
        _worker.Start();
    }

    /// <summary>자료 폴더를 살피고, 남은 zip 을 풀고, 원본 자료를 읽어 본다(뒤 스레드).</summary>
    private void Prepare()
    {
        try
        {
            string version = PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName ?? "?";
            _lines.Clear();
            Say($"창세기전3 안드로이드판 {version} — 1단계 골격");
            Say($"자료 폴더: {DataFolder.Root}");
            Directory.CreateDirectory(DataFolder.Root);
            if (DataFolder.PendingZips() is { Count: > 0 } zips)
            {
                Say($"zip {zips.Count}개를 풉니다 — 몇 분 걸립니다");
                int at = _lines.Count;
                long shown = -1;
                DataFolder.ExtractPending((name, done, total) =>
                {
                    long pct = total > 0 ? done * 100 / total : 100;
                    if (pct == shown) return;
                    shown = pct;
                    Set(at, $"  {name}  {pct}%  ({done >> 20}/{total >> 20}MB)");
                });
                Set(at, "  다 풀었습니다 (zip 은 지워도 됩니다)");
            }
            Say("");
            Say($"파트1 원본: {(DataFolder.HasPart1 ? "있음" : "없음 — part1 폴더 또는 part1-01.zip 을 넣어 주세요")}");
            Say($"파트2 원본: {(DataFolder.HasPart2 ? "있음" : "없음 — part2 폴더 또는 part2-01.zip 을 넣어 주세요")}");
            Say($"추가 자료(assets): {(Directory.Exists(DataFolder.Extras) ? Directory.EnumerateFiles(DataFolder.Extras, "*", SearchOption.AllDirectories).Count() + "개" : "없음")}");
            if (DataFolder.HasPart2)
            {
                // PC 판 읽개가 그대로 도는지 — 이름표(TXR)를 읽어 본다.
                var txr = TxrTable.Open(Path.Combine(DataFolder.Part2, "TXR", "Txr.dat"));
                Say($"TXR 이름표 {txr.Rows.Count}줄 읽음 — 예: {string.Join(", ", txr.Rows.Where(r => r.Text.Any(c => c is >= '가' and <= '힣')).Take(4).Select(r => r.Text))}");
            }
            Say("");
            Say("게임 화면은 다음 단계에서 붙입니다.");
        }
        catch (Exception ex)
        {
            Say("");
            Say("[오류] " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void Say(string line) { lock (_lines) _lines.Add(line); Redraw(); }
    private void Set(int index, string line) { lock (_lines) { while (_lines.Count <= index) _lines.Add(""); _lines[index] = line; } Redraw(); }
    private void Show(IEnumerable<string> lines) { lock (_lines) { _lines.Clear(); _lines.AddRange(lines); } Redraw(); }

    private void Redraw()
    {
        string[] lines;
        lock (_lines) lines = [.. _lines];
        _view.DrawText(lines);
    }
}

/// <summary>「모든 파일 접근」 권한 — 원본 자료가 내장 저장소의 공용 폴더에 있어서 필요하다.</summary>
internal static class StorageAccess
{
    public static bool Granted(Context context) =>
        OperatingSystem.IsAndroidVersionAtLeast(30)
            ? Android.OS.Environment.IsExternalStorageManager
            : context.CheckSelfPermission(Android.Manifest.Permission.WriteExternalStorage) == Permission.Granted;

    public static void Ask(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                Android.Net.Uri.Parse("package:" + activity.PackageName));
            try { activity.StartActivity(intent); }
            catch (ActivityNotFoundException) { activity.StartActivity(new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission)); }
        }
        else activity.RequestPermissions([Android.Manifest.Permission.ReadExternalStorage, Android.Manifest.Permission.WriteExternalStorage], 1);
    }
}
