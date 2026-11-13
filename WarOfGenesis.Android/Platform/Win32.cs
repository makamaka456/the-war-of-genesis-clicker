// duel-dx/Win32.cs 의 안드로이드 · 시험기 판 — 이름 · 값 · 모양은 원본과 같고, 윈도우 대신 PlatformHost 의 메시지 줄 · 메뉴 · 키 상태로 돈다.
// 원본(duel-dx/Win32.cs)은 링크하지 않는다(csproj). 원본에 새 Win32 호출이 생기면 여기에도 넣어야 컴파일된다 — 병합 점검 목록(docs/포팅-계획.md).
#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DuelDx.Native;

internal static unsafe class Win32
{
    public const int CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_VISIBLE = 0x10000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const int MK_LBUTTON = 0x0001;
    public const uint WM_SETCURSOR = 0x0020;
    public const int HTCLIENT = 1;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F;
    public const int GCS_COMPSTR = 0x0008;
    public const int VK_BACK = 0x08;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_INITMENUPOPUP = 0x0117;
    public const uint MF_GRAYED = 0x0001, MF_BYPOSITION = 0x0400;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_APP_SNAPSHOT = 0x8001;
    public const uint MF_STRING = 0x0000, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    public const uint MF_BYCOMMAND = 0x0000, MF_CHECKED = 0x0008, MF_UNCHECKED = 0x0000;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
    public const int GWL_STYLE = -16, WS_POPUP = unchecked((int)0x80000000);
    public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_CYMENU = 15;
    public const uint PM_REMOVE = 0x0001;
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const int VK_LBUTTON = 0x01, VK_SHIFT = 0x10, VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_ESCAPE = 0x1B, VK_F11 = 0x7A, VK_F12 = 0x7B;
    public const int VK_TAB = 0x09;
    public const int VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    public const int IDC_ARROW = 32512;
    public const uint SPI_GETWORKAREA = 0x0030;
    public const uint ES_CONTINUOUS = 0x80000000, ES_DISPLAY_REQUIRED = 0x00000002, ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const int SC_SCREENSAVE = 0xF140, SC_MONITORPOWER = 0xF170;
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProc WindowProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    // ── 창 ───────────────────────────────────────────────────────────────

    /// <summary>창은 하나뿐이다(게임 창). 손잡이 값은 아무거나.</summary>
    internal static readonly IntPtr MainWindow = new(0x1000);
    internal static readonly IntPtr FrameWaitHandle = new(0x2000);
    private static WndProc? _proc;

    public static ushort RegisterClassExW(ref WndClassEx wndClass) { _proc = wndClass.WindowProc; return 1; }

