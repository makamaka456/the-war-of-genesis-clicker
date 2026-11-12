using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Provider;
using Android.Views;
using WarOfGenesis.Assets;
using WarOfGenesis.Droid.Game;
using Path = System.IO.Path;

namespace WarOfGenesis.Droid;

/// <summary>
/// 원본 게임 자리 고르기 화면 — PC 판 <c>OriginalGameSetup</c> 창(「파트2 폴더 고르기」 · 「파트1 폴더 고르기」 · 「끝내기」)의 안드로이드 판.
/// 안드로이드는 zip 도 고를 수 있다(사용자 결정 2026-11-12). 게임 화면(duel-dx)은 다음 단계에서 붙인다.
/// </summary>
/// <remarks>
/// 이 판(0.1.x 시험판)은 고른 뒤 확인 시험(원본 읽기 · 쓰기 · 읽기 속도)을 화면에 보여 준다 — 사용자 기기(SD카드)에서 되는지 보려는 것.
/// </remarks>
[Activity(Label = "창세기전3", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation)]
public class MainActivity : Activity
{
    private const int PickFolderCode = 11, PickZipCode = 12;
    private const string Caption = "창세기전3 파트2";

    private TextView _log = null!;
    private ScrollView _scroll = null!;
    private readonly List<Button> _buttons = [];
    private Thread? _worker;
    private bool _askedAccess;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        GameSetup.StorePath = Path.Combine(FilesDir!.AbsolutePath, "setup.txt");
        GameSetup.Fallback = FallbackDir;
        GameSetup.Load();
        SetContentView(BuildLayout());
        Intro();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!StorageAccess.Granted(this))
        {
            SetLog("원본 게임 폴더(SD카드 · 내장 저장소)를 읽으려면 「모든 파일 접근」을 허용해 주세요.\n허용한 뒤 뒤로 가기를 누르면 이 화면으로 돌아옵니다.\n(버튼을 누르면 허용 화면을 다시 엽니다)");
            if (!_askedAccess) { _askedAccess = true; StorageAccess.Ask(this); }
        }
        else if (_askedAccess)
        {
            _askedAccess = false;
            Intro();
        }
    }

    // ── 화면 ──────────────────────────────────────────────────────────────

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(16, 16, 32));
        root.SetPadding(24, 16, 24, 16);

        var title = new TextView(this) { Text = Caption + " — 안드로이드판 " + Version };
        title.SetTextColor(Color.White);
        title.TextSize = 18;
        root.AddView(title);

        _scroll = new ScrollView(this);
        _log = new TextView(this);
        _log.SetTextColor(Color.Rgb(220, 220, 220));
        _log.TextSize = 14;
        _log.SetTextIsSelectable(false);
        _scroll.AddView(_log);
        root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.Center);
        AddButton(row, "파트2 폴더 고르기", () => Pick(PickFolderCode));
        AddButton(row, "파트1 폴더 고르기", () => Pick(PickFolderCode));
        AddButton(row, "zip 고르기", () => Pick(PickZipCode));
        AddButton(row, "다시 확인", () => RunWork(Check));
        AddButton(row, "끝내기", FinishAffinity);
        root.AddView(row);
        return root;
    }

    private void AddButton(LinearLayout row, string text, Action onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.Click += (_, _) => onClick();
        _buttons.Add(b);
        row.AddView(b, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
    }

    private void Intro()
    {
        // PC 판 OriginalGameSetup 의 안내 글 + 안드로이드에서 달라진 것(zip · SD카드)
        SetLog("이 게임은 원본 「창세기전3 파트2」 · 「창세기전3 파트1」의 그림 · 소리 · 자료를 씁니다.\n\n"
             + "원본 파트2 가 든 폴더(TXR · Chr · Obs 폴더가 든 곳)를 골라 주세요. 원본 폴더에는 아무것도 쓰지 않습니다.\n"
             + "파트1 은 파트1 폴더(Chars · Obss · Btls 가 든 곳)를 고르세요. 두 폴더가 한 폴더 안에 있으면 그 폴더를 골라도 둘 다 찾습니다.\n\n"
             + "zip 도 고를 수 있습니다(원본 폴더를 누른 zip, PC 의 「자료-zip-만들기」가 만든 zip, 추가 자료 extras zip) — zip 옆에 한 번 풉니다.\n"
             + "SD카드에 있어도 됩니다. 내장 저장소는 거의 쓰지 않습니다.\n");
        if (GameSetup.Part2 != null || GameSetup.Part1 != null) RunWork(Check);
    }

    private void SetLog(string text) => RunOnUiThread(() => { _log.Text = text; });

    private void Say(string line) => RunOnUiThread(() =>
    {
        _log.Append(line + "\n");
        _scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
    });

    private void Busy(bool busy) => RunOnUiThread(() => { foreach (var b in _buttons) b.Enabled = !busy; });

    private void RunWork(Action work)
    {
        if (_worker is { IsAlive: true }) return;
        Busy(true);
        _worker = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { Say("\n[오류] " + ex.GetType().Name + ": " + ex.Message); }
            finally { Busy(false); }
        }) { IsBackground = true, Name = "setup" };
        _worker.Start();
    }

    // ── 고르기 ────────────────────────────────────────────────────────────

    private void Pick(int code)
    {
        if (!StorageAccess.Granted(this)) { StorageAccess.Ask(this); return; }
        var intent = code == PickFolderCode
            ? new Intent(Intent.ActionOpenDocumentTree)
            : new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable).SetType("application/zip")
                .PutExtra(Intent.ExtraMimeTypes, new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" });
        try { StartActivityForResult(intent, code); }
        catch (ActivityNotFoundException) { Say("이 기기에는 파일 고르기 화면이 없습니다."); }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is not { } uri) return;
        string? path = StoragePath.FromUri(uri, requestCode == PickFolderCode);
        if (path == null)
        {
            Say($"\n이 자리는 쓸 수 없습니다({uri}).\n파일 고르기 화면 왼쪽 메뉴에서 SD카드나 내장 저장소로 들어가 골라 주세요(「최근」 · 「다운로드」 바로가기 말고).");
            return;
        }
        if (requestCode == PickFolderCode)
            RunWork(() => { Say($"\n고른 폴더: {path}"); Say(GameSetup.UseFolder(path)); Check(); });
        else
            RunWork(() =>
            {
                Say($"\n고른 zip: {path}\n푸는 중… (처음 한 번만, 몇 분 걸릴 수 있습니다)");
                long shown = -1;
                string result = GameSetup.UseZip(path, (done, total) =>
                {
                    long pct = total > 0 ? done * 100 / total : 100;
                    if (pct / 5 == shown) return;
                    shown = pct / 5;
                    Say($"  {pct}%  ({done >> 20}/{total >> 20}MB)");
                });
                Say(result);
                Check();
            });
    }

    // ── 확인 시험 ─────────────────────────────────────────────────────────

    /// <summary>고른 자리로 원본 코드가 자료를 읽는지 · 게임 자료 폴더에 쓸 수 있는지 · 읽기 속도를 본다.</summary>
    private void Check()
    {
        GameSetup.Apply();
        Say("\n── 확인 ──");
        Say($"파트2 원본: {(GameSetup.HasPart2 ? "있음 — " + GameSetup.Part2 : "없음")}");
        Say($"파트1 원본: {(GameSetup.HasPart1 ? "있음 — " + GameSetup.Part1 : "없음")}");
        if (!GameSetup.HasPart2 && !GameSetup.HasPart1) { Say("원본 폴더나 zip 을 골라 주세요."); return; }

        string data = GameSetup.EnsureDataDir();
        Say($"게임 자료 · 세이브 폴더: {data}");
        Say($"  쓰기 시험: {(GameSetup.Writable(data) != null ? "됨" : "안 됨")}{(data.StartsWith(FilesDir!.AbsolutePath) ? " (SD카드에 못 써서 내장 저장소의 앱 폴더를 씁니다)" : "")}");
        string extras = Path.Combine(data, "assets");
        int extraCount = Directory.Exists(extras) ? Directory.EnumerateFiles(extras, "*", SearchOption.AllDirectories).Count() : 0;
        Say($"  추가 자료(extras): {(extraCount > 0 ? extraCount + "개" : "없음 — extras-01.zip 을 골라 주세요")}");

        if (GameSetup.HasPart2)
        {
            var txr = TxrTable.Open(Path.Combine(GameSetup.Part2!, "TXR", "Txr.dat"));
            Say($"파트2 읽기: 이름표 {txr.Rows.Count}줄 — {string.Join(", ", txr.Rows.Where(r => r.Text.Any(c => c is >= '가' and <= '힣')).Skip(1).Take(4).Select(r => r.Text))}");
            var (bytes, sec) = GameSetup.MeasureRead(GameSetup.Part2!);
            Say($"  읽기 속도: {bytes >> 20}MB 를 {sec:F1}초 — 초당 {(sec > 0 ? (bytes >> 20) / sec : 0):F0}MB");
        }
        if (GameSetup.HasPart1)
        {
            var names = Enumerable.Range(0, 40).Select(Part1Game.Character).Where(c => c is { Name.Length: > 0 }).Take(5).Select(c => c!.Name);
            Say($"파트1 읽기: 인물 — {string.Join(", ", names)}");
        }
        Say("\n여기까지 보이면 이 판의 시험은 끝입니다. 이 화면을 사진으로 찍어 보내 주세요. (게임 화면은 다음 판에서 붙입니다)");
    }

    /// <summary>그 자리에 못 쓸 때 대신 쓸 곳 — 같은 저장소(SD카드)의 앱 전용 폴더, 없으면 내장 저장소의 앱 폴더.</summary>
    private string FallbackDir(string near)
    {
        foreach (var dir in GetExternalFilesDirs(null) ?? [])
            if (dir?.AbsolutePath is { } p && StoragePath.SameVolume(p, near) && GameSetup.Writable(Path.Combine(p, GameSetup.DataFolderName)) is { } ok)
                return ok;
        return Path.Combine(FilesDir!.AbsolutePath, GameSetup.DataFolderName);
    }

    private string Version => PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName ?? "?";
}

