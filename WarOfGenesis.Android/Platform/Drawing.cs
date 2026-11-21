// System.Drawing(GDI+, 윈도우 전용)의 아주 작은 대용품 — 원본이 쓰는 것만:
//  · TextRaster(duel-dx/TextRaster.cs): 글꼴 · 글 재기 · 글 그리기 → 글자 그리기는 TextEngine(안드로이드: Paint, 시험기: 따로)이 한다.
//  · 화면 찍기 몇 곳: Bitmap + LockBits + Save(PNG) → 점 배열과 PNG 쓰개(아래)로.
// Color · Rectangle · Point · Size · SizeF 는 .NET 의 System.Drawing.Primitives 에 있어 그대로 쓴다.
#nullable enable
using System.Drawing.Imaging;
using System.IO.Compression;

namespace System.Drawing
{
    public enum FontStyle { Regular = 0, Bold = 1, Italic = 2 }
    public enum GraphicsUnit { Pixel = 2, Point = 3 }

    /// <summary>글자를 재고 그리는 쪽 — 안드로이드 · 시험기가 넣는다.</summary>
    public interface ITextEngine
    {
        /// <summary>글꼴 이름이 이 기기에 있나(없으면 FontFamily 가 ArgumentException — GDI+ 처럼).</summary>
        bool HasFace(string face);
        /// <summary>글 한 줄의 폭 · 높이(점).</summary>
        SizeF Measure(string text, Font font, bool typographic);
        /// <summary>글 한 줄을 점 배열(ARGB, 너비 w)에 (x, y) 를 왼쪽 위로 그린다(안티에일리어스, 알파 섞기).</summary>
        void Draw(uint[] pixels, int w, int h, string text, Font font, Color color, float x, float y);
    }

    public static class TextEngine
    {
        public static ITextEngine? Current;

        /// <summary>그림 파일(PNG · BMP · JPG) 읽개 — 안드로이드: BitmapFactory, 시험기: 따로. (ARGB 점, 너비, 높이).</summary>
        public static Func<Stream, (uint[] Px, int W, int H)>? Decoder;
        internal static ITextEngine Need => Current ?? throw new InvalidOperationException("글자 엔진이 없습니다(TextEngine.Current).");

        private static readonly Dictionary<(char C, string Face, string? File, float Size, bool Bold), float> Advance = [];
        private static readonly Dictionary<(string Face, string? File, float Size, bool Bold), float> LineHeight = [];

        /// <summary>
        /// 글 재기 — 글자마다 한 번 잰 폭을 기억해 더한다. 원본의 줄바꿈(WrapText)은 글자를 하나 붙일 때마다 줄 전체를 다시 재는데,
        /// 그때마다 글꼴 엔진을 부르면 폰에서 「패치 내역」(4만 자)이 몇 분씩 멈췄다(iOS 판 1.1.5 — docs/iOS-포팅-참고.md i4).
        /// </summary>
        internal static SizeF Measure(string text, Font font, bool typographic)
        {
            var key = (font.FontFamily.Name, font.FontFamily.File, font.Size, font.Bold);
            float w = 0, h;
            lock (Advance)
            {
                foreach (char c in text)
                {
                    if (!Advance.TryGetValue((c, key.Name, key.File, key.Size, key.Bold), out float a))
                        Advance[(c, key.Name, key.File, key.Size, key.Bold)] = a = Need.Measure(c.ToString(), font, typographic: true).Width;
                    w += a;
                }
                if (!LineHeight.TryGetValue(key, out h)) LineHeight[key] = h = Need.Measure("가", font, typographic: true).Height;
            }
            return typographic ? new SizeF(w, h) : new SizeF(w + font.Size / 3, h);
        }
    }

    public sealed class FontFamily : IDisposable
    {
        public string Name { get; }
        /// <summary>파일에서 읽은 글꼴이면 그 자리(PrivateFontCollection).</summary>
        public string? File { get; }

        public FontFamily(string name)
        {
            if (!TextEngine.Need.HasFace(name)) throw new ArgumentException($"글꼴 '{name}' 이 없습니다.");
            Name = name;
        }

        internal FontFamily(string name, string file) { Name = name; File = file; }

        public bool IsStyleAvailable(FontStyle style) => true;
        public void Dispose() { }
    }

    public sealed class Font : IDisposable
    {
        public FontFamily FontFamily { get; }
        public float Size { get; }
        public FontStyle Style { get; }
        public bool Bold => (Style & FontStyle.Bold) != 0;

        public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit)
        {
            FontFamily = family;
            Size = unit == GraphicsUnit.Point ? emSize * 96 / 72 : emSize;
            Style = style;
        }

