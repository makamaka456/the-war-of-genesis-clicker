// 게임 화면 — 원본 게임(duel-dx GameWindow)을 게임 스레드에서 돌리고, 뒷버퍼를 화면에 띄우고, 터치 · 패드 · 키를 윈도우 메시지로 넘긴다.
// 패드가 없을 때는 화면 양옆에 단축 아이콘(Shortcuts)을 둔다 — 원본 패드 동작과 같은 일을 한다(docs/mockup/shortcut-*.png, 사용자 확정 2026-11-12).
#nullable enable
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using DuelDx;
using DuelDx.Native;
using WarOfGenesis.Droid.Game;
using Path = System.IO.Path;
using Bitmap = Android.Graphics.Bitmap;
using Color = Android.Graphics.Color;

namespace WarOfGenesis.Droid;

[Activity(Label = "창세기전3", Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard
                         | ConfigChanges.Navigation | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode
                         | ConfigChanges.Density | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.LayoutDirection)]
public class GameActivity : Activity
{
    private const int PickBackdropCode = 21, PickAgain = -100;

    private GameView _view = null!;
    private Shortcuts _shortcuts = null!;
    private static Thread? _game;
    /// <summary>게임 스레드가 오류로 끝났을 때 보인 글 — 앱에 다시 들어와도 같은 글을 보인다.</summary>
    private static string? _failure;

