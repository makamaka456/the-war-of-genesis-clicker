// PC 판이 쓰는 Direct3D11(Vortice) 의 아주 작은 대용품 — 안드로이드 · 리눅스 시험기에서 같은 소스(GameWindow.cs · HiCanvas.cs)를 그대로 돌린다.
//
// 게임이 GPU 로 하는 일은 두 가지뿐이다(duel-dx/GameWindow.cs Draw · System/HiCanvas.cs PresentCmds):
//  ① 판 텍스처를 배율대로 늘려 찍기 — 픽셀 셰이더가 Board.Load(int2((pos - 판 자리) / 배율)) 한다(배율 · 자리는 셰이더 글에 박힌다).
//  ② 새 UI 층(HiCanvas) — 축에 맞춘 사각형(삼각형 둘)마다 텍스처 × 정점 색, 점/겹선형 표본, 비곱셈 알파 섞기.
// 그래서 셰이더 글을 읽어 어느 쪽인지 · 배율과 자리를 알아내고, CPU 로 같은 그림을 뒷버퍼(uint[] BGRA)에 그린다.
// Present 가 오면 뒷버퍼를 PlatformHost 로 넘긴다(안드로이드: 화면, 시험기: PNG).
// 이름 · 모양은 Vortice 3.8 의 것을 따른다 — 원본이 부르는 것만 있다.
#nullable enable
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Vortice.Direct3D;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Vortice.Mathematics
{
    public readonly record struct Color4(float R, float G, float B, float A);
}

namespace Vortice.Direct3D
{
    public enum FeatureLevel { Level_10_0 = 0xa000, Level_10_1 = 0xa100, Level_11_0 = 0xb000 }
    public enum DriverType { Hardware = 1 }
    public enum PrimitiveTopology { TriangleList = 4 }
}

namespace Vortice.DXGI
{
    public enum Format { Unknown = 0, R32G32_Float = 16, B8G8R8A8_UNorm = 87 }
    public record struct SampleDescription(uint Count, uint Quality);
    public enum Usage { RenderTargetOutput = 0x20 }
    public enum SwapEffect { FlipDiscard = 4 }
    public enum Scaling { None = 1 }
    [Flags] public enum SwapChainFlags { None = 0, FrameLatencyWaitableObject = 64 }
    [Flags] public enum PresentFlags { None = 0 }

    public struct SwapChainDescription1
    {
        public uint Width, Height, BufferCount;
        public Format Format;
        public Usage BufferUsage;
        public SampleDescription SampleDescription;
        public SwapEffect SwapEffect;
        public Scaling Scaling;
        public SwapChainFlags Flags;
    }

    public class ComObject : IDisposable
    {
        public virtual void Dispose() { }
        public T QueryInterface<T>() where T : class => (this as T) ?? throw new InvalidCastException(typeof(T).Name);
    }

    public sealed class IDXGIDevice : ComObject
    {
        internal IDXGIDevice() { }
        public IDXGIAdapter GetAdapter() => new();
    }

    public sealed class IDXGIAdapter : ComObject
    {
        public T GetParent<T>() where T : class => (new IDXGIFactory2() as T)!;
    }

    public sealed class IDXGIFactory2 : ComObject
    {
        public IDXGISwapChain1 CreateSwapChainForHwnd(Direct3D11.ID3D11Device device, IntPtr hwnd, SwapChainDescription1 desc) => new IDXGISwapChain2(desc);
    }

    public class IDXGISwapChain1 : ComObject
    {
        internal Direct3D11.ID3D11Texture2D Back;
        internal IDXGISwapChain1(SwapChainDescription1 desc) { Back = new Direct3D11.ID3D11Texture2D((int)desc.Width, (int)desc.Height); }

        public T GetBuffer<T>(uint index) where T : class => (Back as T)!;

        public void ResizeBuffers(uint count, uint width, uint height, Format format, SwapChainFlags flags) => Back = new Direct3D11.ID3D11Texture2D((int)width, (int)height);

        /// <summary>뒷버퍼를 화면으로 — PlatformHost 가 받는다.</summary>
        public void Present(uint syncInterval, PresentFlags flags) => DuelDx.Native.PlatformHost.Present(Back.Pixels, Back.Width, Back.Height);
    }