    public static IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param)
    {
        _menuBar = menu;
        return MainWindow;
    }

    public static IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        msg == WM_CLOSE ? (PostQuit(), IntPtr.Zero).Item2 : IntPtr.Zero;

    private static bool PostQuit() { PlatformHost.Post(WM_QUIT, 0, 0); return true; }
    public static bool DestroyWindow(IntPtr hWnd) => true;
    public static bool ShowWindow(IntPtr hWnd, int cmdShow) => true;
    public static bool UpdateWindow(IntPtr hWnd) => true;
    public static bool GetClientRect(IntPtr hWnd, out Rect rect) { rect = new Rect { Right = PlatformHost.ClientW, Bottom = PlatformHost.ClientH }; return true; }
    public static bool AdjustWindowRect(ref Rect rect, int style, bool menu) => true;
    public static IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value) => IntPtr.Zero;
    public static bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags) => true;
    public static IntPtr GetModuleHandleW(string? moduleName) => new(1);
    public static IntPtr LoadCursorW(IntPtr instance, IntPtr cursorId) => new(1);
    public static IntPtr LoadIconW(IntPtr instance, IntPtr iconId) => new(1);
    public static bool SetProcessDpiAwarenessContext(IntPtr value) => true;

    /// <summary>화면 크기 — 안드로이드 화면(또는 시험기가 정한 크기).</summary>
    public static int GetSystemMetrics(int index) => index switch
    {
        SM_CXSCREEN => PlatformHost.ScreenW,
        SM_CYSCREEN => PlatformHost.ScreenH,
        _ => 0,
    };

    /// <summary>작업 영역 — 게임은 여기서 가로 32 · 세로 100 을 빼고 창(해상도)을 맞춘다(GameWindow.FitZoom). 화면 크기가 그대로 남게 더해 둔다.</summary>
    public static bool SystemParametersInfoW(uint action, uint param, ref Rect rect, uint winIni)
    {
        rect = new Rect { Right = PlatformHost.ScreenW + 32, Bottom = PlatformHost.ScreenH + 100 };
        return true;
    }

    // ── 메시지 ───────────────────────────────────────────────────────────

    public static bool PeekMessageW(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove) => PlatformHost.TryTake(out msg);
    public static bool TranslateMessage(ref Msg msg) => true;
    public static IntPtr DispatchMessageW(ref Msg msg)
    {
        if (msg.Message == WM_QUIT) return IntPtr.Zero;
        if (msg.Message == PlatformHost.WM_APP_INVOKE) { PlatformHost.RunQueued(); return IntPtr.Zero; }
        return _proc?.Invoke(msg.Hwnd, msg.Message, msg.WParam, msg.LParam) ?? IntPtr.Zero;
    }
    public static void PostQuitMessage(int exitCode) => PlatformHost.Post(WM_QUIT, 0, 0);

    /// <summary>틀 기다림 — 스왑체인 손잡이면 다음 화면 시각(60Hz)까지 잔다. 입력이 오면 바로 깬다.</summary>
    public static uint WaitForSingleObjectEx(IntPtr handle, uint milliseconds, bool alertable)
    {
        if (handle == FrameWaitHandle) PlatformHost.WaitFrame((int)Math.Min(milliseconds, 100));
        return 0;
    }

    public static bool CloseHandle(IntPtr handle) => true;

    /// <summary>게임이 도는 동안 화면이 꺼지지 않게 — 안드로이드는 화면 켜 둠 깃발.</summary>
    public static uint SetThreadExecutionState(uint flags)
    {
        PlatformHost.KeepScreenOn((flags & ES_DISPLAY_REQUIRED) != 0);
        return ES_CONTINUOUS;
    }

    public static int LowWord(IntPtr lParam) => unchecked((short)(long)lParam);
    public static int HighWord(IntPtr lParam) => unchecked((short)((long)lParam >> 16));

    // ── 키 · 마우스 상태 ──────────────────────────────────────────────────

    public static short GetKeyState(int virtualKey) => PlatformHost.IsDown(virtualKey) ? unchecked((short)0x8000) : (short)0;

    public static bool GetCursorPos(out Point point) { (point.X, point.Y) = PlatformHost.Cursor; return true; }
    public static IntPtr WindowFromPoint(Point point) => MainWindow;
    public static bool ScreenToClient(IntPtr hWnd, ref Point point) => true;   // 창이 화면 왼쪽 위에 꽉 차 있다
    public static IntPtr SetCursor(IntPtr cursor) => IntPtr.Zero;
    public static void SetCapture(IntPtr hWnd) { }
    public static void ReleaseCapture() { }
    public static IntPtr CreateIconIndirect(ref IconInfo info) => new(1);
    public static bool DestroyCursor(IntPtr cursor) => true;
    public static IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits) => new(1);
    public static bool DeleteObject(IntPtr obj) => true;

    // 글자 조합(IME) — 안드로이드 키보드는 다 된 글자만 WM_CHAR 로 보낸다.
    public static IntPtr ImmGetContext(IntPtr hWnd) => IntPtr.Zero;
    public static bool ImmReleaseContext(IntPtr hWnd, IntPtr context) => true;
    public static int ImmGetCompositionStringW(IntPtr context, int index, char[]? buffer, int bufferBytes) => 0;
    public static string CompositionString(IntPtr hWnd) => "";

    // ── 메뉴 막대 — 안드로이드는 「메뉴」 단추가 이 나무를 보여 준다(PlatformHost.MenuTree) ──

    internal sealed class MenuNode
    {
        public readonly List<MenuItem> Items = [];
    }

    internal sealed class MenuItem
    {
        public uint Flags;
        public nuint Id;
        public string? Text;
        public MenuNode? Sub;
        public bool Checked => (Flags & MF_CHECKED) != 0;
        public bool Grayed => (Flags & MF_GRAYED) != 0;
        public bool Separator => (Flags & MF_SEPARATOR) != 0;
    }

    private static readonly ConcurrentDictionary<IntPtr, MenuNode> Menus = new();
    private static int _nextMenu = 0x3000;
    private static IntPtr _menuBar;

    /// <summary>게임이 만든 메뉴 막대(창에 붙인 것).</summary>
    internal static MenuNode? MenuBar => _menuBar != IntPtr.Zero && Menus.TryGetValue(_menuBar, out var bar) ? bar : null;

    /// <summary>그 메뉴의 손잡이 — 펼치기 전에 WM_INITMENUPOPUP 으로 게임에 알린다(체크 표시를 고친다).</summary>
    internal static IntPtr HandleOf(MenuNode node) => Menus.FirstOrDefault(kv => ReferenceEquals(kv.Value, node)).Key;

    private static IntPtr NewMenu()
    {
        var h = new IntPtr(Interlocked.Increment(ref _nextMenu));
        Menus[h] = new MenuNode();
        return h;
    }

    public static IntPtr CreateMenu() => NewMenu();
    public static IntPtr CreatePopupMenu() => NewMenu();

    public static bool AppendMenuW(IntPtr menu, uint flags, nuint idOrSubMenu, string? text)
    {
        if (!Menus.TryGetValue(menu, out var node)) return false;
        lock (node)
            node.Items.Add(new MenuItem
            {
                Flags = flags, Id = idOrSubMenu, Text = text,
                Sub = (flags & MF_POPUP) != 0 && Menus.TryGetValue((IntPtr)(nint)idOrSubMenu, out var sub) ? sub : null,
            });
        return true;
    }

    public static uint CheckMenuItem(IntPtr menu, uint id, uint check)
    {
        if (!Menus.TryGetValue(menu, out var node)) return uint.MaxValue;
        lock (node)
            foreach (var item in Flatten(node))
                if (item.Sub == null && item.Id == id)
                {
                    uint was = item.Flags & MF_CHECKED;
                    item.Flags = item.Flags & ~MF_CHECKED | (check & MF_CHECKED);
                    return was;
                }
        return uint.MaxValue;
    }

    /// <summary>메뉴와 그 아래 하위 메뉴의 모든 항목 — 원본 CheckMenuItem(MF_BYCOMMAND)도 하위 메뉴까지 찾는다.</summary>
    private static IEnumerable<MenuItem> Flatten(MenuNode node)
    {
        foreach (var item in node.Items.ToArray())
        {
            yield return item;
            if (item.Sub != null) foreach (var sub in Flatten(item.Sub)) yield return sub;
        }
    }

    public static int GetMenuItemCount(IntPtr menu) => Menus.TryGetValue(menu, out var node) ? node.Items.Count : -1;

    public static bool DeleteMenu(IntPtr menu, uint position, uint flags)
    {
        if (!Menus.TryGetValue(menu, out var node)) return false;
        lock (node)
        {
            if ((flags & MF_BYPOSITION) != 0) { if (position >= node.Items.Count) return false; node.Items.RemoveAt((int)position); return true; }
            return node.Items.RemoveAll(i => i.Id == position) > 0;
        }
    }

    public static bool SetMenu(IntPtr hWnd, IntPtr menu) { if (menu != IntPtr.Zero) _menuBar = menu; return true; }
    public static IntPtr GetMenu(IntPtr hWnd) => _menuBar;
}