    /// <summary>돌고 있는 원본 게임 창 — 단축 아이콘이 지금 화면(전투인가 따위)을 본다.</summary>
    internal static volatile GameWindow? Game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CrashLog.Install(FilesDir!.AbsolutePath);
        HideBars();
        Backdrop.Load(FilesDir!.AbsolutePath);
        var root = new FrameLayout(this);
        _view = new GameView(this);
        root.AddView(_view);
        _shortcuts = new Shortcuts(this, root, _view);
        SetContentView(root);
        // 화면이 다시 만들어져도(게임은 그대로 돈다) 새 화면에 이어 붙인다
        PlatformHost.Presenter = _view.Present;
        PlatformHost.ScreenOn = on => RunOnUiThread(() => { if (on) Window!.AddFlags(WindowManagerFlags.KeepScreenOn); else Window!.ClearFlags(WindowManagerFlags.KeepScreenOn); });
        PlatformHost.Dialog = ShowDialogBlocking;
        // 맨 아래에 「원본 폴더 다시 고르기」(런처로) — 런처는 원본을 골라 두면 건너뛰므로 다시 고를 길이 여기뿐이다.
        AndroidHooks.ShowTools = items => RunOnUiThread(() => ShowTools([.. items, new AndroidHooks.ToolItem("원본 폴더 다시 고르기", PickAgain, [])], "도구", null));
        AndroidHooks.ShowBackdrop = () => RunOnUiThread(ShowBackdropDialog);
        AndroidHooks.BackdropNameOf = () => Backdrop.Has ? "있음" : "없음";
        PadInput.Refresh();
        if (_game == null) StartGame();
        else if (!_game.IsAlive)
        {
            // 게임이 이미 끝난 프로세스에 다시 들어왔다 — 예전에는 게임을 새로 켜지 않아 까만 화면에 멈췄다(사용자 보고 2026-11-13,
            // 오프닝 영상을 켜고 게임을 끝낸 뒤 앱을 다시 켜니 아무리 눌러도 까만 화면). 새 프로세스로 다시 띄운다 —
            // 오류로 끝났었으면(그 글은 이미 보였고 crash.log 에 남았다) 고르기 화면에 머물러 원본 폴더를 고칠 수 있게 한다.
            RestartActivity.Run(this, setup: _failure != null);
            return;
        }
        KeepAliveService.Start(this);
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 31);   // 알림줄 표시(③) — 거절해도 서비스는 돈다
    }

    private void HideBars()
    {
#pragma warning disable CA1422 // 옛 기기(안드로이드 7~10)도 같은 깃발로 숨긴다
        Window!.AddFlags(WindowManagerFlags.Fullscreen);
        Window.DecorView.SystemUiFlags = SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable
                                         | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideBars();
    }

    protected override void OnPause()
    {
        base.OnPause();
        PadState.AppInFront = false;
        AndroidAudio.Pause(true);
        PlatformHost.Activate(false);
        MemoryTrim.Trim();   // ① 뒤로 가면 정리
    }

    public override void OnTrimMemory(TrimMemory level)
    {
        base.OnTrimMemory(level);
        if (level >= TrimMemory.UiHidden) MemoryTrim.Trim();
    }

    protected override void OnResume()
    {
        base.OnResume();
        HideBars();
        PadState.AppInFront = true;
        AndroidAudio.Pause(false);
        PlatformHost.Activate(true);
        PadInput.Refresh();
    }

    // ── 게임 스레드 ──────────────────────────────────────────────────────

    private void StartGame()
    {
        var metrics = Resources!.DisplayMetrics!;
        PlatformHost.ScreenW = Math.Max(metrics.WidthPixels, metrics.HeightPixels);
        PlatformHost.ScreenH = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
        string filesDir = FilesDir!.AbsolutePath;
        var assets = Assets!;
        _game = new Thread(() =>
        {
            try
            {
                AndroidPlatform.Install();
                _view.Status = "시작 준비 중…";
                ShippedFiles.Extract(assets, filesDir, s => _view.Status = s);
                GameSetup.StorePath = Path.Combine(filesDir, "setup.txt");
                GameSetup.Load();
                GameSetup.Apply();
                OriginalGameSetup.Progress = (text, done, total) => _view.Status = total > 0 ? $"{text} {done}/{total}" : text;
                if (!OriginalGameSetup.Ensure()) throw new DirectoryNotFoundException("원본 게임 폴더를 모릅니다 — 첫 화면에서 골라 주세요.");
                CustomSprites.InstallExamples();
                _view.Status = null;
                using (var window = new GameWindow())
                {
                    Game = window;
                    window.Run();
                }
                Game = null;
                // 게임 안 「종료」 — 프로세스까지 닫는다. 남겨 두면 다음에 앱을 켤 때 끝난 게임에 붙어 까만 화면이었다(OnCreate).
                RunOnUiThread(() =>
                {
                    KeepAliveService.Stop(this);
                    FinishAffinity();
                    Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
                });
            }
            catch (Exception ex)
            {
                CrashLog.Write("게임 스레드", ex);
                _view.Status = _failure = "게임을 시작하지 못했습니다 — 이 화면을 사진으로 찍어 보내 주세요.\n\n" + ex.GetType().Name + ": " + ex.Message + "\n\n" + ex.StackTrace;
            }
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "Game" };
        _game.Start();
    }

    // ── 대화 상자(원본 MessageBox) ─────────────────────────────────────────

    private int ShowDialogBlocking(string text, string caption, string[] buttons)
    {
        int picked = -1;
        using var done = new ManualResetEventSlim(false);
        RunOnUiThread(() =>
        {
            var b = new AlertDialog.Builder(this).SetTitle(caption)!.SetCancelable(false)!;
            if (buttons.Length <= 3)
            {
                b.SetMessage(text);
                if (buttons.Length > 0) b.SetPositiveButton(buttons[0], (_, _) => { picked = 0; done.Set(); });
                if (buttons.Length > 1) b.SetNegativeButton(buttons[1], (_, _) => { picked = 1; done.Set(); });
                if (buttons.Length > 2) b.SetNeutralButton(buttons[2], (_, _) => { picked = 2; done.Set(); });
            }
            else
            {
                b.SetTitle(caption + " — " + text);
                b.SetItems(buttons, (_, e) => { picked = e.Which; done.Set(); });
            }
            ShowImmersive(b.Create()!);
        });
        done.Wait();
        return picked;
    }

    /// <summary>대화 상자를 띄워도 위 · 아래 막대가 다시 나오지 않게.</summary>
    private void ShowImmersive(Dialog dialog)
    {
        dialog.Window?.SetFlags(WindowManagerFlags.NotFocusable, WindowManagerFlags.NotFocusable);
        dialog.Show();
#pragma warning disable CA1422
        if (dialog.Window != null) dialog.Window.DecorView.SystemUiFlags = Window!.DecorView.SystemUiFlags;
#pragma warning restore CA1422
        dialog.Window?.ClearFlags(WindowManagerFlags.NotFocusable);
        dialog.DismissEvent += (_, _) => HideBars();
    }

    // ── 시스템 메뉴 > 도구 (PC 판 메뉴 막대의 도구 · 개발) — 가운데 목록 창 ─────

    private void ShowTools(IReadOnlyList<AndroidHooks.ToolItem> items, string title, Action? back)
    {
        var names = items.Select(i => i.Children.Count > 0 ? i.Text + "  ▶" : i.Text).ToList();
        names.Add(back != null ? "◀ 뒤로" : "닫기");
        var b = new AlertDialog.Builder(this).SetTitle(title)!;
        b.SetItems([.. names], (_, e) =>
        {
            if (e.Which >= items.Count) { back?.Invoke(); return; }
            var item = items[e.Which];
            if (item.Children.Count > 0) ShowTools(item.Children, title + " › " + item.Text, () => ShowTools(items, title, back));
            else if (item.Command == PickAgain) ConfirmPickAgain();
            else PlatformHost.Command(item.Command);
        });
        ShowImmersive(b.Create()!);
    }

    /// <summary>
    /// 「원본 폴더 다시 고르기」 — 돌던 게임을 끝내고 앱을 새로 띄워 고르기 화면으로 간다. 예전에는 게임을 둔 채 고르기 화면을 띄워,
    /// 새로 고른 폴더가 지금 도는 게임에 안 먹었다(옛 원본 그대로).
    /// </summary>
    private void ConfirmPickAgain()
    {
        var b = new AlertDialog.Builder(this).SetTitle("원본 폴더 다시 고르기")!
            .SetMessage("게임을 끝내고 첫 화면(폴더 고르기)으로 갑니다.\n저장하지 않은 진행은 사라집니다.")!
            .SetPositiveButton("끝내고 가기", (_, _) => RestartActivity.Run(this, setup: true))!
            .SetNegativeButton("취소", (_, _) => { })!;
        ShowImmersive(b.Create()!);
    }

    // ── 설정 > 화면 > 배경 그림 ─────────────────────────────────────────────

    private void ShowBackdropDialog()
    {
        float dp = Resources!.DisplayMetrics!.Density;
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetPadding((int)(20 * dp), (int)(8 * dp), (int)(20 * dp), 0);
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var pick = new Button(this) { Text = "그림 고르기" };
        var clear = new Button(this) { Text = "그림 지우기" };
        pick.SetAllCaps(false);
        clear.SetAllCaps(false);
        row.AddView(pick, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        row.AddView(clear, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        box.AddView(row);
        void Slider(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var text = new TextView(this) { Text = $"{label}: {get()}%" };
            var bar = new SeekBar(this) { Max = max - min, Progress = get() - min };
            bar.ProgressChanged += (_, e) => { set(e.Progress + min); text.Text = $"{label}: {e.Progress + min}%"; _view.PostInvalidate(); };
            bar.StopTrackingTouch += (_, _) => Backdrop.Save();
            box.AddView(text);
            box.AddView(bar);
        }
        Slider("가로 크기(화면 너비 기준)", 20, 200, () => Backdrop.WidthPercent, v => Backdrop.WidthPercent = v);
        Slider("세로 크기(화면 높이 기준)", 20, 200, () => Backdrop.HeightPercent, v => Backdrop.HeightPercent = v);
        Slider("불투명도(0 = 안 보임)", 0, 100, () => Backdrop.OpacityPercent, v => Backdrop.OpacityPercent = v);
        var dialog = new AlertDialog.Builder(this).SetTitle("배경 그림 — 검은 여백 채우기")!.SetView(box)!.SetPositiveButton("닫기", (_, _) => Backdrop.Save())!.Create()!;
        pick.Click += (_, _) =>
        {
            dialog.Dismiss();
            var intent = new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable).SetType("image/*");
            try { StartActivityForResult(intent, PickBackdropCode); } catch (ActivityNotFoundException) { }
        };
        clear.Click += (_, _) => { Backdrop.Clear(); _view.PostInvalidate(); dialog.Dismiss(); };
        ShowImmersive(dialog);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickBackdropCode || resultCode != Result.Ok || data?.Data is not { } uri) return;
        try
        {
            using var src = ContentResolver!.OpenInputStream(uri)!;
            Backdrop.SetImage(src);
            _view.PostInvalidate();
            ShowBackdropDialog();   // 고른 뒤 크기 · 투명도를 바로 맞추게
        }
        catch (Exception ex) { Toast.MakeText(this, "그림을 읽지 못했습니다: " + ex.Message, ToastLength.Long)!.Show(); }
    }

    // ── 키 · 패드 ────────────────────────────────────────────────────────

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e == null) return base.DispatchKeyEvent(e);
        if (PadInput.Key(e)) { _shortcuts.Refresh(); return true; }
        int vk = KeyMap.ToVk(e.KeyCode);
        if (vk != 0)
        {
            if (e.Action == KeyEventActions.Down && (e.RepeatCount == 0 || KeyMap.Repeats(vk))) PlatformHost.Key(vk, true);
            else if (e.Action == KeyEventActions.Up) PlatformHost.Key(vk, false);
            if (e.Action == KeyEventActions.Down && e.UnicodeChar is var uc and >= 32 && vk is not (Win32.VK_RETURN or Win32.VK_SPACE)) PlatformHost.Char((char)uc);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e != null && PadInput.Motion(e)) { _shortcuts.Refresh(); return true; }
        return base.DispatchGenericMotionEvent(e);
    }

    public override void OnBackPressed() { }   // 뒤로 = Esc (DispatchKeyEvent 가 받는다)
}

