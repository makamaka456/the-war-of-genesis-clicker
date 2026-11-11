using Android.Content;
using Android.Graphics;
using Android.Views;

namespace WarOfGenesis.Droid;

/// <summary>
/// 게임 판(<c>uint[]</c> ARGB, PC 판 <c>GameWindow._fb</c> 와 같은 꼴)을 화면에 비율 그대로 늘려 띄우는 뷰.
/// </summary>
/// <remarks>
/// PC 판은 이 판을 D3D11 텍스처로 올려 띄운다. 여기서는 Bitmap 하나로 옮겨 그린다(1단계 — 느리면 GLES 텍스처로 바꾼다).
/// 남는 자리는 검게 둔다(4:3 판을 16:9 화면에 띄우면 양옆이 빈다).
/// </remarks>
public sealed class FrameView : View
{
    public int FrameWidth { get; }
    public int FrameHeight { get; }

    /// <summary>판 — 다 그렸으면 <see cref="Present"/>.</summary>
    public uint[] Pixels { get; }

    private readonly Bitmap _bitmap;
    private readonly Paint _filter = new() { FilterBitmap = false };
    private readonly object _gate = new();

    public FrameView(Context context, int width, int height) : base(context)
    {
        FrameWidth = width;
        FrameHeight = height;
        Pixels = new uint[width * height];
        _bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        SetBackgroundColor(Color.Black);
    }

    /// <summary>판을 화면으로 — 아무 스레드에서나 부른다.</summary>
    public void Present()
    {
        lock (_gate)
            _bitmap.SetPixels((int[])(object)Pixels, 0, FrameWidth, 0, 0, FrameWidth, FrameHeight);
        PostInvalidate();
    }

    /// <summary>1단계용: 글 줄들을 판에 찍는다(아직 게임 글꼴이 없어 안드로이드 글꼴로).</summary>
    public void DrawText(IReadOnlyList<string> lines)
    {
        lock (_gate)
        {
            using var canvas = new Canvas(_bitmap);
            canvas.DrawColor(Color.Rgb(16, 16, 32));
            using var paint = new Paint { Color = Color.White, TextSize = 16, AntiAlias = true };
            float y = 28;
            foreach (string line in lines) { canvas.DrawText(line, 16, y, paint); y += 22; }
        }
        PostInvalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float scale = Math.Min((float)Width / FrameWidth, (float)Height / FrameHeight);
        float w = FrameWidth * scale, h = FrameHeight * scale, x = (Width - w) / 2, y = (Height - h) / 2;
        lock (_gate)
            canvas.DrawBitmap(_bitmap, null, new RectF(x, y, x + w, y + h), _filter);
    }
}