    public sealed class IDXGISwapChain2 : IDXGISwapChain1
    {
        internal IDXGISwapChain2(SwapChainDescription1 desc) : base(desc) { }
        public uint MaximumFrameLatency { get; set; }
        /// <summary>틀 대기 손잡이 — Win32.WaitForSingleObjectEx 가 이 값이면 다음 틀 시각까지 잔다.</summary>
        public IntPtr FrameLatencyWaitableObject => DuelDx.Native.Win32.FrameWaitHandle;
    }
}

namespace Vortice.D3DCompiler
{
    public static class Compiler
    {
        /// <summary>셰이더를 「컴파일」 — 글 그대로 담아 돌려준다(진입점 이름을 앞에 붙인다). 그리는 쪽이 글을 읽어 할 일을 정한다.</summary>
        public static ReadOnlyMemory<byte> Compile(string source, string entryPoint, string fileName, string profile) =>
            Encoding.UTF8.GetBytes(entryPoint + "\n" + source);
    }
}

namespace Vortice.Direct3D11
{
    public readonly struct Result
    {
        public void CheckError() { }
    }

    [Flags] public enum DeviceCreationFlags { None = 0, BgraSupport = 0x20 }
    public enum ResourceUsage { Default = 0, Dynamic = 2, Staging = 3 }
    [Flags] public enum BindFlags { None = 0, VertexBuffer = 1, ShaderResource = 8 }
    [Flags] public enum CpuAccessFlags { None = 0, Write = 0x10000, Read = 0x20000 }
    public enum MapMode { Read = 1, WriteDiscard = 4 }

    public struct Texture2DDescription
    {
        public uint Width, Height, MipLevels, ArraySize;
        public Format Format;
        public SampleDescription SampleDescription;
        public ResourceUsage Usage;
        public BindFlags BindFlags;
        public CpuAccessFlags CPUAccessFlags;
    }

    public readonly struct MappedSubresource
    {
        public MappedSubresource(nint data, uint rowPitch) { DataPointer = data; RowPitch = rowPitch; }
        public nint DataPointer { get; }
        public uint RowPitch { get; }
    }

    public readonly record struct InputElementDescription(string SemanticName, uint SemanticIndex, Format Format, uint AlignedByteOffset, uint Slot);

    public readonly record struct BufferDescription(uint ByteWidth, BindFlags BindFlags, ResourceUsage Usage, CpuAccessFlags CPUAccessFlags);

    public readonly record struct BlendDescription(bool NonPremultipliedAlpha)
    {
        public static BlendDescription NonPremultiplied => new(true);
    }

    public readonly record struct RasterizerDescription(int Kind)
    {
        public static RasterizerDescription CullNone => new(0);
    }

    public readonly record struct SamplerDescription(bool Linear)
    {
        public static SamplerDescription PointClamp => new(false);
        public static SamplerDescription LinearClamp => new(true);
    }

    public static class D3D11
    {
        public static Result D3D11CreateDevice(IDXGIAdapter? adapter, DriverType driverType, DeviceCreationFlags flags, FeatureLevel[] levels,
            out ID3D11Device device, out ID3D11DeviceContext context)
        {
            device = new ID3D11Device();
            context = new ID3D11DeviceContext();
            return default;
        }
    }

    /// <summary>자원(텍스처 · 정점 버퍼) — 메모리를 고정해 두고 Map 은 그 자리를 돌려준다.</summary>
    public abstract class ID3D11Resource : ComObject
    {
        internal abstract nint Pointer { get; }
        internal abstract uint RowPitch { get; }
    }

    public sealed class ID3D11Texture2D : ID3D11Resource
    {
        internal readonly uint[] Pixels;
        internal readonly int Width, Height;
        private readonly Texture2DDescription _desc;

        internal ID3D11Texture2D(int width, int height, Texture2DDescription desc = default)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Pixels = GC.AllocateArray<uint>(Width * Height, pinned: true);
            _desc = desc with { Width = (uint)Width, Height = (uint)Height, Format = Format.B8G8R8A8_UNorm };
        }

