// 대용품(Platform/)이 부르는 안드로이드 쪽 — 글자(Paint), 그림 읽기(BitmapFactory), 소리 장치(AudioTrack), mp3 · ogg 풀기(MediaCodec).
#nullable enable
using Android.Graphics;
using Android.Media;
using WarOfGenesis.Assets;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using SizeF = System.Drawing.SizeF;
using Bitmap = Android.Graphics.Bitmap;
using Stream = System.IO.Stream;

namespace WarOfGenesis.Droid;

internal static class AndroidPlatform
{
    public static void Install()
    {
        System.Drawing.TextEngine.Current = new AndroidText();
        System.Drawing.TextEngine.Decoder = DecodeImage;
        AudioSink.Open = (rate, channels) => AndroidAudio.TryOpen(rate, channels);
        DuelDx.MediaFoundationAudio.Decoder = AndroidMedia.Decode;
    }

    private static (uint[] Px, int W, int H) DecodeImage(Stream stream)
    {
        var options = new BitmapFactory.Options { InPreferredConfig = Bitmap.Config.Argb8888, InPremultiplied = false };
        using var bmp = BitmapFactory.DecodeStream(stream, null, options) ?? throw new ArgumentException("그림을 읽지 못했습니다");
        int w = bmp.Width, h = bmp.Height;
        var px = new int[w * h];
        bmp.GetPixels(px, 0, w, 0, 0, w, h);
        return ((uint[])(object)px, w, h);
    }
}

/// <summary>
/// 글자 — PC 판은 시스템 글꼴(맑은 고딕 · 굴림 · 궁서 · 바탕)을 GDI+ 로 굽는다. 안드로이드에는 그 글꼴이 없어 기기의 기본 한글 글꼴로 굽는다
/// (굴림 · 맑은 고딕 → 고딕, 궁서 · 바탕 → 명조). 파트1 의 가을체 · Premi 는 원본 파트1 폴더의 Font\G3Han.ttf · G3Eng.ttf 를 그대로 읽는다.
/// </summary>
internal sealed class AndroidText : System.Drawing.ITextEngine
{
    private readonly Dictionary<(string Face, string? File, bool Bold), Typeface> _faces = [];
    private readonly object _gate = new();

    public bool HasFace(string face) => face is not ("가을체" or "Premi");

    private Typeface FaceOf(Font font)
    {
        var key = (font.FontFamily.Name, font.FontFamily.File, font.Bold);
        if (_faces.TryGetValue(key, out var tf)) return tf;
        Typeface baseFace;
        if (font.FontFamily.File is { } file)
        {
            try { baseFace = Typeface.CreateFromFile(file) ?? Typeface.Default!; }
            catch (Exception) { baseFace = Typeface.Default!; }
        }
        else baseFace = font.FontFamily.Name is "궁서" or "궁서체" or "바탕" or "바탕체" or "Batang" or "Gungsuh" ? Typeface.Serif! : Typeface.SansSerif!;
        return _faces[key] = Typeface.Create(baseFace, font.Bold ? TypefaceStyle.Bold : TypefaceStyle.Normal)!;
    }

    private Paint PaintFor(Font font, Color color) => new()
    {
        AntiAlias = true,
        TextSize = font.Size,
        Color = new Android.Graphics.Color(color.R, color.G, color.B, color.A),
    };

    public SizeF Measure(string text, Font font, bool typographic)
    {
        lock (_gate)
        {
            using var paint = PaintFor(font, Color.White);
            paint.SetTypeface(FaceOf(font));
            float w = paint.MeasureText(text);
            var fm = paint.GetFontMetrics()!;
            float h = fm.Descent - fm.Ascent;
            // GDI+ MeasureString(보통 꼴)은 앞뒤에 글자 크기의 1/6 쯤 여백을 둔다 — 활자 꼴(typographic)은 여백이 없다.
            return typographic ? new SizeF(w, h) : new SizeF(w + font.Size / 3, h);
        }
    }

    public void Draw(uint[] pixels, int w, int h, string text, Font font, Color color, float x, float y)
    {
        lock (_gate)
        {
            using var bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
            bmp.SetPixels((int[])(object)pixels, 0, w, 0, 0, w, h);
            using (var canvas = new Canvas(bmp))
            using (var paint = PaintFor(font, color))
            {
                paint.SetTypeface(FaceOf(font));
                var fm = paint.GetFontMetrics()!;
                // GDI+ 는 (x, y) 가 글 상자 왼쪽 위 — 보통 꼴은 앞에 글자 크기의 1/6 여백.
                canvas.DrawText(text, x + font.Size / 6, y - fm.Ascent, paint);
            }
            var tmp = new int[w * h];
            bmp.GetPixels(tmp, 0, w, 0, 0, w, h);
            tmp.AsSpan().CopyTo((int[])(object)pixels);
        }
    }
}

