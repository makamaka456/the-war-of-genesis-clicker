// 「패치 내역」에 싣는 릴리즈 노트를 새 판부터 몇 대목만 남긴다 — 원본 줄바꿈(MosesScene.WrapText)은 글자를 하나 붙일 때마다 줄 전체를
// 그림으로 다시 그려 폭을 재서, 4만 자 노트 전체면 폰에서 창이 뜨기 전에 오래 멈췄다(시험기 · iOS 판 1.1.5 — docs/iOS-포팅-참고.md i4).
// 원본 코드는 그대로 두고, 앱이 꺼내 두는 노트 파일만 줄인다(Droid/GameActivity.cs ShippedFiles · devtools/sim/build.py 가 같은 규칙).
#nullable enable
namespace WarOfGenesis.Droid.Game;

public static class ReleaseNotesTrim
{
    public const int Sections = 6, MaxChars = 1800;
    public const string Tail = "\n(이전 판 내역은 PC 판 릴리즈 노트에 있습니다)\n";

    public static string Head(string text)
    {
        int at = 0, found = 0;
        while ((at = text.IndexOf("\n## ", at, StringComparison.Ordinal)) >= 0)
        {
            if (++found > Sections || at > MaxChars) return text[..at].TrimEnd() + "\n" + Tail;
            at += 4;
        }
        return text;
    }
}
