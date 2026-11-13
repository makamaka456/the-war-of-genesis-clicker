// 파트1 엔딩 영상(MPEG-1 · AVI)을 기기 재생기(MediaPlayer)로 게임 화면 위에 띄운다 — Platform/Replaced.cs DirectShowVideo 가 부른다.
// 누르면 끝난 것으로 본다(원본은 클릭 · Esc 로 넘긴다 — Esc 는 원본 CutscenePlayer 가 받아 Dispose 한다).
#nullable enable
using Android.Media;
using Android.Views;
using DuelDx;

namespace WarOfGenesis.Droid;

internal sealed class VideoOverlay : Java.Lang.Object, DirectShowVideo.IPlayer, ISurfaceHolderCallback
{
    private readonly Activity _activity;
    private readonly FrameLayout _root;
    private SurfaceView? _surface;
    private MediaPlayer? _player;
    private readonly ManualResetEventSlim _surfaceReady = new(false);
    private volatile bool _finished;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Finished => _finished;

    private VideoOverlay(Activity activity, FrameLayout root) { _activity = activity; _root = root; }

    /// <summary>게임 스레드에서 — 화면을 깔고, 재생기를 준비해 튼다. 못 틀면 예외.</summary>
    public static VideoOverlay Open(Activity activity, FrameLayout root, string path, float volume)
    {
        var o = new VideoOverlay(activity, root);
        activity.RunOnUiThread(() =>
        {
            o._surface = new SurfaceView(activity);
            o._surface.Holder!.AddCallback(o);
            o._surface.Click += (_, _) => o._finished = true;   // 누르면 넘긴다
            root.AddView(o._surface, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        });
        if (!o._surfaceReady.Wait(TimeSpan.FromSeconds(5))) { o.Dispose(); throw new IOException("영상 화면을 만들지 못했습니다"); }
        try
        {
            var p = o._player = new MediaPlayer();
            p.SetDataSource(path);
            p.SetSurface(o._surface!.Holder!.Surface);
            p.Error += (_, e) => { o._finished = true; e.Handled = true; };
            p.Completion += (_, _) => o._finished = true;
            p.Prepare();   // 기기가 꼴을 모르면 여기서 예외
            (o.Width, o.Height) = (p.VideoWidth, p.VideoHeight);
            if (o.Width <= 0 || o.Height <= 0) throw new IOException("영상 그림 갈래를 못 읽었습니다");
            p.SetVolume(volume, volume);
            p.Start();
            activity.RunOnUiThread(() => o.FitAspect());
            return o;
        }
        catch (Exception)
        {
            o.Dispose();
            throw;
        }
    }

    /// <summary>영상 비율대로 가운데에.</summary>
    private void FitAspect()
    {
        if (_surface == null || Width <= 0) return;
        float s = Math.Min((float)_root.Width / Width, (float)_root.Height / Height);
        var lp = new FrameLayout.LayoutParams((int)(Width * s), (int)(Height * s), GravityFlags.Center);
        _surface.LayoutParameters = lp;
    }

    public void SurfaceCreated(ISurfaceHolder holder) => _surfaceReady.Set();
    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height) { }
    public void SurfaceDestroyed(ISurfaceHolder holder) => _finished = true;

    public new void Dispose()
    {
        _finished = true;
        try { _player?.Stop(); } catch (Exception) { }
        try { _player?.Release(); } catch (Exception) { }
        _player = null;
        var surface = _surface;
        _surface = null;
        if (surface != null) _activity.RunOnUiThread(() => _root.RemoveView(surface));
    }
}
