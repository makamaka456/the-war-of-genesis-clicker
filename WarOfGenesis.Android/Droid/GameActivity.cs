// 게임 화면 — 원본 게임(duel-dx GameWindow)을 게임 스레드에서 돌리고, 그 뒷버퍼를 화면에 띄우고, 터치 · 패드 · 키를 윈도우 메시지로 넘긴다.
#nullable enable
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Views.InputMethods;
using DuelDx;
using DuelDx.Native;
using WarOfGenesis.Assets;
using WarOfGenesis.Droid.Game;
using Path = System.IO.Path;
using Bitmap = Android.Graphics.Bitmap;

namespace WarOfGenesis.Droid;

[Activity(Label = "창세기전3", Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard
                         | ConfigChanges.Navigation | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
public class GameActivity : Activity
{
    private GameView _view = null!;
    private static Thread? _game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window!.AddFlags(WindowManagerFlags.Fullscreen);
        HideBars();
        var root = new FrameLayout(this);
        _view = new GameView(this);
        root.AddView(_view);
        AddCornerButtons(root);
        SetContentView(root);
        PlatformHost.ScreenOn = on => RunOnUiThread(() => { if (on) Window!.AddFlags(WindowManagerFlags.KeepScreenOn); else Window!.ClearFlags(WindowManagerFlags.KeepScreenOn); });
        PlatformHost.Dialog = ShowDialogBlocking;
        PadInput.Refresh();
        if (_game == null) StartGame();
    }

    private void HideBars()
    {
#pragma warning disable CA1422 // 옛 기기(안드로이드 7~10)도 같은 깃발로 숨긴다
        Window!.DecorView.SystemUiFlags = SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable
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
    }

    protected override void OnResume()
    {
        base.OnResume();
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
        PlatformHost.Presenter = _view.Present;
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
                using (var window = new GameWindow()) window.Run();
                RunOnUiThread(FinishAffinity);
            }
            catch (Exception ex)
            {
                string log = $"{ex}";
                try { File.WriteAllText(Path.Combine(GameSetup.DataDir ?? filesDir, "crash.log"), log); } catch (Exception) { }
                _view.Status = "게임을 시작하지 못했습니다 — 이 화면을 사진으로 찍어 보내 주세요.\n\n" + ex.GetType().Name + ": " + ex.Message + "\n\n" + ex.StackTrace;
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
            b.Show();
        });
        done.Wait();
        return picked;
    }

    // ── 모서리 단추: 메뉴(PC 판 메뉴 막대) · 키보드 ─────────────────────────

    private void AddCornerButtons(FrameLayout root)
    {
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal, Alpha = 0.55f };
        bar.AddView(CornerButton("☰", ShowMenuBar));
        bar.AddView(CornerButton("⌨", () => _view.ToggleKeyboard()));
        root.AddView(bar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Right));
    }

    private TextView CornerButton(string label, Action click)
    {
        var b = new TextView(this) { Text = label, TextSize = 18, Gravity = GravityFlags.Center };
        b.SetTextColor(Android.Graphics.Color.White);
        b.SetBackgroundColor(Android.Graphics.Color.Argb(120, 0, 0, 0));
        int pad = (int)(8 * Resources!.DisplayMetrics!.Density);
        b.SetPadding(pad * 2, pad, pad * 2, pad);
        b.Click += (_, _) => click();
        return b;
    }

    private void ShowMenuBar()
    {
        // 펼치기 전에 게임에 알려 체크 표시를 고치게 한다(원본 WM_INITMENUPOPUP) — 게임 스레드가 처리할 틈을 조금 준다.
        if (Win32.MenuBar is { } bar)
            foreach (var item in bar.Items.ToArray())
                if (item.Sub != null) PlatformHost.Post(Win32.WM_INITMENUPOPUP, Win32.HandleOf(item.Sub), 0);
        _view.PostDelayed(() =>
        {
            var anchor = FindViewById(Android.Resource.Id.Content)!;
            var popup = new PopupMenu(this, _view, GravityFlags.Right);
            var actions = new Dictionary<int, Action>();
            int next = 1;
            if (Win32.MenuBar is { } menuBar) AddItems(popup.Menu!, menuBar, actions, ref next);
            actions[next] = () => StartActivity(new Intent(this, typeof(MainActivity)).PutExtra(MainActivity.ExtraSetup, true));
            popup.Menu!.Add(0, next, 9999, "원본 폴더 고르기 화면");
            popup.MenuItemClick += (_, e) => { if (actions.TryGetValue(e.Item!.ItemId, out var act)) act(); };
            popup.Show();
        }, 120);
    }

    private static void AddItems(IMenu menu, Win32.MenuNode node, Dictionary<int, Action> actions, ref int next)
    {
        foreach (var item in node.Items.ToArray())
        {
            if (item.Separator || item.Text == null) continue;
            string text = item.Text.Replace("&", "");
            if (item.Sub != null)
            {
                var sub = menu.AddSubMenu(0, next++, next, text)!;
                AddItems(sub, item.Sub, actions, ref next);
                continue;
            }
            int id = next++;
            var mi = menu.Add(0, id, id, text)!;
            mi.SetCheckable(item.Checked);
            mi.SetChecked(item.Checked);
            mi.SetEnabled(!item.Grayed);
            int command = (int)item.Id;
            actions[id] = () => PlatformHost.Command(command);
        }
    }

    // ── 키 · 패드 ────────────────────────────────────────────────────────

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e == null) return base.DispatchKeyEvent(e);
        if (PadInput.Key(e)) return true;
        int vk = KeyMap.ToVk(e.KeyCode);
        if (vk != 0)
        {
            if (e.Action == KeyEventActions.Down && e.RepeatCount == 0 || e.Action == KeyEventActions.Down && KeyMap.Repeats(vk)) PlatformHost.Key(vk, true);
            else if (e.Action == KeyEventActions.Up) PlatformHost.Key(vk, false);
            if (e.Action == KeyEventActions.Down && e.UnicodeChar is var uc and >= 32 && vk is not (Win32.VK_RETURN or Win32.VK_SPACE)) PlatformHost.Char((char)uc);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e) => (e != null && PadInput.Motion(e)) || base.DispatchGenericMotionEvent(e);

    public override void OnBackPressed() { }   // 뒤로 = Esc (DispatchKeyEvent 가 받는다)
}

