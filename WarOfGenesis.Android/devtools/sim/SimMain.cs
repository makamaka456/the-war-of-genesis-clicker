// 헤드리스 시험기 — 안드로이드 APK 와 같은 소스(원본 duel-dx · WarOfGenesis.Assets + Platform 대용품)를 리눅스에서 돌린다.
// 안드로이드 자리(Droid/)만 아래로 바꾼다: 글자 · 그림 = PIL 도우미(pilserver.py), 소리 = 재생 시간만큼 기다리는 가짜, 화면 = PNG.
// 쓰기:  dotnet sim.dll <설정 파일(setup.txt)> <결과 폴더> <대본 파일>
// 대본(한 줄에 하나): wait 초 · shot 이름 · click x y · rclick x y · drag x1 y1 x2 y2 · key 이름|16진 · char 글 · wheel 칸 x y
//                   menu 명령번호 · pad 단추 초 · until 초 글(상태 줄에 그 글이 보일 때까지) · # 주석
#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuelDx;
using DuelDx.Native;
using WarOfGenesis.Assets;
using WarOfGenesis.Droid.Game;

internal static class SimMain
{
    private static uint[] _last = [];
    private static int _lastW, _lastH;
    private static readonly object Gate = new();
    private static string _out = "";

    private static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("쓰기: sim.dll <setup.txt> <결과 폴더> <대본>"); return 2; }
        _out = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(_out);
        string script = File.ReadAllText(args[2]);
        int w = int.TryParse(Environment.GetEnvironmentVariable("SIM_SCREEN_W"), out var sw) ? sw : 1280;
        int h = int.TryParse(Environment.GetEnvironmentVariable("SIM_SCREEN_H"), out var sh) ? sh : 960;
        PlatformHost.ScreenW = w;
        PlatformHost.ScreenH = h;
        TextEngine.Current = new PilText();
        TextEngine.Decoder = PilText.Decode;
        AudioSink.Open = (rate, ch) => new PacedSink(rate, ch);
        PlatformHost.Presenter = (px, pw, ph) =>
        {
            lock (Gate)
            {
                if (_last.Length != px.Length) _last = new uint[px.Length];
                px.AsSpan().CopyTo(_last);
                (_lastW, _lastH) = (pw, ph);
            }
        };
        PlatformHost.Dialog = (text, caption, buttons) => { Log($"대화 상자 [{caption}] {text.Replace('\n', ' ')} → {buttons.FirstOrDefault()}"); return 0; };

        Exception? crash = null;
        var game = new Thread(() =>
        {
            try
            {
                GameSetup.StorePath = Path.GetFullPath(args[0]);
                GameSetup.Load();
                GameSetup.Apply();
                OriginalGameSetup.Progress = (t, d, n) => { if (n == 0 || d == n || d % 500 == 0) Log($"{t} {d}/{n}"); };
                if (!OriginalGameSetup.Ensure()) throw new DirectoryNotFoundException("원본 게임 폴더를 모릅니다");
                CustomSprites.InstallExamples();
                Log("게임 시작");
                using var window = new GameWindow();
                window.Run();
                Log("게임 끝");
            }
            catch (Exception ex) { crash = ex; Log("터짐: " + ex); }
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "Game" };
        var clock = Stopwatch.StartNew();
        game.Start();

        try { RunScript(script, game, () => crash); }
        catch (Exception ex) { Log("대본 오류: " + ex.Message); crash ??= ex; }
        PlatformHost.Post(Win32.WM_QUIT, 0, 0);
        game.Join(5000);
        Log($"끝 — {clock.Elapsed.TotalSeconds:F0}초, 틀 {PlatformHost.Frames}");
        return crash == null ? 0 : 1;
    }

    private static void Log(string line) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");

    private static void RunScript(string script, Thread game, Func<Exception?> crash)
    {
        foreach (string raw in script.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (crash() != null || !game.IsAlive) { Log("게임이 멈춰 대본을 그만둔다"); return; }
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int I(int i) => int.Parse(p[i]);
            switch (p[0])
            {
                case "wait": Sleep(double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), game); break;
                case "shot": Shot(p[1]); break;
                case "click": Click(Win32.WM_LBUTTONDOWN, Win32.WM_LBUTTONUP, I(1), I(2)); break;
                case "rclick": Click(Win32.WM_RBUTTONDOWN, Win32.WM_RBUTTONUP, I(1), I(2)); break;
                case "drag":
                    PlatformHost.Mouse(Win32.WM_MOUSEMOVE, I(1), I(2));
                    PlatformHost.Mouse(Win32.WM_LBUTTONDOWN, I(1), I(2));
                    for (int k = 1; k <= 10; k++) { Thread.Sleep(30); PlatformHost.Mouse(Win32.WM_MOUSEMOVE, I(1) + (I(3) - I(1)) * k / 10, I(2) + (I(4) - I(2)) * k / 10); }
                    PlatformHost.Mouse(Win32.WM_LBUTTONUP, I(3), I(4));
                    break;
                case "move": PlatformHost.Mouse(Win32.WM_MOUSEMOVE, I(1), I(2)); break;
                case "key":
                {
                    int vk = Vk(p[1]);
                    PlatformHost.Key(vk, true); Thread.Sleep(60); PlatformHost.Key(vk, false);
                    break;
                }
                case "char": foreach (char c in line[5..]) PlatformHost.Char(c); break;
                case "wheel": PlatformHost.Wheel(120 * I(1), I(2), I(3)); break;
                case "menu": PlatformHost.Command(I(1)); break;
                case "pad":
                {
                    ushort bit = (ushort)typeof(PadState).GetField(p[1])!.GetValue(null)!;
                    PadState.Connected = true;
                    PadState.Set(bit, true);
                    Sleep(double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), game);
                    PadState.Set(bit, false);
                    break;
                }
                case "nopad": PadState.Connected = false; break;
                case "menus": DumpMenu(Win32.MenuBar, ""); break;
                case "ending":
                    PlatformHost.RunOnGame(() =>
                    {
                        var file = Part1EndingMovie.Find();
                        Log($"엔딩 영상 찾기: {file ?? "없음"} · 모드 창 글: {string.Join(" / ", Part1EndingMovie.Status().Select(t => t.Text))}");
                        if (file != null) GameWindow._active?.Cut.StartVideo(() => Part1EndingMovie.Prepare(file), played => Log($"엔딩 영상 틀기: {played}"));
                    });
                    break;
                case "movie": { int n = I(1); PlatformHost.RunOnGame(() => Log($"영상 {n} 걸기: {GameWindow._active?.Cut.StartMovie(n)}")); break; }
                case "audio": Log($"소리 장치로 나간 바이트: {PacedSink.BytesOut:N0}"); break;
                case "files": foreach (var f in Directory.EnumerateFiles(Environment.GetEnvironmentVariable("DUELDX_SAVEDIR")!, p.Length > 1 ? p[1] : "*", SearchOption.TopDirectoryOnly)) Log("  " + Path.GetFileName(f) + " " + new FileInfo(f).Length); break;
                default: Log("모르는 줄: " + line); break;
            }
            Thread.Sleep(80);
        }
    }

    private static void DumpMenu(Win32.MenuNode? node, string indent)
    {
        if (node == null) { Log("메뉴 막대 없음"); return; }
        foreach (var item in node.Items.ToArray())
        {
            Log($"{indent}{(item.Separator ? "────" : item.Text)}{(item.Sub == null && !item.Separator ? $"  [{item.Id}]" : "")}{(item.Checked ? " ✓" : "")}");
            if (item.Sub != null) DumpMenu(item.Sub, indent + "    ");
        }
    }

    private static void Sleep(double seconds, Thread game)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until && game.IsAlive) Thread.Sleep(50);
    }

    private static void Click(uint down, uint up, int x, int y)
    {
        PlatformHost.Mouse(Win32.WM_MOUSEMOVE, x, y);
        Thread.Sleep(40);
        PlatformHost.Mouse(down, x, y);
        Thread.Sleep(60);
        PlatformHost.Mouse(up, x, y);
    }

    private static int Vk(string name) => name.ToUpperInvariant() switch
    {
        "ESC" => Win32.VK_ESCAPE, "ENTER" => Win32.VK_RETURN, "SPACE" => Win32.VK_SPACE,
        "LEFT" => Win32.VK_LEFT, "RIGHT" => Win32.VK_RIGHT, "UP" => Win32.VK_UP, "DOWN" => Win32.VK_DOWN,
        "TAB" => Win32.VK_TAB, "F12" => Win32.VK_F12, "F11" => Win32.VK_F11,
        var s when s.Length == 1 => s[0],
        var s when s.StartsWith("0X") => Convert.ToInt32(s, 16),
        var s => throw new ArgumentException("모르는 키 " + s),
    };

    private static void Shot(string name)
    {
        Thread.Sleep(100);
        uint[] px;
        int w, h;
        lock (Gate) { px = (uint[])_last.Clone(); (w, h) = (_lastW, _lastH); }
        if (w == 0) { Log($"shot {name}: 아직 그린 틀이 없다"); return; }
        for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000;
        using var file = File.Create(Path.Combine(_out, name + ".png"));
        Png.Write(file, px, w, h);
        Log($"shot {name} ({w}×{h}, 틀 {PlatformHost.Frames})");
    }
}