        public Texture2DDescription Description => _desc;
        internal override unsafe nint Pointer { get { fixed (uint* p = Pixels) return (nint)p; } }   // 고정 배열이라 주소가 안 바뀐다
        internal override uint RowPitch => (uint)Width * 4;
    }

    public sealed class ID3D11Buffer : ID3D11Resource
    {
        internal readonly byte[] Bytes;
        internal ID3D11Buffer(int size) { Bytes = GC.AllocateArray<byte>(Math.Max(1, size), pinned: true); }
        internal override unsafe nint Pointer { get { fixed (byte* p = Bytes) return (nint)p; } }
        internal override uint RowPitch => (uint)Bytes.Length;
    }

    public sealed class ID3D11ShaderResourceView : ComObject
    {
        internal readonly ID3D11Texture2D Texture;
        internal ID3D11ShaderResourceView(ID3D11Texture2D texture) { Texture = texture; }
    }

    public sealed class ID3D11RenderTargetView : ComObject
    {
        internal readonly ID3D11Texture2D Texture;
        internal ID3D11RenderTargetView(ID3D11Texture2D texture) { Texture = texture; }
    }

    /// <summary>셰이더 — 글을 읽어 판 셰이더(배율 · 자리)인지 UI 층 셰이더인지 가린다.</summary>
    public sealed class ShaderCode
    {
        public readonly bool IsBoard;
        public readonly double Zoom;
        public readonly int OffsetX, OffsetY;

        internal ShaderCode(ReadOnlySpan<byte> blob)
        {
            string text = Encoding.UTF8.GetString(blob);
            IsBoard = text.Contains("Board.Load", StringComparison.Ordinal);
            if (!IsBoard) return;
            // int2 uv = int2((i.pos.xy - float2(X, Y)) / Z);
            var m = Regex.Match(text, @"float2\((-?\d+),\s*(-?\d+)\)\)\s*/\s*([0-9.eE+-]+)\)");
            if (!m.Success) throw new NotSupportedException("판 셰이더 글을 못 읽었습니다");
            OffsetX = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            OffsetY = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            Zoom = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        }
    }

    public sealed class ID3D11VertexShader : ComObject { internal ID3D11VertexShader() { } }
    public sealed class ID3D11PixelShader : ComObject { internal readonly ShaderCode Code; internal ID3D11PixelShader(ShaderCode code) { Code = code; } }
    public sealed class ID3D11InputLayout : ComObject { internal ID3D11InputLayout() { } }
    public sealed class ID3D11BlendState : ComObject { internal ID3D11BlendState() { } }
    public sealed class ID3D11RasterizerState : ComObject { internal ID3D11RasterizerState() { } }
    public sealed class ID3D11SamplerState : ComObject { internal readonly bool Linear; internal ID3D11SamplerState(bool linear) { Linear = linear; } }

    public sealed class ID3D11Device : ComObject
    {
        internal ID3D11Device() { }
        public ID3D11Texture2D CreateTexture2D(Texture2DDescription desc) => new((int)desc.Width, (int)desc.Height, desc);
        public ID3D11ShaderResourceView CreateShaderResourceView(ID3D11Texture2D texture) => new(texture);
        public ID3D11RenderTargetView CreateRenderTargetView(ID3D11Texture2D texture) => new(texture);
        public ID3D11VertexShader CreateVertexShader(ReadOnlySpan<byte> code) => new();
        public ID3D11PixelShader CreatePixelShader(ReadOnlySpan<byte> code) => new(new ShaderCode(code));
        public ID3D11InputLayout CreateInputLayout(InputElementDescription[] elements, ReadOnlySpan<byte> code) => new();
        public ID3D11BlendState CreateBlendState(BlendDescription desc) => new();
        public ID3D11RasterizerState CreateRasterizerState(RasterizerDescription desc) => new();
        public ID3D11SamplerState CreateSamplerState(SamplerDescription desc) => new(desc.Linear);
        public ID3D11Buffer CreateBuffer(BufferDescription desc) => new((int)desc.ByteWidth);
    }

    /// <summary>그리기 — CPU 로 뒷버퍼에 찍는다.</summary>
    public sealed unsafe class ID3D11DeviceContext : ComObject
    {
        internal ID3D11DeviceContext() { }

        private ID3D11Texture2D? _target;
        private int _vx, _vy, _vw, _vh;
        private ID3D11PixelShader? _ps;
        private ID3D11ShaderResourceView? _srv;
        private ID3D11SamplerState? _sampler;
        private bool _blend;
        private ID3D11Buffer? _vb;
        private uint _stride;

        public MappedSubresource Map(ID3D11Resource resource, uint subresource, MapMode mode) => new(resource.Pointer, resource.RowPitch);
        public void Unmap(ID3D11Resource resource, uint subresource) { }

        public void OMSetRenderTargets(ID3D11RenderTargetView rtv) => _target = rtv.Texture;
        public void ClearRenderTargetView(ID3D11RenderTargetView rtv, Color4 color)
        {
            uint c = (uint)(color.A * 255) << 24 | (uint)(color.R * 255) << 16 | (uint)(color.G * 255) << 8 | (uint)(color.B * 255);
            rtv.Texture.Pixels.AsSpan().Fill(c);
        }
        public void RSSetViewport(float x, float y, float width, float height) => (_vx, _vy, _vw, _vh) = ((int)x, (int)y, (int)width, (int)height);
        public void RSSetState(ID3D11RasterizerState? state) { }
        public void IASetInputLayout(ID3D11InputLayout? layout) { }
        public void IASetPrimitiveTopology(PrimitiveTopology topology) { }
        public void IASetVertexBuffer(uint slot, ID3D11Buffer buffer, uint stride, uint offset = 0) => (_vb, _stride) = (buffer, stride);
        public void VSSetShader(ID3D11VertexShader? shader) { }
        public void PSSetShader(ID3D11PixelShader? shader) => _ps = shader;
        public void PSSetShaderResources(uint slot, ID3D11ShaderResourceView[] views) => _srv = views.Length > 0 ? views[0] : null;
        public void PSSetShaderResource(uint slot, ID3D11ShaderResourceView view) => _srv = view;
        public void PSSetSampler(uint slot, ID3D11SamplerState sampler) => _sampler = sampler;
        public void OMSetBlendState(ID3D11BlendState? state) => _blend = state != null;

        public void CopyResource(ID3D11Resource dst, ID3D11Resource src)
        {
            if (dst is ID3D11Texture2D d && src is ID3D11Texture2D s) s.Pixels.AsSpan(0, Math.Min(s.Pixels.Length, d.Pixels.Length)).CopyTo(d.Pixels);
        }

        public void Draw(uint vertexCount, uint startVertex)
        {
            if (_target == null || _ps == null || _srv == null) return;
            if (_ps.Code.IsBoard) DrawBoard();
            else DrawQuads((int)vertexCount, (int)startVertex);
        }

        /// <summary>① 판 — 뷰포트 안의 각 점에 판 텍스처의 (점 − 자리) ÷ 배율 자리 점을 찍는다(Load: 범위 밖은 0).</summary>
        private void DrawBoard()
        {
            var dst = _target!;
            var src = _srv!.Texture;
            var code = _ps!.Code;
            int x0 = Math.Max(0, _vx), y0 = Math.Max(0, _vy), x1 = Math.Min(dst.Width, _vx + _vw), y1 = Math.Min(dst.Height, _vy + _vh);
            if (x1 <= x0 || y1 <= y0) return;
            var cols = new int[x1 - x0];
            for (int x = x0; x < x1; x++) cols[x - x0] = (int)((x + 0.5 - code.OffsetX) / code.Zoom);   // SV_Position 은 점 가운데(+0.5)
            fixed (uint* d0 = dst.Pixels)
            fixed (uint* s0 = src.Pixels)
                for (int y = y0; y < y1; y++)
                {
                    int sy = (int)((y + 0.5 - code.OffsetY) / code.Zoom);
                    uint* drow = d0 + y * dst.Width;
                    if ((uint)sy >= (uint)src.Height) { for (int x = x0; x < x1; x++) if (!_blend) drow[x] = 0; continue; }
                    uint* srow = s0 + sy * src.Width;
                    for (int x = x0; x < x1; x++)
                    {
                        int sx = cols[x - x0];
                        uint c = (uint)sx < (uint)src.Width ? srow[sx] : 0;
                        drow[x] = _blend ? Blend(drow[x], c) : c;
                    }
                }
        }

        /// <summary>② UI 층 — 정점 6개가 사각형 하나(HiCanvas 의 Vertex: X, Y, U, V, 색). 텍스처 × 정점 색을 알파로 섞는다.</summary>
        private void DrawQuads(int count, int start)
        {
            if (_vb == null || _stride < 20) return;
            var dst = _target!;
            var tex = _srv!.Texture;
            bool linear = _sampler?.Linear == true;
            fixed (byte* vb = _vb.Bytes)
                for (int q = start; q + 6 <= start + count; q += 6)
                {
                    float* a = (float*)(vb + q * _stride), b = (float*)(vb + (q + 4) * _stride);   // 0 = 왼쪽 위, 4 = 오른쪽 아래
                    uint color = *(uint*)(vb + q * _stride + 16);
                    // 정규 좌표(−1~1) → 뷰포트 점
                    double l = _vx + (a[0] + 1) * 0.5 * _vw, t = _vy + (1 - a[1]) * 0.5 * _vh;
                    double r = _vx + (b[0] + 1) * 0.5 * _vw, btm = _vy + (1 - b[1]) * 0.5 * _vh;
                    float u0 = a[2], v0 = a[3], u1 = b[2], v1 = b[3];
                    int x0 = Math.Max(0, (int)Math.Round(l)), x1 = Math.Min(dst.Width, (int)Math.Round(r));
                    int y0 = Math.Max(0, (int)Math.Round(t)), y1 = Math.Min(dst.Height, (int)Math.Round(btm));
                    if (x1 <= x0 || y1 <= y0) continue;
                    double du = (u1 - u0) / (r - l), dv = (v1 - v0) / (btm - t);
                    uint ca = color >> 24, cr = color >> 16 & 0xFF, cg = color >> 8 & 0xFF, cb = color & 0xFF;
                    bool plainWhite = tex.Width == 1 && tex.Height == 1;
                    for (int y = y0; y < y1; y++)
                    {
                        double v = v0 + (y + 0.5 - t) * dv;
                        uint* drow = (uint*)Unsafe(dst) + y * dst.Width;
                        for (int x = x0; x < x1; x++)
                        {
                            uint s = plainWhite ? tex.Pixels[0] : linear ? SampleLinear(tex, u0 + (x + 0.5 - l) * du, v) : SamplePoint(tex, u0 + (x + 0.5 - l) * du, v);
                            // 텍스처 × 정점 색
                            uint sa = (s >> 24) * ca / 255, sr = (s >> 16 & 0xFF) * cr / 255, sg = (s >> 8 & 0xFF) * cg / 255, sb = (s & 0xFF) * cb / 255;
                            drow[x] = Blend(drow[x], sa << 24 | sr << 16 | sg << 8 | sb);
                        }
                    }
                }
        }

        private static uint* Unsafe(ID3D11Texture2D t) { fixed (uint* p = t.Pixels) return p; }   // 고정 배열

        private static uint SamplePoint(ID3D11Texture2D t, double u, double v)
        {
            int x = Math.Clamp((int)Math.Floor(u * t.Width), 0, t.Width - 1), y = Math.Clamp((int)Math.Floor(v * t.Height), 0, t.Height - 1);
            return t.Pixels[y * t.Width + x];
        }

        private static uint SampleLinear(ID3D11Texture2D t, double u, double v)
        {
            double fx = u * t.Width - 0.5, fy = v * t.Height - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            int kx = (int)((fx - x0) * 256), ky = (int)((fy - y0) * 256);
            int xa = Math.Clamp(x0, 0, t.Width - 1), xb = Math.Clamp(x0 + 1, 0, t.Width - 1), ya = Math.Clamp(y0, 0, t.Height - 1), yb = Math.Clamp(y0 + 1, 0, t.Height - 1);
            uint p00 = t.Pixels[ya * t.Width + xa], p10 = t.Pixels[ya * t.Width + xb], p01 = t.Pixels[yb * t.Width + xa], p11 = t.Pixels[yb * t.Width + xb];
            uint Mix(int shift)
            {
                int top = (int)(p00 >> shift & 0xFF) * (256 - kx) + (int)(p10 >> shift & 0xFF) * kx;
                int bot = (int)(p01 >> shift & 0xFF) * (256 - kx) + (int)(p11 >> shift & 0xFF) * kx;
                return (uint)((top * (256 - ky) + bot * ky) >> 16);
            }
            return Mix(24) << 24 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
        }

        /// <summary>비곱셈 알파 섞기 — src × a + dst × (1 − a). 결과 알파는 불투명.</summary>
        private static uint Blend(uint dst, uint src)
        {
            uint a = src >> 24;
            if (a == 255) return src;
            if (a == 0) return dst;
            uint ia = 255 - a;
            uint r = ((src >> 16 & 0xFF) * a + (dst >> 16 & 0xFF) * ia) / 255;
            uint g = ((src >> 8 & 0xFF) * a + (dst >> 8 & 0xFF) * ia) / 255;
            uint b = ((src & 0xFF) * a + (dst & 0xFF) * ia) / 255;
            return 0xFF000000 | r << 16 | g << 8 | b;
        }
    }
}