/// <summary>소리 장치 — AudioTrack(스트리밍, 16비트). 앱이 뒤로 가면 멈춘다(iOS 판 1.1.6 — docs/iOS-포팅-참고.md i1).</summary>
internal sealed class AndroidAudio : AudioSink
{
    private static readonly List<AndroidAudio> Open = [];
    private static volatile bool _paused;
    private static readonly ManualResetEventSlim Running = new(true);

    private readonly AudioTrack _track;

    private AndroidAudio(AudioTrack track) { _track = track; }

    public static AndroidAudio? TryOpen(int rate, int channels)
    {
        try
        {
            var mask = channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
            int min = AudioTrack.GetMinBufferSize(rate, mask, Encoding.Pcm16bit);
            var track = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game)!.SetContentType(AudioContentType.Music)!.Build()!)!
                .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit)!.SetSampleRate(rate)!.SetChannelMask(mask)!.Build()!)!
                .SetBufferSizeInBytes(Math.Max(min * 2, rate * channels * 2 / 10))!
                .SetTransferMode(AudioTrackMode.Stream)!
                .Build()!;
            if (!_paused) track.Play();
            var sink = new AndroidAudio(track);
            lock (Open) Open.Add(sink);
            return sink;
        }
        catch (Exception) { return null; }
    }

    /// <summary>앱이 뒤로 갔다(true) · 돌아왔다(false).</summary>
    public static void Pause(bool paused)
    {
        _paused = paused;
        lock (Open)
            foreach (var s in Open)
                try { if (paused) s._track.Pause(); else s._track.Play(); } catch (Exception) { }
        if (paused) Running.Reset(); else Running.Set();
    }

    public override void Write(byte[] data, int length)
    {
        Running.Wait();
        int at = 0;
        while (at < length)
        {
            int n = _track.Write(data, at, length - at);
            if (n <= 0) { if (_paused) Running.Wait(); else return; }
            else at += n;
        }
    }

    public override void Flush() { try { _track.Flush(); } catch (Exception) { } }

    public override void Dispose()
    {
        lock (Open) Open.Remove(this);
        try { _track.Stop(); _track.Release(); } catch (Exception) { }
    }
}

/// <summary>mp3 · ogg 따위를 PCM 으로 — 파트1 시디 곡을 대신하는 파일(Part1CdMusic 이 MediaFoundationAudio.Decode 로 부른다).</summary>
internal static class AndroidMedia
{
    public static PcmSound? Decode(string path)
    {
        using var extractor = new MediaExtractor();
        try { extractor.SetDataSource(path); }
        catch (Exception) { return null; }
        int track = -1;
        MediaFormat? format = null;
        for (int i = 0; i < extractor.TrackCount; i++)
        {
            var f = extractor.GetTrackFormat(i);
            if (f.GetString(MediaFormat.KeyMime)?.StartsWith("audio/") == true) { track = i; format = f; break; }
        }
        if (track < 0 || format == null) return null;
        extractor.SelectTrack(track);
        using var codec = MediaCodec.CreateDecoderByType(format.GetString(MediaFormat.KeyMime)!);
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();
        int rate = format.GetInteger(MediaFormat.KeySampleRate), channels = format.GetInteger(MediaFormat.KeyChannelCount);
        var samples = new List<short>(rate * channels * 60);
        var info = new MediaCodec.BufferInfo();
        bool inputDone = false;
        try
        {
            while (true)
            {
                if (!inputDone)
                {
                    int inIndex = codec.DequeueInputBuffer(10000);
                    if (inIndex >= 0)
                    {
                        var buf = codec.GetInputBuffer(inIndex)!;
                        int size = extractor.ReadSampleData(buf, 0);
                        if (size < 0) { codec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); inputDone = true; }
                        else { codec.QueueInputBuffer(inIndex, 0, size, extractor.SampleTime, 0); extractor.Advance(); }
                    }
                }
                int outIndex = codec.DequeueOutputBuffer(info, 10000);
                if (outIndex == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    var of = codec.OutputFormat;
                    rate = of.GetInteger(MediaFormat.KeySampleRate);
                    channels = of.GetInteger(MediaFormat.KeyChannelCount);
                }
                else if (outIndex >= 0)
                {
                    var buf = codec.GetOutputBuffer(outIndex)!;
                    var bytes = new byte[info.Size];
                    buf.Position(info.Offset);
                    buf.Get(bytes, 0, info.Size);
                    for (int i = 0; i + 1 < bytes.Length; i += 2) samples.Add((short)(bytes[i] | bytes[i + 1] << 8));
                    codec.ReleaseOutputBuffer(outIndex, false);
                    if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) break;
                }
            }
        }
        finally { codec.Stop(); }
        return samples.Count == 0 ? null : new PcmSound([.. samples], rate, channels);
    }
}