/// <summary>뒷버퍼를 비율 그대로 화면 가운데에 띄우고(둘레는 배경 그림), 터치를 창 좌표의 마우스로 바꾼다.</summary>
internal sealed class GameView : View
{
    private Bitmap? _bitmap;
    private int[] _pixels = [];
    private readonly object _gate = new();
    private readonly Paint _paint = new() { FilterBitmap = true };
    private readonly Paint _back = new() { FilterBitmap = true };
    private readonly Paint _text = new() { Color = Color.White, AntiAlias = true };
    private volatile string? _status;
    private readonly TouchMouse _touch;

    public GameView(Context context) : base(context)
    {
        Focusable = true;
        FocusableInTouchMode = true;
        SetBackgroundColor(Color.Black);
        _text.TextSize = 16 * context.Resources!.DisplayMetrics!.Density;
        _touch = new TouchMouse(this, context.Resources.DisplayMetrics.Density);
    }

    /// <summary>게임 화면 대신 보일 글(준비 중 · 오류). null 이면 게임 화면.</summary>
    public string? Status { get => _status; set { _status = value; PostInvalidate(); } }

    /// <summary>게임 스레드가 다 그린 뒷버퍼 — 복사해 둔다.</summary>
    public void Present(uint[] px, int w, int h)
    {
        lock (_gate)
        {
            if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
            {
                _bitmap?.Recycle();
                _bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
                _pixels = new int[w * h];
            }
            var src = (int[])(object)px;
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = src[i] | unchecked((int)0xFF000000);
            _bitmap.SetPixels(_pixels, 0, w, 0, 0, w, h);
        }
        PostInvalidate();
    }