/// <summary>뒷버퍼를 비율 그대로 화면 가운데에 띄우고, 터치를 창 좌표의 마우스로 바꾼다.</summary>
internal sealed class GameView : View
{
    private Bitmap? _bitmap;
    private int[] _pixels = [];
    private readonly object _gate = new();
    private readonly Paint _paint = new() { FilterBitmap = true };
    private readonly Paint _text = new() { Color = Android.Graphics.Color.White, AntiAlias = true };
    private volatile string? _status;
    private readonly TouchMouse _touch;

    public GameView(Context context) : base(context)
    {
        Focusable = true;
        FocusableInTouchMode = true;
        SetBackgroundColor(Android.Graphics.Color.Black);
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
        lock (_gate)
        {
            if (_bitmap == null) return;
            var (x, y, s) = Placement;
            canvas.DrawBitmap(_bitmap, null, new RectF(x, y, x + _bitmap.Width * s, y + _bitmap.Height * s), _paint);
        }
    }

    public override bool OnTouchEvent(MotionEvent? e) => e != null && _touch.Handle(e);

    // ── 글 입력(안드로이드 키보드) ──
    private bool _keyboard;

    public void ToggleKeyboard()
    {
        var imm = (InputMethodManager)Context!.GetSystemService(Context.InputMethodService)!;
        _keyboard = !_keyboard;
        RequestFocus();
        if (_keyboard) imm.ShowSoftInput(this, ShowFlags.Forced);
        else imm.HideSoftInputFromWindow(WindowToken, HideSoftInputFlags.None);
    }

    public override bool OnCheckIsTextEditor() => true;

    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs != null)
        {
            outAttrs.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagNoSuggestions;
            outAttrs.ImeOptions = ImeFlags.NoFullscreen | (ImeFlags)ImeAction.Done;
        }
        return new CharInput(this);
    }

    /// <summary>키보드가 친 글자를 WM_CHAR 로, 지우기를 Backspace 로.</summary>
    private sealed class CharInput(View view) : BaseInputConnection(view, false)
    {
        public override bool CommitText(Java.Lang.ICharSequence? text, int newCursorPosition)
        {
            foreach (char c in text?.ToString() ?? "")
            {
                if (c == '\n') { PlatformHost.Key(Win32.VK_RETURN, true); PlatformHost.Key(Win32.VK_RETURN, false); }
                else PlatformHost.Char(c);
            }
            return true;
        }

        public override bool DeleteSurroundingText(int beforeLength, int afterLength)
        {
            for (int i = 0; i < Math.Max(1, beforeLength); i++) { PlatformHost.Key(Win32.VK_BACK, true); PlatformHost.Key(Win32.VK_BACK, false); PlatformHost.Char('\b'); }
            return true;
        }
    }
}

