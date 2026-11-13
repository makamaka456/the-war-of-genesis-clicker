// 윈도우 전용 원본 파일의 안드로이드 · 시험기 판(같은 이름 · 같은 모양) — 원본 파일은 링크하지 않는다(csproj Exclude).
#nullable enable
using System.Runtime.InteropServices;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// duel-dx/System/OriginalGameSetup.cs 의 판 — 원본 자리는 앱의 고르기 화면(Game/GameSetup)이 정해 환경 변수로 넘긴다.
/// 여기서는 PC 판 Ensure 의 뒤쪽(원본 폴더에서 자료 차리기)만 한다. 진행은 <see cref="Progress"/> 로 알린다.
/// </summary>
internal static class OriginalGameSetup
{
    public static bool Part1Only { get; private set; } = Environment.GetEnvironmentVariable("DUELDX_PART1ONLY") == "1";

    /// <summary>자료 차리기 진행 — (글, 끝낸 수, 전체 수). 앱이 화면에 보인다.</summary>
    public static Action<string, int, int>? Progress;

    public static bool Ensure()
    {
        if (!OriginalAssets.NeedsGame) return true;
        if (OriginalGame.Root is { } root)
        {
            int missing = OriginalAssets.Prepare(root, (done, total) => Progress?.Invoke("원본 게임 폴더에서 자료를 꺼내는 중…", done, total));
            if (missing > 0) Progress?.Invoke($"원본 게임 폴더에서 파일 {missing}개를 못 찾았습니다 — 그 그림·소리는 빠진 채로 켭니다.", 0, 0);
            return true;
        }
        if (Part1Game.Root != null)
        {
            // 파트2 없이 파트1 만 — PC 판은 원작자 릴리즈에서 파트1 바탕을 받는다(AssetDownload.PrepareBase). 사용자 결정: 폰은 인터넷으로 안 받는다.
            Part1Only = true;
            AssetDownload.PrepareBase((got, total) => Progress?.Invoke("파트1 바탕 자료를 받는 중…", (int)(got >> 10), (int)(total >> 10)));
            return true;
        }
        return false;
    }

    /// <summary>PC 판: 이 PC 에서 찾고 물어본다 — 안드로이드는 고르기 화면에서 이미 찾았다(파트1 타이틀의 「PART 2 연결」).</summary>
    internal static string? ScanAndAsk(string title, Func<string?> scan, Action<string> use, object? owner = null)
    {
        if (scan() is not { } found) return null;
        use(found);
        return found;
    }

    /// <summary>PC 판: 폴더 고르기 창 — 안드로이드는 앱의 고르기 화면에서 고른다.</summary>
    internal static string? PickFolder(string description, Func<string?, bool> valid, string needs, object? owner = null)
    {
        System.Windows.Forms.MessageBox.Show("원본 폴더는 앱 첫 화면(게임을 끝내고 다시 켜기)의 「폴더 고르기」에서 고릅니다.", "창세기전3");
        return null;
    }
}

/// <summary>duel-dx/System/PlatformInfo.cs 의 판 — 통계 줄에 적는 기기 정보.</summary>
internal static class PlatformInfo
{
    internal static (string Kind, bool Touch, string Screen) Current => ("android", true, $"{Native.PlatformHost.ScreenW}x{Native.PlatformHost.ScreenH}");
}

/// <summary>
/// duel-dx/System/MediaFoundationAudio.cs 의 판 — 파트1 시디 곡 대신 넣은 mp3 · ogg 따위를 PCM 으로 푼다.
/// 안드로이드: MediaCodec(<see cref="Decoder"/>), 시험기: 없음(wav 만).
/// </summary>
internal static class MediaFoundationAudio
{
    public static Func<string, PcmSound?>? Decoder;
    public static PcmSound? Decode(string path) => Decoder?.Invoke(path);
}

/// <summary>
/// duel-dx/System/DirectShowVideo.cs 의 판 — 파트1 엔딩 영상(MPEG · AVI)을 튼다. 원본은 DirectShow 가 그림을 넘기지만,
/// 안드로이드는 기기 재생기(MediaPlayer)가 게임 화면 위에 바로 그린다(<see cref="Player"/>). 그래서 <see cref="CopyFrame"/> 은 늘 false 다.
/// 기기가 그 꼴을 못 틀면 열기에서 COMException — 원본 CutscenePlayer 가 「엔딩 영상을 틀 수 없습니다」로 넘긴다. 시험기는 늘 못 튼다.
/// </summary>
internal sealed class DirectShowVideo : IDisposable
{
    /// <summary>재생기 하나 — 안드로이드 쪽(Droid/VideoOverlay)이 만든다.</summary>
    internal interface IPlayer : IDisposable
    {
        int Width { get; }
        int Height { get; }
        bool Finished { get; }
    }

    /// <summary>(파일, 소리 크기 0~1) → 재생기. 못 틀면 예외(사유 글).</summary>
    internal static Func<string, float, IPlayer>? Player;

    private readonly IPlayer _player;
    private DirectShowVideo(IPlayer player) { _player = player; }

    public int Width => _player.Width;
    public int Height => _player.Height;

    public static DirectShowVideo Open(string path, float volume)
    {
        if (Player == null) throw new COMException("이 판은 영상 재생기가 없습니다");
        try { return new DirectShowVideo(Player(path, volume)); }
        catch (Exception ex) when (ex is not COMException) { throw new COMException("기기가 이 영상을 틀지 못합니다: " + ex.Message); }
    }

    public bool Finished => _player.Finished;
    public bool CopyFrame(uint[] frame) => false;
    public void Dispose() => _player.Dispose();
}