    /// <summary>뒷버퍼가 놓인 자리(화면 점) — (왼쪽, 위, 배율).</summary>
    public (float X, float Y, float Scale) Placement
    {
        get
        {
            int w = PlatformHost.ClientW, h = PlatformHost.ClientH;
            float s = Math.Min((float)Width / w, (float)Height / h);
            return ((Width - w * s) / 2, (Height - h * s) / 2, s);
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_status is { } status)
        {
            float y = 60;
            foreach (string line in status.Split('\n').Take(40)) { canvas.DrawText(line, 40, y, _text); y += _text.TextSize * 1.3f; }
            return;
        }
        if (Backdrop.Image is { } bg && Backdrop.OpacityPercent > 0)
        {
            float bw = Width * Backdrop.WidthPercent / 100f, bh = Height * Backdrop.HeightPercent / 100f;
            _back.Alpha = Backdrop.OpacityPercent * 255 / 100;
            canvas.DrawBitmap(bg, null, new RectF((Width - bw) / 2, (Height - bh) / 2, (Width + bw) / 2, (Height + bh) / 2), _back);
        }
        lock (_gate)
        {
            if (_bitmap == null) return;
            var (x, y, s) = Placement;
            canvas.DrawBitmap(_bitmap, null, new RectF(x, y, x + _bitmap.Width * s, y + _bitmap.Height * s), _paint);
        }
    }

