// WarOfGenesis.Assets/AudioMixer.cs 가 부르는 winmm waveOut 함수의 안드로이드 · 시험기 판.
// 원본 믹서(섞기 · 버퍼 돌리기 · 장치 다시 열기)는 그대로 돌고, 버퍼를 받는 「장치」만 AudioSink(안드로이드: AudioTrack)로 갈아 끼운다.
//   waveOutWrite(머리) → 그 머리의 자료를 장치 줄에 넣는다 → 장치 스레드가 다 내보내면 머리 Flags 에 WHDR_DONE 을 세운다(원본 믹서가 그걸 보고 버퍼를 다시 쓴다).
#nullable enable
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WarOfGenesis.Assets;

public sealed partial class AudioMixer
{
    private const uint WhdrPrepared = 2;

    private static readonly ConcurrentDictionary<IntPtr, WaveDevice> Devices = new();
    private static int _nextDevice = 0x100;

    private static int waveOutOpen(out IntPtr hwo, uint device, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags)
    {
        hwo = IntPtr.Zero;
        var sink = AudioSink.Open?.Invoke((int)fmt.SamplesPerSec, fmt.Channels);
        if (sink == null) return 2;   // MMSYSERR_BADDEVICEID — 소리 없이 돈다
        hwo = new IntPtr(Interlocked.Increment(ref _nextDevice));
        Devices[hwo] = new WaveDevice(sink);
        return 0;
    }

    private static int waveOutClose(IntPtr hwo)
    {
        if (Devices.TryRemove(hwo, out var d)) d.Dispose();
        return 0;
    }

    private static int waveOutReset(IntPtr hwo)
    {
        if (Devices.TryGetValue(hwo, out var d)) d.Reset();
        return 0;
    }

    private static unsafe int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, uint size)
    {
        ((WaveHdr*)hdr)->Flags |= WhdrPrepared;
        return 0;
    }

    private static unsafe int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, uint size)
    {
        ((WaveHdr*)hdr)->Flags &= ~WhdrPrepared;
        return 0;
    }

    private static unsafe int waveOutWrite(IntPtr hwo, IntPtr hdr, uint size)
    {
        if (!Devices.TryGetValue(hwo, out var d)) return 5;   // MMSYSERR_INVALHANDLE
        ((WaveHdr*)hdr)->Flags &= ~WhdrDone;
        d.Enqueue(hdr);
        return 0;
    }

    /// <summary>장치 하나 — 받은 머리들을 차례로 내보내고 끝낸 것은 WHDR_DONE.</summary>
    private sealed unsafe class WaveDevice : IDisposable
    {
        private readonly AudioSink _sink;
        private readonly BlockingCollection<IntPtr> _queue = new();
        private readonly Thread _thread;
        private volatile bool _closed;
        private byte[] _buffer = [];

        public WaveDevice(AudioSink sink)
        {
            _sink = sink;
            _thread = new Thread(Run) { IsBackground = true, Name = "WaveDevice" };
            _thread.Start();
        }

        public void Enqueue(IntPtr hdr) => _queue.Add(hdr);

        private void Run()
        {
            try
            {
                foreach (var hdr in _queue.GetConsumingEnumerable())
                {
                    var h = (WaveHdr*)hdr;
                    int length = (int)h->Length;
                    if (_buffer.Length < length) _buffer = new byte[length];
                    Marshal.Copy(h->Data, _buffer, 0, length);
                    if (!_closed) _sink.Write(_buffer, length);   // 장치가 받을 수 있을 때까지 기다린다(재생 속도에 맞춰진다)
                    h->Flags |= WhdrDone;
                    if (_closed) break;
                }
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>waveOutReset — 남은 머리를 모두 끝낸 것으로.</summary>
        public void Reset()
        {
            _sink.Flush();
            while (_queue.TryTake(out var hdr)) ((WaveHdr*)hdr)->Flags |= WhdrDone;
        }

        public void Dispose()
        {
            _closed = true;
            Reset();
            _queue.CompleteAdding();
            _sink.Dispose();
        }
    }
}

/// <summary>소리를 내보내는 곳(16비트 PCM) — 안드로이드: AudioTrack. 시험기: 재생 시간만큼 기다리는 가짜.</summary>
public abstract class AudioSink : IDisposable
{
    /// <summary>(샘플레이트, 채널 수) → 장치. 없으면 소리 없음.</summary>
    public static Func<int, int, AudioSink?>? Open;

    /// <summary>바이트를 내보낸다 — 장치가 받을 수 있을 때까지 막힌다(앱이 뒤로 가 멈춘 동안도).</summary>
    public abstract void Write(byte[] data, int length);
    public abstract void Flush();
    public abstract void Dispose();
}
