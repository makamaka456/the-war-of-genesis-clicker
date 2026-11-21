// 사용자가 뺀 기능(docs/포팅-계획.md) — 원본 파일은 링크하지 않고, 원본 코드가 부르는 이름만 같은 모양으로 둔다.
// 부르면 아무것도 안 하거나 「안드로이드 판에서는 쓰지 않습니다」를 알린다(iOS 판이 버그 리포트에서 죽던 일 — docs/iOS-포팅-참고.md i3).
#nullable enable
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>뺀 기능: 자동 업데이트(Velopack) — duel-dx/Updater.cs.</summary>
internal static class Updater
{
    internal static string Status => "";
    internal static int Progress => -1;
    internal static void CheckInBackground() { }
}

/// <summary>뺀 기능: 편집기 — duel-dx/EditorLauncher.cs.</summary>
internal static class EditorLauncher
{
    internal static void Open(Action<string> toast) => toast("안드로이드 판에는 편집기가 없습니다");
}

/// <summary>뺀 기능: 버그 리포트 — duel-dx/System/BugReport.cs. F12 · 메뉴에서 불러도 죽지 않게 알림만.</summary>
internal sealed class BugReport(GameWindow host)
{
    internal const int MaxText = 8000;
    internal static string QueueFolder => UserDataFolder.File("bug-queue");
    internal static string CrashLogPath => Path.Combine(Path.GetTempPath(), "dueldx_crash.log");
    internal static string Url => "";
    internal void Open() => host.Toast("안드로이드 판에서는 버그 리포트를 쓰지 않습니다");
    internal void HintKeyOnce() { }
    internal void Submit(string text, Dictionary<string, object?> context) { }
    internal void UploadInBackground(bool announce = false) { }
}

/// <summary>뺀 기능: 내 버그 리포트 창 — duel-dx/System/MyBugReportsDialog.cs.</summary>
internal static class MyBugReportsDialog
{
    internal static bool ShotIfAsked() => false;
}

/// <summary>뺀 기능: 고해상도 모세스 배경(ONNX DirectML) — duel-dx/Moses/HiResBackdrop.cs. 켜도 원본 배경 그대로.</summary>
internal sealed class HiResBackdrop(GameWindow host)
{
    internal static readonly int[] MosesBackgrounds = [70, 52, 12, 13, 39, 40, 42, 58, 65, 66, 67, 68, 69, 71, 72, 94];
    internal bool Enabled => false;
    internal int Made => 0;
    internal string StatusLine => "안드로이드 판에서는 쓰지 않습니다";
    internal void Start() => host.Toast("안드로이드 판에서는 고해상도 모세스 배경을 쓰지 않습니다");
    internal void Drop() { }
    internal void Mark(int id, uint[] low, int x, int y) { }
}
