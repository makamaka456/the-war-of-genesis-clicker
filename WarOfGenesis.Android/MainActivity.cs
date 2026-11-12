using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views;
using WarOfGenesis.Assets;
using WarOfGenesis.Droid.Game;
using Path = System.IO.Path;
using Color = Android.Graphics.Color;

namespace WarOfGenesis.Droid;

/// <summary>
/// 런처 — 원본 게임 자리 고르기(PC 판 OriginalGameSetup 창의 안드로이드 판). 사용자 결정(2026-11-12): 「폴더 고르기」 단추 하나.
/// 고른 폴더 안을 <b>파일로</b> 살펴 파트2 · 파트1 원본과 게임 자료 폴더를 찾는다 — 폴더 이름은 보지 않는다(GameSetup.UseRoot).
/// 이미 골라 두었으면 곧장 게임으로 간다(게임 안 시스템 메뉴 > 도구 > 「원본 폴더 고르기」로 돌아온다).
/// </summary>
[Activity(Label = "창세기전3", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation)]
public class MainActivity : Activity
{
    private const int PickFolderCode = 11;
    /// <summary>고르기 화면으로 일부러 왔다 — 바로 게임으로 넘기지 않는다.</summary>
    public const string ExtraSetup = "setup";

    private TextView _log = null!;
    private ScrollView _scroll = null!;
    private Button _pick = null!, _start = null!;
    private Thread? _worker;
    private bool _askedAccess;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        GameSetup.StorePath = Path.Combine(FilesDir!.AbsolutePath, "setup.txt");
        GameSetup.Fallback = FallbackDir;
        GameSetup.Load();
        ExitReasons.ShowLast(this);
        if (!Intent!.GetBooleanExtra(ExtraSetup, false) && StorageAccess.Granted(this) && (GameSetup.HasPart2 || GameSetup.HasPart1))
        {
            StartGame();
            return;
        }
        SetContentView(BuildLayout());
        Intro();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_log == null) return;
        if (!StorageAccess.Granted(this))
        {
            SetLog("원본 게임 폴더(SD카드 · 내장 저장소)를 읽으려면 「모든 파일 접근」을 허용해 주세요.\n허용한 뒤 뒤로 가기를 누르면 이 화면으로 돌아옵니다.");
            if (!_askedAccess) { _askedAccess = true; StorageAccess.Ask(this); }
        }
        else if (_askedAccess) { _askedAccess = false; Intro(); }
    }

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Rgb(16, 16, 32));
        root.SetPadding(24, 16, 24, 16);
        var title = new TextView(this) { Text = "창세기전3 — 안드로이드판 " + Version, TextSize = 18 };
        title.SetTextColor(Color.White);
        root.AddView(title);
        _scroll = new ScrollView(this);
        _log = new TextView(this) { TextSize = 14 };
        _log.SetTextColor(Color.Rgb(220, 220, 220));
        _scroll.AddView(_log);
        root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _pick = Button("폴더 고르기", PickFolder);
        _start = Button("게임 시작", StartGame);
        _start.Enabled = GameSetup.HasPart2 || GameSetup.HasPart1;
        row.AddView(_pick, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        row.AddView(_start, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        root.AddView(row);
        return root;
    }

    private Button Button(string text, Action click)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.Click += (_, _) => click();
        return b;
    }

    private void Intro()
    {
        SetLog("이 게임은 원본 「창세기전3 파트2」 · 「창세기전3 파트1」의 그림 · 소리 · 자료를 씁니다. 원본 폴더에는 아무것도 쓰지 않습니다.\n\n"
             + "「폴더 고르기」로 원본 게임들을 넣어 둔 폴더를 골라 주세요(SD카드여도 됩니다). 그 안에서 찾습니다:\n"
             + "  · 파트2 원본 — TXR\\Txr.dat · Chr · Obs 가 든 폴더\n"
             + "  · 파트1 원본 — Obss\\Obss.idx · Chars 가 든 폴더\n"
             + "  · 게임 자료 · 세이브 폴더 — 추가 자료(assets)나 설정 · 세이브가 든 폴더 (없으면 새로 만듭니다)\n"
             + "폴더 이름은 무엇이든 됩니다 — 안에 든 파일로 알아봅니다.\n");
        if (GameSetup.Part2 != null || GameSetup.Part1 != null) RunWork(Check);
    }

    private void SetLog(string text) => RunOnUiThread(() => _log.Text = text);

    private void Say(string line) => RunOnUiThread(() =>
    {
        _log.Append(line + "\n");
        _scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
    });

    private void RunWork(Action work)
    {
        if (_worker is { IsAlive: true }) return;
        RunOnUiThread(() => _pick.Enabled = _start.Enabled = false);
        _worker = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { Say("\n[오류] " + ex.GetType().Name + ": " + ex.Message); }
            finally { RunOnUiThread(() => { _pick.Enabled = true; _start.Enabled = GameSetup.HasPart2 || GameSetup.HasPart1; }); }
        }) { IsBackground = true, Name = "setup" };
        _worker.Start();
    }

    private void PickFolder()
    {
        if (!StorageAccess.Granted(this)) { StorageAccess.Ask(this); return; }
        try { StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), PickFolderCode); }
        catch (ActivityNotFoundException) { Say("이 기기에는 폴더 고르기 화면이 없습니다."); }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickFolderCode || resultCode != Result.Ok || data?.Data is not { } uri) return;
        string? path = StoragePath.FromUri(uri, tree: true);
        if (path == null)
        {
            Say($"\n이 자리는 쓸 수 없습니다.\n폴더 고르기 화면 왼쪽 메뉴(☰)에서 SD카드나 내장 저장소로 들어가 골라 주세요(「최근」 · 「다운로드」 바로가기 말고).");
            return;
        }
        RunWork(() => { Say($"\n고른 폴더: {path}\n살펴보는 중…"); GameSetup.UseRoot(path); Check(); });
    }

    /// <summary>구동 확인 — 찾은 것을 보이고, 원본 코드로 실제로 읽어 보고, 게임 자료 폴더에 써 본다.</summary>
    private void Check()
    {
        GameSetup.Apply();
        Say("\n── 구동 확인 ──");
        Say(GameSetup.HasPart2 ? $"✔ 파트2 원본 — {GameSetup.Part2}" : "✘ 파트2 원본을 못 찾았습니다");
        Say(GameSetup.HasPart1 ? $"✔ 파트1 원본 — {GameSetup.Part1}" : "✘ 파트1 원본을 못 찾았습니다");
        if (!GameSetup.HasPart2 && !GameSetup.HasPart1) { Say("원본 게임이 든 폴더를 골라 주세요."); return; }
        if (GameSetup.DataDir is { } data)
        {
            bool writable = GameSetup.Writable(data) != null;
            Say($"{(writable ? "✔" : "✘")} 게임 자료 · 세이브 폴더 — {data}{(writable ? "" : " (쓸 수 없습니다)")}");
            int extras = GameSetup.ExtrasCount();
            Say(extras > 0 ? $"✔ 추가 자료(원작자 꾸러미) {extras}개" : "△ 추가 자료(원작자 꾸러미)가 없습니다 — 파트1 일부 그림 · 시디 곡 · 기술 컷신이 빠집니다");
        }
        try
        {
            if (GameSetup.HasPart2)
            {
                var txr = TxrTable.Open(Path.Combine(GameSetup.Part2!, "TXR", "Txr.dat"));
                Say($"✔ 파트2 읽기 — 이름표 {txr.Rows.Count}줄");
            }
            if (GameSetup.HasPart1)
            {
                var names = Enumerable.Range(0, 40).Select(Part1Game.Character).Where(c => c is { Name.Length: > 0 }).Take(3).Select(c => c!.Name);
                Say($"✔ 파트1 읽기 — 인물 {string.Join(", ", names)}");
            }
            Say("\n구동 준비가 됐습니다. 「게임 시작」을 누르세요.");
        }
        catch (Exception ex) { Say("✘ 원본 자료를 읽지 못했습니다: " + ex.Message); }
    }

    private void StartGame()
    {
        if (!(GameSetup.HasPart2 || GameSetup.HasPart1)) return;
        StartActivity(new Intent(this, typeof(GameActivity)));
        Finish();
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

/// <summary>
/// 안드로이드가 지난번에 앱을 닫은 까닭(안드로이드 11+) — 「잠깐 나갔다 오면 다시 시작」을 가리려고, 메모리가 모자라 닫혔으면 한 번 알리고 적어 둔다.
/// </summary>
internal static class ExitReasons
{
    public static void ShowLast(Activity a)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return;
        try
        {
            var am = (ActivityManager)a.GetSystemService(Context.ActivityService)!;
            var info = am.GetHistoricalProcessExitReasons(a.PackageName, 0, 1)?.FirstOrDefault();
            if (info == null) return;
            string stamp = Path.Combine(a.FilesDir!.AbsolutePath, "exit-seen.txt");
            if (File.Exists(stamp) && File.ReadAllText(stamp) == info.Timestamp.ToString()) return;
            File.WriteAllText(stamp, info.Timestamp.ToString());
            string why = info.Reason switch
            {
                3 => "메모리가 모자라 안드로이드가 앱을 닫았습니다(LOW_MEMORY)",
                4 => "앱이 터졌습니다(CRASH)",
                5 => "앱이 터졌습니다(NATIVE CRASH)",
                6 => "앱이 응답하지 않았습니다(ANR)",
                10 => "사용자가 닫았습니다",
                13 => "안드로이드가 다른 까닭으로 닫았습니다(OTHER)",
                _ => $"종료 코드 {info.Reason}",
            };
            string when = DateTimeOffset.FromUnixTimeMilliseconds(info.Timestamp).LocalDateTime.ToString("MM-dd HH:mm");
            File.AppendAllText(Path.Combine(a.FilesDir!.AbsolutePath, "exit-reasons.log"), $"{when} {why} · {info.Description} · 메모리 {info.Pss / 1024}MB\n");
            if (info.Reason is 3 or 4 or 5 or 6 or 13)
                Toast.MakeText(a, $"지난번({when}) 게임이 닫힌 까닭: {why} — 그때 쓰던 메모리 {info.Pss / 1024}MB", ToastLength.Long)!.Show();
        }
        catch (Exception) { }
    }
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
        if (uri.Authority != "com.android.externalstorage.documents") return null;
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string volume = id[..colon], rel = id[(colon + 1)..];
        string basePath = volume.Equals("primary", StringComparison.OrdinalIgnoreCase) ? "/storage/emulated/0" : "/storage/" + volume;
        return rel.Length == 0 ? basePath : basePath + "/" + rel;
    }

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
