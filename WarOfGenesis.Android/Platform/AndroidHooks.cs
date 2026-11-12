// 원본 파일에 #if ANDROID 로 넣은 두 줄이 부르는 곳(사용자 요청 2026-11-12).
//  · duel-dx/System/SystemMenu.cs — 「버그 리포트」 자리의 「도구」: PC 판 메뉴 막대의 「도구」 · 「개발」 묶음을 목록 창으로(「패치 내역」은 뺀다).
//  · duel-dx/System/SettingsScreen.cs — 설정 > 화면 「배경 그림」: 검은 여백을 채울 그림 · 크기 · 투명도.
// 창은 안드로이드 쪽(Droid/)이 띄운다. 시험기에서는 목록을 찍기만 한다.
#nullable enable
using DuelDx.Native;

namespace DuelDx;

internal static class AndroidHooks
{
    /// <summary>도구 목록 한 줄 — 명령 번호(0 이면 하위 목록), 하위 목록.</summary>
    internal sealed record ToolItem(string Text, int Command, IReadOnlyList<ToolItem> Children);

    /// <summary>목록 창을 띄우는 곳(안드로이드: 가운데 목록 창). 고른 줄의 명령은 PlatformHost.Command 로 보낸다.</summary>
    internal static Action<IReadOnlyList<ToolItem>>? ShowTools;

    /// <summary>배경 그림 창을 띄우는 곳 · 지금 그림 이름.</summary>
    internal static Action? ShowBackdrop;
    internal static Func<string>? BackdropNameOf;

    /// <summary>게임 스레드(시스템 메뉴 누름)에서 불린다 — 메뉴 막대의 하위 메뉴들을 펼친 것처럼 알려(동적 목록 「다녀온 장소」를 채운다) 목록을 만든다.</summary>
    internal static void OpenTools(GameWindow host)
    {
        if (Win32.MenuBar is not { } bar) return;
        foreach (var sub in Subs(bar)) host.WndProc(host._hwnd, Win32.WM_INITMENUPOPUP, Win32.HandleOf(sub), 0);
        var items = new List<ToolItem>();
        foreach (var top in bar.Items.ToArray())
            if (top.Sub != null) items.AddRange(Build(top.Sub));   // 「도구」 · 「개발」 (모드 · 설정은 시스템 메뉴에 이미 있다)
        if (ShowTools != null) ShowTools(items);
        else foreach (var i in items) Console.WriteLine($"도구 목록: {i.Text} [{i.Command}] {(i.Children.Count > 0 ? $"하위 {i.Children.Count}" : "")}");
    }

    private static IEnumerable<Win32.MenuNode> Subs(Win32.MenuNode node)
    {
        foreach (var item in node.Items.ToArray())
            if (item.Sub != null) { yield return item.Sub; foreach (var s in Subs(item.Sub)) yield return s; }
    }

    private static List<ToolItem> Build(Win32.MenuNode node)
    {
        var list = new List<ToolItem>();
        foreach (var item in node.Items.ToArray())
        {
            if (item.Separator || item.Text == null) continue;
            if (item.Sub == null && (int)item.Id == ReleaseNotesScreen.MenuNotes) continue;   // 패치 내역 — 사용자가 뺐다
            // 「캐릭터 에디터(&C)...」 → 「캐릭터 에디터」 — PC 키보드 단축 글자 · 「...」를 뗀다
            string text = System.Text.RegularExpressions.Regex.Replace(item.Text, @"\(&?[A-Za-z0-9]\)", "").Replace("&", "").Replace("...", "").Trim();
            list.Add(item.Sub != null ? new ToolItem(text, 0, Build(item.Sub)) : new ToolItem(text, (int)item.Id, []));
        }
        return list;
    }

    internal static string BackdropName() => BackdropNameOf?.Invoke() ?? "없음";

    internal static void OpenBackdrop(GameWindow host) => ShowBackdrop?.Invoke();
}