/// <summary>
/// 터치 → 마우스(창 좌표). 누르고 떼기 = 왼쪽 클릭, 끌기 = 왼쪽 끌기, 길게 누르기 = 오른쪽 클릭,
/// 두 손가락 위아래 = 휠, 두 손가락 톡 = 오른쪽 클릭 (iOS 판 TouchInput 과 같은 짜임 — docs/iOS-포팅-참고.md).
/// </summary>
internal sealed class TouchMouse(GameView view, float density)
{
    private const int LongPressMs = 450;
    private readonly float _slop = 12 * density;
    private float _downX, _downY, _lastWheelY;
    private bool _dragging, _rightDone, _multi, _wheeled;
    private Java.Lang.IRunnable? _longPress;

    private (int X, int Y) ToClient(float x, float y)
    {
        var (ox, oy, s) = view.Placement;
        if (s <= 0) return (0, 0);
        return ((int)Math.Clamp((x - ox) / s, 0, PlatformHost.ClientW - 1), (int)Math.Clamp((y - oy) / s, 0, PlatformHost.ClientH - 1));
    }

    public bool Handle(MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            {
                (_downX, _downY) = (e.GetX(), e.GetY());
                _dragging = _rightDone = _multi = _wheeled = false;
                var (cx, cy) = ToClient(_downX, _downY);
                PlatformHost.Mouse(Win32.WM_MOUSEMOVE, cx, cy);
                _longPress = new Java.Lang.Runnable(() =>
                {
                    if (_dragging || _multi) return;
                    _rightDone = true;
                    PlatformHost.Mouse(Win32.WM_RBUTTONDOWN, cx, cy);
                    PlatformHost.Mouse(Win32.WM_RBUTTONUP, cx, cy);
                    view.PerformHapticFeedback(FeedbackConstants.LongPress);
                });
                view.PostDelayed(_longPress, LongPressMs);
                return true;
            }
            case MotionEventActions.PointerDown:
                CancelLongPress();
                if (_dragging) { var (ux, uy) = ToClient(e.GetX(0), e.GetY(0)); PlatformHost.Mouse(Win32.WM_LBUTTONUP, ux, uy); _dragging = false; }
                _multi = true;
                _lastWheelY = (e.GetY(0) + e.GetY(1)) / 2;
                return true;
            case MotionEventActions.Move:
            {
                if (_rightDone) return true;
                if (_multi && e.PointerCount >= 2)
                {
                    float y = (e.GetY(0) + e.GetY(1)) / 2, step = 40 * density;
                    while (Math.Abs(y - _lastWheelY) >= step)
                    {
                        int dir = y > _lastWheelY ? 1 : -1;   // 손가락을 아래로 = 위로 굴리기(휠 +)
                        var (wx, wy) = ToClient(e.GetX(0), e.GetY(0));
                        PlatformHost.Wheel(120 * dir, wx, wy);
                        _lastWheelY += step * dir;
                        _wheeled = true;
                    }
                    return true;
                }
                if (_multi) return true;
                var (cx, cy) = ToClient(e.GetX(), e.GetY());
                if (!_dragging && Math.Abs(e.GetX() - _downX) + Math.Abs(e.GetY() - _downY) > _slop)
                {
                    CancelLongPress();
                    _dragging = true;
                    var (sx, sy) = ToClient(_downX, _downY);
                    PlatformHost.Mouse(Win32.WM_LBUTTONDOWN, sx, sy);
                }
                PlatformHost.Mouse(Win32.WM_MOUSEMOVE, cx, cy);
                return true;
            }
            case MotionEventActions.Up:
            {
                CancelLongPress();
                var (cx, cy) = ToClient(e.GetX(), e.GetY());
                if (_multi)
                {
                    if (!_wheeled) { PlatformHost.Mouse(Win32.WM_RBUTTONDOWN, cx, cy); PlatformHost.Mouse(Win32.WM_RBUTTONUP, cx, cy); }
                }
                else if (_rightDone) { }
                else if (_dragging) PlatformHost.Mouse(Win32.WM_LBUTTONUP, cx, cy);
                else
                {
                    PlatformHost.Mouse(Win32.WM_MOUSEMOVE, cx, cy);
                    PlatformHost.Mouse(Win32.WM_LBUTTONDOWN, cx, cy);
                    PlatformHost.Mouse(Win32.WM_LBUTTONUP, cx, cy);
                }
                _dragging = false;
                return true;
            }
            case MotionEventActions.Cancel:
                CancelLongPress();
                if (_dragging) { var (cx, cy) = ToClient(e.GetX(), e.GetY()); PlatformHost.Mouse(Win32.WM_LBUTTONUP, cx, cy); }
                _dragging = false;
                return true;
        }
        return true;
    }

    private void CancelLongPress()
    {
        if (_longPress != null) view.RemoveCallbacks(_longPress);
        _longPress = null;
    }
}