/// <summary>
/// 게임(원본 코드)과 안드로이드 · 시험기 사이 — 메시지 줄, 키 · 마우스 상태, 화면 크기, 화면 내기, 창(대화 상자) 띄우기.
/// </summary>
internal static class PlatformHost
{
    /// <summary>「모니터」 크기 — 게임이 고른 해상도는 이보다 크지 않게 깎인다(GameWindow.FitZoom). 안드로이드는 실제 화면 점 수.</summary>
    public static int ScreenW = 1280, ScreenH = 960;

    /// <summary>게임이 그리는 창 크기(뒷버퍼) — 마지막 Present 의 크기.</summary>
    public static int ClientW = 1280, ClientH = 960;

    // ── 메시지 줄 ──
    private static readonly ConcurrentQueue<Win32.Msg> Queue = new();
    private static readonly SemaphoreSlim Wake = new(0);

    /// <summary>줄 자물쇠 — <see cref="Batch"/> 로 넣은 메시지 묶음은 게임이 한 틀에 다 받는다(반만 받고 갱신하지 않게).</summary>
    private static readonly object Gate = new();

    public static void Post(uint message, nint wParam, nint lParam)
    {
        lock (Gate)
        {
            var (x, y) = Cursor;
            Queue.Enqueue(new Win32.Msg
            {
                Hwnd = Win32.MainWindow, Message = message, WParam = wParam, LParam = lParam,
                Time = (uint)Environment.TickCount, Pt = new Win32.Point { X = x, Y = y },
            });
        }
        if (Wake.CurrentCount == 0) Wake.Release();
    }

    public static bool TryTake(out Win32.Msg msg)
    {
        lock (Gate) return Queue.TryDequeue(out msg);
    }

    /// <summary>
    /// 안에서 보내는 메시지를 한 묶음으로 넣는다 — 게임 스레드는 묶음을 다 넣은 뒤에야 첫 것을 꺼낸다.
    /// 터치 탭(옮기기 → 누르기 → 떼기 → 화면 밖으로)이 둘로 갈려 그 사이에 한 틀이 돌면, 커서가 창 위쪽 탭(가장자리 띠)에 머문 그 틀에
    /// 가장자리 스크롤이 맵을 16점 밀었다(사용자 보고 — 모드 창 위쪽 탭을 누르면 맵이 살짝씩 밀린다).
    /// </summary>
    public static void Batch(Action send)
    {
        lock (Gate) send();
    }

