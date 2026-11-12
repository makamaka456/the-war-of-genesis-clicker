// 입력(터치 → 마우스, 키, 패드)과 APK 에 실은 파일 꺼내기.
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
    /// <summary>실제 패드가 있나 — 단축 아이콘을 숨긴다(사용자 결정).</summary>
    public static volatile bool RealPad;

    public static void Refresh()
    {
        bool any = false;
        foreach (int id in InputDevice.GetDeviceIds() ?? [])
            if (InputDevice.GetDevice(id) is { } d && IsPad(d.Sources)) any = true;
        PadState.Connected = any;
        RealPad = any;
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
            PadState.Connected = RealPad = true;
            return true;
        }
        if (bit == 0) return false;
        PadState.Connected = RealPad = true;
        if (e.Action == KeyEventActions.Down) PadState.Set(bit, true);
        else if (e.Action == KeyEventActions.Up) PadState.Set(bit, false);
        return true;
    }

    public static bool Motion(MotionEvent e)
    {
        if (!IsPad(e.Source) || e.Action != MotionEventActions.Move) return false;
        PadState.Connected = RealPad = true;
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
            if (Path.GetFileName(to) == "릴리즈노트.md")
            {
                using var reader = new StreamReader(src);
                File.WriteAllText(to, ReleaseNotesTrim.Head(reader.ReadToEnd()));
                return;
            }
            using var dst = File.Create(to);
            src.CopyTo(dst);
            return;
        }
        Directory.CreateDirectory(to);
        foreach (string name in names) Copy(assets, from + "/" + name, Path.Combine(to, name));
    }
}
