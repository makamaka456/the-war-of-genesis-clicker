// 앱 다시 켜기 — 게임 스레드가 끝났거나(게임 안 「종료」) 원본 폴더를 새로 골랐으면 프로세스를 통째로 새로 띄운다.
// 원본 게임은 정적 상태(창 · 메시지 줄 · 설정 · 자료 캐시)를 많이 쥐고 있어 같은 프로세스에서 두 번 켜면 안 된다.
#nullable enable
using Android.Content;

namespace WarOfGenesis.Droid;

/// <summary>
/// 따로 도는 프로세스(:restart)의 빈 화면 — 옛 프로세스를 끝내고 첫 화면(런처)을 새 프로세스로 띄운 뒤 저도 끝난다(ProcessPhoenix 방식).
/// 같은 프로세스에서 런처를 띄우면 옛 게임이 쥔 정적 상태가 그대로 남는다.
/// </summary>
[Activity(Process = ":restart", Theme = "@android:style/Theme.Translucent.NoTitleBar", NoHistory = true, Exported = false, ExcludeFromRecents = true)]
public class RestartActivity : Activity
{
    private const string ExtraPid = "pid";

    /// <summary>지금 프로세스를 끝내고 런처를 새로 띄운다 — <paramref name="setup"/> 이면 고르기 화면에 머문다(바로 게임으로 안 간다).</summary>
    public static void Run(Activity from, bool setup)
    {
        var intent = new Intent(from, typeof(RestartActivity)).AddFlags(ActivityFlags.NewTask)
            .PutExtra(ExtraPid, Android.OS.Process.MyPid()).PutExtra(MainActivity.ExtraSetup, setup);
        KeepAliveService.Stop(from);
        from.StartActivity(intent);
        from.FinishAffinity();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        int pid = Intent!.GetIntExtra(ExtraPid, 0);
        if (pid != 0 && pid != Android.OS.Process.MyPid()) Android.OS.Process.KillProcess(pid);   // 아직 살아 있으면
        var next = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        if (Intent.GetBooleanExtra(MainActivity.ExtraSetup, false)) next.PutExtra(MainActivity.ExtraSetup, true);
        StartActivity(next);
        Finish();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
    }
}