    public override bool OnTouchEvent(MotionEvent? e) => e != null && _touch.Handle(e);
}

/// <summary>설정 > 화면 > 배경 그림 — 앱 폴더에 그림을 복사해 두고 크기 · 불투명도를 적어 둔다.</summary>
internal static class Backdrop
{
    private static string _dir = "";
    public static Bitmap? Image { get; private set; }
    public static bool Has => Image != null;
    public static int WidthPercent = 100, HeightPercent = 100, OpacityPercent = 100;

    private static string ImagePath => Path.Combine(_dir, "backdrop.img");
    private static string SettingsPath => Path.Combine(_dir, "backdrop.txt");

    public static void Load(string dir)
    {
        _dir = dir;
        try
        {
            if (File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Split(',') is [var w, var h, var o])
                (WidthPercent, HeightPercent, OpacityPercent) = (int.Parse(w), int.Parse(h), int.Parse(o));
            if (Image == null && File.Exists(ImagePath)) Image = Decode(ImagePath);
        }
        catch (Exception) { }
    }

    public static void Save()
    {
        try { File.WriteAllText(SettingsPath, $"{WidthPercent},{HeightPercent},{OpacityPercent}"); } catch (Exception) { }
    }

    public static void SetImage(Stream src)
    {
        using (var dst = File.Create(ImagePath)) src.CopyTo(dst);
        Image = Decode(ImagePath) ?? throw new IOException("그림 파일이 아닙니다");
        Save();
    }

    public static void Clear()
    {
        Image = null;
        try { File.Delete(ImagePath); } catch (Exception) { }
    }

    /// <summary>큰 사진은 화면 크기 정도로 줄여 읽는다(메모리).</summary>
    private static Bitmap? Decode(string path)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        int sample = 1;
        while (bounds.OutWidth / (sample * 2) >= 1920 || bounds.OutHeight / (sample * 2) >= 1920) sample *= 2;
        return BitmapFactory.DecodeFile(path, new BitmapFactory.Options { InSampleSize = sample });
    }
}

/// <summary>
/// 단축 아이콘 — 패드가 없을 때 원본 패드 동작을 화면 양옆 단추로(사용자 확정 시안 docs/mockup/shortcut-battle.png · shortcut-story.png).
/// 전투 중: 왼쪽 시스템 메뉴 · 배속 · 조작 안내 · 취소 / 오른쪽 공격 · 어빌리티 · 아이템 · 휴식 · 링 커맨드 · 상태 · 이전 인물 · 다음 인물.
/// 그 밖(이야기 · 필드 · 모세스): 왼쪽 시스템 메뉴 · 배속 / 오른쪽 확인 · 취소. 타이틀에서는 숨긴다. 실제 패드가 있으면 모두 숨긴다.
/// 단추를 누른 터치는 게임으로 넘어가지 않는다(iOS i8). 각 단추는 PC 판 키 설정의 그 동작 키를 보낸다(패드 동작도 같은 키를 보낸다 — GamePad.KeyOf).
/// </summary>
internal sealed class Shortcuts
{
    private readonly Activity _activity;
    private readonly GameView _view;
    private readonly LinearLayout _left, _right;
    private readonly float _dp;
    private string _mode = "";
    private TextView? _speed;

