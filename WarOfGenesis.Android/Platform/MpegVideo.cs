// 파트1 엔딩 영상(MPEG-1) 재생기 — 원본이 DirectShow 에 맡기던 것을 앱 안의 해독기(native/wogmpeg.c · pl_mpeg)로 푼다(사용자 결정 2026-11-13).
// 원본 CutscenePlayer 는 DirectShowVideo 에서 그림을 받아(CopyFrame) 제 화면에 그리므로, 여기서는 그림을 넘기고 소리는 소리 장치(AudioSink)로 낸다.
// 시계는 소리 — 소리 장치가 받아 간 만큼이 지금 시각이다(소리가 없으면 실제 시간).
#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using WarOfGenesis.Assets;

namespace DuelDx;

internal sealed unsafe class MpegVideo : DirectShowVideo.IPlayer
{
    [DllImport("wogmpeg")] private static extern IntPtr wog_open(string path, out int width, out int height, out double fps, out int samplerate, out double duration);
    [DllImport("wogmpeg")] private static extern int wog_video(IntPtr p, uint* argb, out double time);
    [DllImport("wogmpeg")] private static extern int wog_audio(IntPtr p, short* pcm, out double time);
    [DllImport("wogmpeg")] private static extern void wog_close(IntPtr p);

    private IntPtr _handle;
    private readonly int _rate;
    private readonly float _volume;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly Queue<(double Time, uint[] Px)> _frames = new();
    private readonly Stack<uint[]> _free = new();
    private volatile bool _stop, _videoDone, _audioDone;
    private long _samplesOut;
    private readonly Stopwatch _clock = new();
    private AudioSink? _sink;

    public int Width { get; }
    public int Height { get; }
    public bool Finished => _stop || (_videoDone && _frames.Count == 0 && (_audioDone || _rate == 0));

    private MpegVideo(IntPtr handle, int w, int h, int rate, float volume)
    {
        _handle = handle;
        (Width, Height, _rate, _volume) = (w, h, rate, volume);
        _thread = new Thread(Run) { IsBackground = true, Name = "MpegVideo" };
    }

    public static MpegVideo Open(string path, float volume)
    {
        IntPtr h = wog_open(path, out int w, out int ht, out _, out int rate, out _);
        if (h == IntPtr.Zero) throw new InvalidDataException("MPEG 영상을 열지 못했습니다");
        var v = new MpegVideo(h, w, ht, rate, volume);
        v._sink = rate > 0 ? AudioSink.Open?.Invoke(rate, 2) : null;
        v._clock.Start();
        v._thread.Start();
        return v;
    }

    /// <summary>지금 시각(초) — 소리 장치가 받아 간 표본으로, 없으면 실제 시간.</summary>
    private double Now => _sink != null && !_audioDone ? Interlocked.Read(ref _samplesOut) / (double)_rate : _clock.Elapsed.TotalSeconds;

    /// <summary>풀기 실 — 소리 덩이를 내보내며(소리 장치가 막아 실제 속도로 간다), 그 앞 0.3초까지 그림을 풀어 둔다.</summary>
    private void Run()
    {
        var pcm = new short[1152 * 2];
        try
        {
            while (!_stop)
            {
                // 그림을 소리보다 조금 앞까지 — 쌓인 그림은 8장까지
                while (!_stop && !_videoDone && _frames.Count < 8)
                {
                    uint[] px;
                    lock (_gate) px = _free.Count > 0 ? _free.Pop() : new uint[Width * Height];
                    double t;
                    int ok;
                    fixed (uint* p = px) ok = wog_video(_handle, p, out t);
                    if (ok == 0) { _videoDone = true; break; }
                    lock (_gate) _frames.Enqueue((t, px));
                    if (t > Now + 0.3) break;
                }
                if (_sink != null && !_audioDone)
                {
                    int n;
                    fixed (short* p = pcm) n = wog_audio(_handle, p, out _);
                    if (n == 0) { _audioDone = true; _clock.Restart(); _clockBase = Interlocked.Read(ref _samplesOut) / (double)_rate; continue; }
                    if (_volume < 0.999f) for (int i = 0; i < n * 2; i++) pcm[i] = (short)(pcm[i] * _volume);
                    var bytes = new byte[n * 4];
                    Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                    _sink.Write(bytes, bytes.Length);   // 실제 재생 속도로 막힌다
                    Interlocked.Add(ref _samplesOut, n);
                }
                else
                {
                    if (_videoDone) break;
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception) { _stop = true; }
    }

    private double _clockBase;

    /// <summary>원본 CutscenePlayer 가 틀마다 부른다 — 지금 시각까지의 가장 늦은 그림을 넘긴다. 새 그림이 없으면 false.</summary>
    public bool CopyFrame(uint[] frame)
    {
        double now = _audioDone ? _clockBase + _clock.Elapsed.TotalSeconds : Now;
        uint[]? pick = null;
        lock (_gate)
        {
            while (_frames.Count > 0 && _frames.Peek().Time <= now)
            {
                if (pick != null) _free.Push(pick);
                pick = _frames.Dequeue().Px;
            }
        }
        if (pick == null) return false;
        pick.AsSpan(0, Math.Min(pick.Length, frame.Length)).CopyTo(frame);
        lock (_gate) _free.Push(pick);
        return true;
    }

    public void Dispose()
    {
        _stop = true;
        _sink?.Flush();
        _thread.Join(2000);
        _sink?.Dispose();
        _sink = null;
        if (_handle != IntPtr.Zero) { wog_close(_handle); _handle = IntPtr.Zero; }
    }
}
