// duel-dx/System/GamePad.cs 가 부르는 윈도우 함수(XInput · 커서 · 메시지)의 안드로이드 · 시험기 판.
// 원본 GamePad.cs 는 그대로 링크하고, 그 안의 DllImport 줄만 #if !ANDROID 로 가렸다 — 이 파일이 같은 이름을 채운다(partial).
// 패드 상태는 PadState(안드로이드: 실제 패드 · 시험기: 스크립트)가 XInput 꼴로 채운다 — 원본의 패드 조작이 그대로 돈다.
#nullable enable
using DuelDx.Native;

namespace DuelDx;

internal sealed partial class GamePad
{
    private static uint GetState14(uint index, out State state)
    {
        state = default;
        if (index != 0 || !PadState.Connected) return 1167;   // ERROR_DEVICE_NOT_CONNECTED
        lock (PadState.Gate)
        {
            state.Packet = PadState.Packet;
            state.Buttons = PadState.Buttons;
            state.LeftTrigger = PadState.LeftTrigger;
            state.RightTrigger = PadState.RightTrigger;
            state.LX = PadState.LX; state.LY = PadState.LY;
            state.RX = PadState.RX; state.RY = PadState.RY;
        }
        return 0;
    }

    private static uint GetState910(uint index, out State state) => GetState14(index, out state);

    /// <summary>앞에 있는 창 — 앱이 앞에 있을 때만 게임 창.</summary>
    private static IntPtr GetForegroundWindow() => PadState.AppInFront ? Win32.MainWindow : IntPtr.Zero;

    /// <summary>패드가 마우스 커서를 옮긴다 — 화면 좌표 = 창 좌표.</summary>
    private static bool SetCursorPos(int x, int y)
    {
        PlatformHost.Mouse(Win32.WM_MOUSEMOVE, x, y);
        return true;
    }

    private static bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        PlatformHost.Post(msg, wParam, lParam);
        return true;
    }

    private static bool ClientToScreen(IntPtr hWnd, ref Win32.Point point) => true;
}

/// <summary>패드 하나의 지금 상태(XInput 꼴) — 안드로이드 입력 쪽이 채운다.</summary>
internal static class PadState
{
    public static readonly object Gate = new();
    public static volatile bool Connected;
    public static volatile bool AppInFront = true;
    public static uint Packet;
    public static ushort Buttons;
    public static byte LeftTrigger, RightTrigger;
    public static short LX, LY, RX, RY;

    // XInput 단추 비트
    public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Start = 0x0010, Back = 0x0020,
        LeftThumb = 0x0040, RightThumb = 0x0080, LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

    public static void Set(ushort bit, bool down)
    {
        lock (Gate)
        {
            Buttons = down ? (ushort)(Buttons | bit) : (ushort)(Buttons & ~bit);
            Packet++;
        }
    }
}
