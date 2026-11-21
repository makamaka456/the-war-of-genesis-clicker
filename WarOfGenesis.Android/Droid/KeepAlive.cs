// 메모리 · 백그라운드 종료 줄이기(사용자 결정 2026-11-13 — 측정: 시험기 전투 중 365~382MB, 가장 센 정리 뒤 246~297MB).
//  ① 앱을 뒤로 내리면(OnPause · OnTrimMemory) 다시 만들 수 있는 글자 그림을 비우고 가장 센 정리(GC)를 한다.
//  ② 평소에도 힙이 커지면 일찍 정리하게 한다(Mono GC soft-heap-limit — Environment.txt).
//  ③ 게임이 도는 동안 알림줄에 「창세기전3 실행 중」(포그라운드 서비스, 미디어 재생 종류) — 안드로이드가 뒤의 앱을 닫을 때 거의 고르지 않는다.
#nullable enable
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using DuelDx.Native;

namespace WarOfGenesis.Droid;

internal static class MemoryTrim
{
    /// <summary>① — 게임 스레드에서 글자 그림 캐시를 비우고, 어느 스레드에서든 가장 센 정리.</summary>
    public static void Trim()
    {
        PlatformHost.RunOnGame(() =>
        {
            if (GameActivity.Game is { } g) g._textCache.Clear();   // 다시 쓸 때 다시 굽는다(TextRaster)
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
    }
}

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
internal sealed class KeepAliveService : Service
{
    private const int NoteId = 3001;
    private const string Channel = "game";

    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(KeepAliveService));
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
            else context.StartService(intent);
        }
        catch (Exception ex) { CrashLog.Write("알림줄 서비스 시작", ex); }
    }

    public static void Stop(Context context) => context.StopService(new Intent(context, typeof(KeepAliveService)));

    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>
    /// 최근 앱 목록에서 밀어 닫았다(「모두 닫기」 포함) — 게임까지 끝낸다(사용자 요청 2026-11-13). 이 서비스는 다른 앱에 잠깐 다녀오는 동안
    /// 게임을 살려 두려는 것이다. 예전에는 밀어 닫아도 서비스가 프로세스를 붙잡아 게임 안 「종료」나 설정의 강제 중지로만 꺼졌다.
    /// </summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        base.OnTaskRemoved(rootIntent);
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "게임 실행 중", NotificationImportance.Low) { Description = "게임이 도는 동안 안드로이드가 앱을 닫지 않게 한다" });
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(GameActivity)).AddFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
#pragma warning disable CA1422
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, Channel) : new Notification.Builder(this);
#pragma warning restore CA1422
        var note = builder.SetContentTitle("창세기전3 실행 중")!
            .SetContentText("누르면 게임으로 돌아갑니다")!
            .SetSmallIcon(Resources!.GetIdentifier("appicon", "mipmap", PackageName))!
            .SetContentIntent(open)!
            .SetOngoing(true)!
            .Build()!;
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NoteId, note, ForegroundService.TypeMediaPlayback);
        else StartForeground(NoteId, note);
        // 다시 띄우지 않는다 — 게임을 끝내 프로세스를 닫은 뒤에 알림만 살아나 「실행 중」으로 남으면 안 된다(Droid/Restart.cs).
        return StartCommandResult.NotSticky;
    }
}