        public void Dispose() { }
    }

    public abstract class Brush : IDisposable { public void Dispose() { } }

    public sealed class SolidBrush(Color color) : Brush
    {
        public Color Color { get; } = color;
    }

    [Flags] public enum StringFormatFlags { MeasureTrailingSpaces = 0x800 }

    public sealed class StringFormat : ICloneable, IDisposable
    {
        public StringFormatFlags FormatFlags { get; set; }
        internal bool Typographic { get; init; }
        public static StringFormat GenericTypographic => new() { Typographic = true };
        public object Clone() => new StringFormat { FormatFlags = FormatFlags, Typographic = Typographic };
        public void Dispose() { }
    }

    public abstract class Image : IDisposable
    {
        public abstract int Width { get; }
        public abstract int Height { get; }
        public virtual void Dispose() { }
    }

    /// <summary>점 배열 그림(ARGB). LockBits 는 그 배열을 고정해 돌려준다.</summary>
    public sealed class Bitmap : Image
    {
        internal readonly uint[] Pixels;
        private readonly int _w, _h;

        public Bitmap(int width, int height) : this(width, height, PixelFormat.Format32bppArgb) { }

        /// <summary>그림 파일을 읽는다 — 못 읽으면 ArgumentException(GDI+ 처럼).</summary>
        public Bitmap(Stream stream)
        {
            var decoder = TextEngine.Decoder ?? throw new ArgumentException("그림 읽개가 없습니다");
            (uint[] px, _w, _h) = decoder(stream);
            if (px.Length < _w * _h || _w <= 0 || _h <= 0) throw new ArgumentException("그림을 읽지 못했습니다");
            Pixels = GC.AllocateArray<uint>(_w * _h, pinned: true);
            px.AsSpan(0, _w * _h).CopyTo(Pixels);
        }

        public Bitmap(string path) : this(OpenRead(path)) { }

        private static Stream OpenRead(string path)
        {
            try { return new MemoryStream(File.ReadAllBytes(path)); }
            catch (IOException ex) { throw new ArgumentException(ex.Message, ex); }
        }

        public Bitmap(int width, int height, PixelFormat format)
        {
            _w = Math.Max(1, width);
            _h = Math.Max(1, height);
            Pixels = GC.AllocateArray<uint>(_w * _h, pinned: true);
        }

        public override int Width => _w;
        public override int Height => _h;

        public unsafe BitmapData LockBits(Rectangle rect, ImageLockMode mode, PixelFormat format)
        {
            fixed (uint* p = Pixels)
                return new BitmapData { Scan0 = (IntPtr)(p + rect.Y * _w + rect.X), Stride = _w * 4, Width = rect.Width, Height = rect.Height };
        }

        public void UnlockBits(BitmapData data) { }

        public void Save(string path, ImageFormat format)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            Png.Write(file, Pixels, _w, _h);
        }

        public void Save(Stream stream, ImageFormat format) => Png.Write(stream, Pixels, _w, _h);
    }

    public sealed class Graphics : IDisposable
    {
        private readonly Bitmap _target;
        private Graphics(Bitmap target) { _target = target; }

        public static Graphics FromImage(Image image) => new((Bitmap)image);

        public System.Drawing.Text.TextRenderingHint TextRenderingHint { get; set; }

        public void Clear(Color color) => _target.Pixels.AsSpan().Fill((uint)color.ToArgb());

        public SizeF MeasureString(string text, Font font) => TextEngine.Measure(text, font, typographic: false);

        public SizeF MeasureString(string text, Font font, int width, StringFormat format) => TextEngine.Measure(text, font, format.Typographic);

        public void DrawString(string text, Font font, Brush brush, float x, float y) =>
            TextEngine.Need.Draw(_target.Pixels, _target.Width, _target.Height, text, font, ((SolidBrush)brush).Color, x, y);

        public void Dispose() { }
    }

    /// <summary>아주 작은 PNG 쓰개(RGBA 8비트, 필터 없음) — 화면 찍기용.</summary>
    public static class Png
    {
        public static void Write(Stream output, uint[] argb, int w, int h)
        {
            output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, (uint)w);
            BigEndian(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 6;   // 8비트 RGBA
            Chunk(output, "IHDR", ihdr);
            using var raw = new MemoryStream();
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + w * 4];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        uint p = argb[y * w + x];
                        row[1 + x * 4] = (byte)(p >> 16);
                        row[2 + x * 4] = (byte)(p >> 8);
                        row[3 + x * 4] = (byte)p;
                        row[4 + x * 4] = (byte)(p >> 24);
                    }
                    z.Write(row);
                }
            }
            Chunk(output, "IDAT", raw.ToArray());
            Chunk(output, "IEND", []);
        }

        private static void BigEndian(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            BigEndian(head, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head);
            s.Write(data);
            uint crc = Crc(head.AsSpan(4), 0xFFFFFFFF);
            crc = Crc(data, crc) ^ 0xFFFFFFFF;
            var tail = new byte[4];
            BigEndian(tail, 0, crc);
            s.Write(tail);
        }

        private static uint[]? _table;

        private static uint Crc(ReadOnlySpan<byte> data, uint crc)
        {
            _table ??= Enumerable.Range(0, 256).Select(n =>
            {
                uint c = (uint)n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                return c;
            }).ToArray();
            foreach (byte b in data) crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}

namespace System.Drawing.Imaging
{
    public enum PixelFormat { Format32bppArgb = 2498570, Format32bppRgb = 139273, Format24bppRgb = 137224 }
    public enum ImageLockMode { ReadOnly = 1, WriteOnly = 2, ReadWrite = 3 }

    public sealed class BitmapData
    {
        public IntPtr Scan0 { get; set; }
        public int Stride { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class ImageFormat
    {
        private ImageFormat() { }
        public static ImageFormat Png { get; } = new();
        public static ImageFormat Bmp { get; } = new();
    }
}

namespace System.Drawing.Text
{
    public enum TextRenderingHint { SystemDefault = 0, AntiAlias = 4, AntiAliasGridFit = 3, ClearTypeGridFit = 5 }

    /// <summary>파일에서 읽는 글꼴(파트1 Font\G3Han.ttf · G3Eng.ttf) — 이름은 TextRaster 가 찾는 이름(가을체 · Premi)으로 둔다.</summary>
    public sealed class PrivateFontCollection : IDisposable
    {
        private readonly List<FontFamily> _families = [];
        private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase) { ["G3Han.ttf"] = "가을체", ["G3Eng.ttf"] = "Premi" };

        public void AddFontFile(string path)
        {
            string name = KnownNames.TryGetValue(Path.GetFileName(path), out var n) ? n : Path.GetFileNameWithoutExtension(path);
            _families.Add(new FontFamily(name, path));
        }

        public FontFamily[] Families => [.. _families];
        public void Dispose() { }
    }
}