/// <summary>재생 시간만큼 기다리는 가짜 소리 장치 — 원본 믹서가 실제처럼 돈다.</summary>
internal sealed class PacedSink(int rate, int channels) : AudioSink
{
    public static long BytesOut;
    public override void Write(byte[] data, int length)
    {
        Interlocked.Add(ref BytesOut, length);
        Thread.Sleep((int)(1000L * length / (rate * channels * 2)));
    }
    public override void Flush() { }
    public override void Dispose() { }
}

/// <summary>글자 · 그림 — PIL 도우미(pilserver.py)에 맡긴다.</summary>
internal sealed class PilText : ITextEngine
{
    private static readonly object Gate = new();
    private static Process? _proc;
    private static readonly string Tmp = Path.Combine(Path.GetTempPath(), $"sim-pil-{Environment.ProcessId}.raw");

    private static JsonNode Ask(object req)
    {
        lock (Gate)
        {
            if (_proc == null)
            {
                string script = Path.Combine(AppContext.BaseDirectory, "pilserver.py");
                _proc = Process.Start(new ProcessStartInfo("python3", ["-I", script]) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
            }
            _proc.StandardInput.WriteLine(JsonSerializer.Serialize(req));
            _proc.StandardInput.Flush();
            var res = JsonNode.Parse(_proc.StandardOutput.ReadLine()!)!;
            if (res["error"] is { } err) throw new ArgumentException("PIL: " + err);
            return res;
        }
    }

    public bool HasFace(string face) => face is not ("가을체" or "Premi");

    public SizeF Measure(string text, Font font, bool typographic)
    {
        var r = Ask(new { op = "measure", text, size = font.Size, bold = font.Bold, file = font.FontFamily.File });
        float w = (float)r["w"]!.GetValue<double>(), h = (float)r["h"]!.GetValue<double>();
        return typographic ? new SizeF(w, h) : new SizeF(w + font.Size / 3, h);
    }

    public void Draw(uint[] pixels, int w, int h, string text, Font font, Color color, float x, float y)
    {
        lock (Gate)
        {
            File.WriteAllBytes(Tmp, ToRgba(pixels, w * h));
            Ask(new { op = "draw", text, size = font.Size, bold = font.Bold, file = font.FontFamily.File, w, h, x = x + font.Size / 6, y, argb = (uint)color.ToArgb(), @out = Tmp });
            FromRgba(File.ReadAllBytes(Tmp), pixels, w * h);
        }
    }

    public static (uint[] Px, int W, int H) Decode(Stream stream)
    {
        lock (Gate)
        {
            string src = Tmp + ".src";
            using (var f = File.Create(src)) stream.CopyTo(f);
            var r = Ask(new { op = "decode", path = src, @out = Tmp });
            int w = r["w"]!.GetValue<int>(), h = r["h"]!.GetValue<int>();
            var px = new uint[w * h];
            FromRgba(File.ReadAllBytes(Tmp), px, w * h);
            return (px, w, h);
        }
    }

    private static byte[] ToRgba(uint[] px, int n)
    {
        var b = new byte[n * 4];
        for (int i = 0; i < n; i++) { uint p = px[i]; b[i * 4] = (byte)(p >> 16); b[i * 4 + 1] = (byte)(p >> 8); b[i * 4 + 2] = (byte)p; b[i * 4 + 3] = (byte)(p >> 24); }
        return b;
    }

    private static void FromRgba(byte[] b, uint[] px, int n)
    {
        for (int i = 0; i < n; i++) px[i] = (uint)b[i * 4 + 3] << 24 | (uint)b[i * 4] << 16 | (uint)b[i * 4 + 1] << 8 | b[i * 4 + 2];
    }
}