/// <summary>파일 고르기 화면이 돌려준 주소(content://…)를 파일 경로(/storage/…)로 — 「모든 파일 접근」이 있으면 그 경로로 바로 읽는다.</summary>
internal static class StoragePath
{
    public static string? FromUri(Android.Net.Uri uri, bool tree)
    {
        if (uri.Scheme == "file") return uri.Path;
        string? id;
        try { id = tree ? DocumentsContract.GetTreeDocumentId(uri) : DocumentsContract.GetDocumentId(uri); }
        catch (Exception) { return null; }
        if (id == null) return null;
        if (id.StartsWith("raw:", StringComparison.Ordinal)) return id[4..];
        if (uri.Authority != "com.android.externalstorage.documents") return null;   // 다운로드 · 최근 바로가기 따위는 경로를 알 수 없다
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string volume = id[..colon], rel = id[(colon + 1)..];
        string basePath = volume.Equals("primary", StringComparison.OrdinalIgnoreCase) ? "/storage/emulated/0" : "/storage/" + volume;
        return rel.Length == 0 ? basePath : basePath + "/" + rel;
    }

    /// <summary>두 경로가 같은 저장소(내장 · SD카드)에 있나 — /storage/emulated/0/… · /storage/XXXX-XXXX/….</summary>
    public static bool SameVolume(string a, string b) => Volume(a) == Volume(b);

    private static string Volume(string p)
    {
        string[] parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "storage") return parts[1] == "emulated" ? "emulated" : parts[1];
        return parts.Length > 0 ? parts[0] : "";
    }
}

/// <summary>「모든 파일 접근」 권한 — 원본 게임 폴더(SD카드 · 내장 저장소의 공용 폴더)를 경로로 읽으려면 필요하다.</summary>
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
            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission, Android.Net.Uri.Parse("package:" + activity.PackageName));
            try { activity.StartActivity(intent); }
            catch (ActivityNotFoundException) { activity.StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission)); }
        }
        else activity.RequestPermissions([Android.Manifest.Permission.ReadExternalStorage, Android.Manifest.Permission.WriteExternalStorage], 1);
    }
}
