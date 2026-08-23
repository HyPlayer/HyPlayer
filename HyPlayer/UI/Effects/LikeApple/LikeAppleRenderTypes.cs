using System.Numerics;
using System.Runtime.InteropServices;

namespace HyPlayer.UI.Effects.LikeApple;

[StructLayout(LayoutKind.Sequential)]
internal struct LikeAppleFrameConstants
{
    public float Time;
    public float TextureTransitionMix;
    public Vector2 ViewScale;
    public float BlackScrimAlpha;
    public float OutputDitherStrength;
    public Vector2 BlurScale;
    public Vector4 ImageScales;
    public Vector4 PinchTextureTransform;
    public float LyricsModeMix;
    public float RotationScale;
    public Vector2 Padding;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LikeAppleQuadVertex
{
    public LikeAppleQuadVertex(Vector4 position, Vector2 textureCoordinate)
    {
        Position = position;
        TextureCoordinate = textureCoordinate;
    }

    public readonly Vector4 Position;
    public readonly Vector2 TextureCoordinate;
}