/// <summary>안드로이드 키 → 윈도우 가상 키(VK). 키보드 · 리모컨 · 「뒤로」(= Esc).</summary>
internal static class KeyMap
{
    public static int ToVk(Keycode k)
    {
        if (k >= Keycode.A && k <= Keycode.Z) return 'A' + (k - Keycode.A);
        if (k >= Keycode.Num0 && k <= Keycode.Num9) return '0' + (k - Keycode.Num0);
        if (k >= Keycode.F1 && k <= Keycode.F12) return 0x70 + (k - Keycode.F1);
        return k switch
        {
            Keycode.Back or Keycode.Escape => Win32.VK_ESCAPE,
            Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadCenter => Win32.VK_RETURN,
            Keycode.Space => Win32.VK_SPACE,
            Keycode.DpadLeft => Win32.VK_LEFT,
            Keycode.DpadRight => Win32.VK_RIGHT,
            Keycode.DpadUp => Win32.VK_UP,
            Keycode.DpadDown => Win32.VK_DOWN,
            Keycode.Tab => Win32.VK_TAB,
            Keycode.Del => Win32.VK_BACK,
            Keycode.ShiftLeft or Keycode.ShiftRight => Win32.VK_SHIFT,
            Keycode.PageUp => Win32.VK_PRIOR,
            Keycode.PageDown => Win32.VK_NEXT,
            Keycode.MoveHome => Win32.VK_HOME,
            Keycode.MoveEnd => Win32.VK_END,
            Keycode.CtrlLeft or Keycode.CtrlRight => 0x11,
            Keycode.AltLeft or Keycode.AltRight => 0x12,
            Keycode.Minus => 0xBD,
            Keycode.Equals => 0xBB,
            Keycode.Comma => 0xBC,
            Keycode.Period => 0xBE,
            Keycode.Slash => 0xBF,
            Keycode.Grave => 0xC0,
            _ => 0,
        };
    }

    public static bool Repeats(int vk) => vk is Win32.VK_LEFT or Win32.VK_RIGHT or Win32.VK_UP or Win32.VK_DOWN or Win32.VK_BACK;
}

/// <summary>
/// 게임 패드 → XInput 꼴(PadState). 원본 GamePad 가 그 상태로 키 · 마우스 조작을 한다(PC 판과 같은 패드 조작 — 사용자 결정: 패드 = 키보드 · 마우스 매핑).
/// </summary>
internal static class PadInput
{
    public static void Refresh()
    {
        bool any = false;
        foreach (int id in InputDevice.GetDeviceIds() ?? [])
            if (InputDevice.GetDevice(id) is { } d && IsPad(d.Sources)) any = true;
        PadState.Connected = any;
    }

    private static bool IsPad(InputSourceType s) => (s & InputSourceType.Gamepad) == InputSourceType.Gamepad || (s & InputSourceType.Joystick) == InputSourceType.Joystick;

