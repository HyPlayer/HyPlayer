using System;

namespace HyPlayer.UI.Effects.LikeApple;

/// <summary>
/// Immutable hand-off between the XAML resource layer and the D3D pipeline.
/// Shader files are loaded by the layer before the renderer is constructed.
/// </summary>
internal sealed class LikeAppleShaderBytecode
{
    public LikeAppleShaderBytecode(
        byte[] rotationVertex,
        byte[] artworkFillVertex,
        byte[] fullscreenVertex,
        byte[] pinchVertex,
        byte[] rotationPixel,
        byte[] blurHorizontalPixel,
        byte[] blurVerticalPixel,
        byte[] ordinaryMaterialPixel,
        byte[] materialTreatedPixel,
        byte[] materialCompositePixel,
        byte[] pinchPixel,
        byte[] pinchCompositePixel)
    {
        RotationVertex = RequireBytecode(rotationVertex);
        ArtworkFillVertex = RequireBytecode(artworkFillVertex);
        FullscreenVertex = RequireBytecode(fullscreenVertex);
        PinchVertex = RequireBytecode(pinchVertex);
        RotationPixel = RequireBytecode(rotationPixel);
        BlurHorizontalPixel = RequireBytecode(blurHorizontalPixel);
        BlurVerticalPixel = RequireBytecode(blurVerticalPixel);
        OrdinaryMaterialPixel = RequireBytecode(ordinaryMaterialPixel);
        MaterialTreatedPixel = RequireBytecode(materialTreatedPixel);
        MaterialCompositePixel = RequireBytecode(materialCompositePixel);
        PinchPixel = RequireBytecode(pinchPixel);
        PinchCompositePixel = RequireBytecode(pinchCompositePixel);
    }

    public byte[] RotationVertex { get; }
    public byte[] ArtworkFillVertex { get; }
    public byte[] FullscreenVertex { get; }
    public byte[] PinchVertex { get; }
    public byte[] RotationPixel { get; }
    public byte[] BlurHorizontalPixel { get; }
    public byte[] BlurVerticalPixel { get; }
    public byte[] OrdinaryMaterialPixel { get; }
    public byte[] MaterialTreatedPixel { get; }
    public byte[] MaterialCompositePixel { get; }
    public byte[] PinchPixel { get; }
    public byte[] PinchCompositePixel { get; }

    private static byte[] RequireBytecode(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0
            ? value
            : throw new ArgumentException("Shader bytecode cannot be empty.", nameof(value));
    }
}