    // ── 틀 맞추기 (60Hz) ──
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _nextFrame;
    public static double FrameSeconds = 1 / 60.0;

    public static void WaitFrame(int maxMs)
    {
        double now = Clock.Elapsed.TotalSeconds;
        if (_nextFrame < now - FrameSeconds) _nextFrame = now;   // 많이 밀렸으면 따라잡지 않는다
        double wait = Math.Min(_nextFrame - now, maxMs / 1000.0);
        if (wait > 0.001 && Queue.IsEmpty) Wake.Wait(TimeSpan.FromSeconds(wait));
        _nextFrame = Math.Max(_nextFrame, now) + FrameSeconds;
    }

    // ── 키 · 마우스 ──
    private static readonly bool[] Down = new bool[256];
    public static (int X, int Y) Cursor { get; private set; }

    public static bool IsDown(int vk) => (uint)vk < 256 && Down[vk];

    private static nint Pack(int x, int y) => (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    public static void Mouse(uint message, int x, int y)
    {
        Cursor = (x, y);
        if (message == Win32.WM_LBUTTONDOWN) Down[Win32.VK_LBUTTON] = true;
        if (message == Win32.WM_LBUTTONUP) Down[Win32.VK_LBUTTON] = false;
        if (message == Win32.WM_RBUTTONDOWN) Down[0x02] = true;
        if (message == Win32.WM_RBUTTONUP) Down[0x02] = false;
        nint flags = Down[Win32.VK_LBUTTON] ? Win32.MK_LBUTTON : 0;
        Post(message, flags, Pack(x, y));
    }

    /// <summary>휠 — delta 는 120 이 한 칸(위로 +). 원본처럼 lParam 은 화면 좌표.</summary>
    public static void Wheel(int delta, int x, int y)
    {
        Cursor = (x, y);
        Post(Win32.WM_MOUSEWHEEL, (nint)((delta & 0xFFFF) << 16), Pack(x, y));
    }

    public static void Key(int vk, bool down)
    {
        if ((uint)vk >= 256) return;
        bool was = Down[vk];
        Down[vk] = down;
        // lParam 30번 비트 = 전에 눌려 있었나(되풀이), 31번 = 떼었나
        nint lParam = (was ? 1 << 30 : 0) | (down ? 0 : unchecked((int)0xC0000000));
        Post(down ? Win32.WM_KEYDOWN : Win32.WM_KEYUP, vk, lParam);
    }

    public static void Char(char c) => Post(Win32.WM_CHAR, c, 1);

    /// <summary>게임 스레드에서 할 일 — 메시지 줄로 넘겨 원본 게임 갱신과 겹치지 않게 돈다(단축 아이콘의 이전 · 다음 인물 · 배속 따위).</summary>
    public const uint WM_APP_INVOKE = 0x8101;
    private static readonly ConcurrentQueue<Action> Work = new();
    public static void RunOnGame(Action action) { Work.Enqueue(action); Post(WM_APP_INVOKE, 0, 0); }
    internal static void RunQueued() { while (Work.TryDequeue(out var a)) a(); }

    public static void Command(int id) => Post(Win32.WM_COMMAND, id & 0xFFFF, 0);

    /// <summary>앱이 뒤로 갔다 · 앞으로 왔다 — 게임은 WM_ACTIVATEAPP 로 소리 · 패드를 멈춘다(원본 처리).</summary>
    public static void Activate(bool active) => Post(Win32.WM_ACTIVATEAPP, active ? 1 : 0, 0);

    // ── 화면 내기 ──
    /// <summary>뒷버퍼를 받는 곳 — 안드로이드: 화면 뷰, 시험기: PNG. (점, 너비, 높이) — 부른 동안만 유효하니 복사해 간다.</summary>
    public static Action<uint[], int, int>? Presenter;

    public static long Frames;

    public static void Present(uint[] pixels, int w, int h)
    {
        ClientW = w;
        ClientH = h;
        Frames++;
        Presenter?.Invoke(pixels, w, h);
    }

    // ── 그 밖 ──
    public static Action<bool>? ScreenOn;
    public static void KeepScreenOn(bool on) => ScreenOn?.Invoke(on);

    /// <summary>
    /// 대화 상자(MessageBox) 띄우기 — (글, 제목, 단추 이름들) → 누른 단추 순번(−1 = 닫음). 게임 스레드에서 불리고 답이 올 때까지 기다린다.
    /// 없으면(시험기) 첫 단추.
    /// </summary>
    public static Func<string, string, string[], int>? Dialog;
}