    public static bool Key(KeyEvent e)
    {
        if (!IsPad(e.Source)) return false;
        ushort bit = e.KeyCode switch
        {
            Keycode.ButtonA => PadState.A,
            Keycode.ButtonB => PadState.B,
            Keycode.ButtonX => PadState.X,
            Keycode.ButtonY => PadState.Y,
            Keycode.ButtonL1 => PadState.LeftShoulder,
            Keycode.ButtonR1 => PadState.RightShoulder,
            Keycode.ButtonThumbl => PadState.LeftThumb,
            Keycode.ButtonThumbr => PadState.RightThumb,
            Keycode.ButtonStart => PadState.Start,
            Keycode.ButtonSelect or Keycode.Back => PadState.Back,
            Keycode.DpadUp => PadState.DPadUp,
            Keycode.DpadDown => PadState.DPadDown,
            Keycode.DpadLeft => PadState.DPadLeft,
            Keycode.DpadRight => PadState.DPadRight,
            _ => 0,
        };
        if (e.KeyCode is Keycode.ButtonL2 or Keycode.ButtonR2)
        {
            lock (PadState.Gate)
            {
                byte v = e.Action == KeyEventActions.Down ? (byte)255 : (byte)0;
                if (e.KeyCode == Keycode.ButtonL2) PadState.LeftTrigger = v; else PadState.RightTrigger = v;
                PadState.Packet++;
            }
            PadState.Connected = true;
            return true;
        }
        if (bit == 0) return false;
        PadState.Connected = true;
        if (e.Action == KeyEventActions.Down) PadState.Set(bit, true);
        else if (e.Action == KeyEventActions.Up) PadState.Set(bit, false);
        return true;
    }

    public static bool Motion(MotionEvent e)
    {
        if (!IsPad(e.Source) || e.Action != MotionEventActions.Move) return false;
        PadState.Connected = true;
        static short ToShort(float v) => (short)Math.Clamp(v * 32767, -32767, 32767);
        lock (PadState.Gate)
        {
            PadState.LX = ToShort(e.GetAxisValue(Axis.X));
            PadState.LY = ToShort(-e.GetAxisValue(Axis.Y));
            PadState.RX = ToShort(e.GetAxisValue(Axis.Z));
            PadState.RY = ToShort(-e.GetAxisValue(Axis.Rz));
            float lt = Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake));
            float rt = Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas));
            PadState.LeftTrigger = (byte)Math.Clamp(lt * 255, 0, 255);
            PadState.RightTrigger = (byte)Math.Clamp(rt * 255, 0, 255);
            // 십자키가 축(HAT)으로 오는 패드
            float hx = e.GetAxisValue(Axis.HatX), hy = e.GetAxisValue(Axis.HatY);
            ushort b = (ushort)(PadState.Buttons & ~(PadState.DPadLeft | PadState.DPadRight | PadState.DPadUp | PadState.DPadDown));
            if (hx < -0.5f) b |= PadState.DPadLeft;
            if (hx > 0.5f) b |= PadState.DPadRight;
            if (hy < -0.5f) b |= PadState.DPadUp;
            if (hy > 0.5f) b |= PadState.DPadDown;
            PadState.Buttons = b;
            PadState.Packet++;
        }
        return true;
    }
}

/// <summary>APK 에 실은 우리 파일(PC 판이 exe 옆에 두는 것: assets/ JSON · 꺼낼 목록 · 아이콘 · 그림 예제, 릴리즈노트.md, contributor.md)을 실행 파일 자리로 꺼낸다.</summary>
internal static class ShippedFiles
{
    public static void Extract(Android.Content.Res.AssetManager assets, string filesDir, Action<string> status)
    {
        // 원본 코드는 AppContext.BaseDirectory 옆에서 찾는다(AssetsFolder.Shipped · ReleaseNotesScreen). 거기에 쓸 수 있으면 거기에.
        string target = AppContext.BaseDirectory;
        if (GameSetup.Writable(target) == null) throw new IOException($"실행 파일 자리에 쓸 수 없습니다: {target}");
        string stamp = Path.Combine(target, ".shipped-version");
        string version = typeof(ShippedFiles).Assembly.ManifestModule.ModuleVersionId.ToString();
        if (File.Exists(stamp) && File.ReadAllText(stamp) == version) return;
        status("게임 파일을 꺼내는 중…");
        Copy(assets, "shipped", target);
        File.WriteAllText(stamp, version);
    }

    private static void Copy(Android.Content.Res.AssetManager assets, string from, string to)
    {
        string[] names = assets.List(from) ?? [];
        if (names.Length == 0)
        {
            // 파일
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using var src = assets.Open(from);
            using var dst = File.Create(to);
            src.CopyTo(dst);
            return;
        }
        Directory.CreateDirectory(to);
        foreach (string name in names) Copy(assets, from + "/" + name, Path.Combine(to, name));
    }
}