    public Shortcuts(Activity activity, FrameLayout root, GameView view)
    {
        _activity = activity;
        _view = view;
        _dp = activity.Resources!.DisplayMetrics!.Density;
        _left = Column(root, GravityFlags.Left);
        _right = Column(root, GravityFlags.Right);
        Tick();
    }

    private LinearLayout Column(FrameLayout root, GravityFlags side)
    {
        var col = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        col.SetGravity(GravityFlags.Center);
        root.AddView(col, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent, side | GravityFlags.CenterVertical));
        return col;
    }

    public void Refresh() => _activity.RunOnUiThread(() => Apply(Mode()));

    private void Tick()
    {
        Apply(Mode());
        _view.PostDelayed(Tick, 300);
    }

    private static string Mode()
    {
        if (PadInput.RealPad || GameActivity.Game is not { } g) return "none";
        try
        {
            if (g.TitleScr._titleOpen || g.Part1TitleScr._open) return "none";
            bool battle = g._battleLoaded && g.BoardIsMap && !g.Fld.FieldOpen && !g.Mos._mosesOpen && !g.EpisodesScr._episodesOpen;
            return battle ? "battle" : "story";
        }
        catch (Exception) { return "story"; }
    }

    private void Apply(string mode)
    {
        if (_speed != null && GameActivity.Game is { } game) _speed.Text = SpeedLabel(game);
        if (mode == _mode) { Layout(); return; }
        _mode = mode;
        _left.RemoveAllViews();
        _right.RemoveAllViews();
        _speed = null;
        if (mode == "none") return;
        Add(_left, "≡", "시스템 메뉴", false, () => Key(KeyAction.System, Win32.VK_ESCAPE));
        _speed = Add(_left, "»", "배속 1×", false, NextSpeed);
        if (mode == "battle")
        {
            Add(_left, "?", "조작 안내", false, () => Key(KeyAction.KeyHelp, 0));
            Add(_left, "✕", "취소", false, () => Tap(Win32.VK_ESCAPE));
            Add(_right, "▲", "공격", true, () => Key(KeyAction.Attack, 0));
            Add(_right, "★", "어빌리티", true, () => Key(KeyAction.Ability, 0));
            Add(_right, "◆", "아이템", false, () => Key(KeyAction.Item, 0));
            Add(_right, "Z", "휴식", false, () => Key(KeyAction.Rest, 0));
            Add(_right, "◎", "링 커맨드", false, () => Key(KeyAction.Ring, 0));
            Add(_right, "ⓘ", "상태", false, () => Key(KeyAction.Status, 0));
            Add(_right, "◀", "이전 인물", false, () => Unit(-1));
            Add(_right, "▶", "다음 인물", false, () => Unit(1));
        }
        else
        {
            Add(_right, "●", "확인", true, () => Tap(Win32.VK_RETURN));
            Add(_right, "✕", "취소", false, () => Tap(Win32.VK_ESCAPE));
        }
        Layout();
    }

    /// <summary>단추 크기 · 투명도 — 옆 여백이 넉넉하면 여백에, 좁으면(16:9 해상도) 게임 위에 반투명하게.</summary>
    private void Layout()
    {
        if (_mode == "none" || _view.Width == 0) return;
        var (x, _, _) = _view.Placement;
        int rows = Math.Max(_left.ChildCount, _right.ChildCount);
        int h = (int)Math.Min(64 * _dp, (_view.Height - 16 * _dp) / Math.Max(1, rows) - 6 * _dp);
        bool roomy = x >= 72 * _dp;
        int w = (int)(roomy ? Math.Min(x - 16 * _dp, 110 * _dp) : 72 * _dp);
        float alpha = roomy ? 0.85f : 0.45f;
        foreach (var col in new[] { _left, _right })
        {
            col.Alpha = alpha;
            for (int i = 0; i < col.ChildCount; i++)
            {
                var child = col.GetChildAt(i)!;
                var lp = (LinearLayout.LayoutParams)child.LayoutParameters!;
                if (lp.Width != w || lp.Height != h) { lp.Width = w; lp.Height = h; child.LayoutParameters = lp; }
            }
            var flp = (FrameLayout.LayoutParams)col.LayoutParameters!;
            int margin = (int)(roomy ? (x - w) / 2 : 4 * _dp);
            if (flp.LeftMargin != margin) { flp.LeftMargin = flp.RightMargin = margin; col.LayoutParameters = flp; }
        }
    }

    private TextView Add(LinearLayout col, string icon, string label, bool strong, Action press)
    {
        var b = new TextView(_activity) { Gravity = GravityFlags.Center };
        b.SetTextColor(Color.White);
        b.Text = icon + "\n" + label;
        b.TextSize = 11;
        var bg = new GradientDrawable();
        bg.SetCornerRadius(12 * _dp);
        bg.SetColor(strong ? Color.Argb(200, 200, 150, 40) : Color.Argb(200, 40, 40, 60));
        bg.SetStroke((int)(1.5f * _dp), Color.Argb(150, 255, 255, 255));
        b.Background = bg;
        b.Click += (_, _) => { b.PerformHapticFeedback(FeedbackConstants.VirtualKey); press(); };
        var lp = new LinearLayout.LayoutParams((int)(100 * _dp), (int)(60 * _dp)) { TopMargin = (int)(3 * _dp), BottomMargin = (int)(3 * _dp) };
        col.AddView(b, lp);
        return b;
    }

    /// <summary>PC 판 키 설정의 그 동작 키를 누르고 뗀다(없으면 <paramref name="fallback"/>).</summary>
    private static void Key(KeyAction action, int fallback)
    {
        int vk = fallback;
        try { if (GameActivity.Game is { } g && g._keys[action] is var k and > 0) vk = k; } catch (Exception) { }
        if (vk != 0) Tap(vk);
    }

    private static void Tap(int vk) { PlatformHost.Key(vk, true); PlatformHost.Key(vk, false); }

    /// <summary>이전 · 다음 인물 — 원본 패드 LB · RB 와 같이(GamePad: CycleReadyUnit(∓1)). 전투 밖이면 다음 인물 키.</summary>
    private static void Unit(int dir) => PlatformHost.RunOnGame(() =>
    {
        if (GameActivity.Game is not { } g) return;
        if (!g.Btl._abilityMenu && g.Btl._targetWork < 0 && g._battleLoaded) g.Btl.CycleReadyUnit(dir);
        else Key(KeyAction.NextUnit, 0);
    });

    /// <summary>배속 — 설정 > 게임 > 게임 속도와 같은 값(보통 · 1.5배 · 2배)을 돌린다(iOS 판과 같은 짜임).</summary>
    private static void NextSpeed() => PlatformHost.RunOnGame(() =>
    {
        if (GameActivity.Game is not { } g) return;
        int i = Math.Max(0, Array.IndexOf(GameWindow.SpeedChoices, g._gameSpeed));
        g.OnMenuCommand(GameWindow.MenuSpeedBase + (i + 1) % GameWindow.SpeedChoices.Length);
    });

    private static string SpeedLabel(GameWindow g) => "»\n배속 " + (g._gameSpeed / 100.0).ToString("0.#") + "×";
}

/// <summary>앱이 터진 까닭 · 안드로이드가 앱을 닫은 까닭을 적어 둔다(「잠깐 나갔다 오면 다시 시작」 원인 찾기).</summary>
internal static class CrashLog
{
    private static string _dir = "";

    public static void Install(string dir)
    {
        if (_dir.Length > 0) return;
        _dir = dir;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Write("처리 안 된 예외", ex); };
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write("안드로이드 예외", e.Exception);
    }

    public static void Write(string where, Exception ex)
    {
        try { File.AppendAllText(Path.Combine(_dir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n"); } catch (Exception) { }
    }
}
