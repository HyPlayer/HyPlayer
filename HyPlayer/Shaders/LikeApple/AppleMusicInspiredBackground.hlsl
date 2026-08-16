Texture2D<float4> Source0 : register(t0);
Texture2D<float4> Source1 : register(t1);
SamplerState LinearClampSampler : register(s0);
SamplerState LinearZeroBorderSampler : register(s1);

cbuffer FrameConstants : register(b0)
{
    float Time;
    float TextureTransitionMix;
    float2 ViewScale;
    float BlackScrimAlpha;
    float OutputDitherStrength;
    float2 BlurScale;
    float4 ImageScales;
    float4 PinchTextureTransform;
    float LyricsModeMix;
    float3 Padding;
};

struct QuadInput
{
    float4 Position : POSITION;
    float2 TextureCoordinate : TEXCOORD0;
};

struct QuadOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
};

struct RotationOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
};

float2 RotateClockwise(float2 value, float angle)
{
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    return float2(
        cosine * value.x + sine * value.y,
        -sine * value.x + cosine * value.y);
}

float2 ModelTranslation(uint instanceId)
{
    if (instanceId == 1)
    {
        return float2(-0.5, 0.7);
    }
    if (instanceId == 2)
    {
        return float2(-0.95, -0.7);
    }
    return float2(0.0, 0.0);
}

float RotationTimeScale(uint instanceId)
{
    if (instanceId == 1)
    {
        return 90.0;
    }
    if (instanceId == 2)
    {
        return 70.0;
    }
    return 120.0;
}

float ImageScale(uint instanceId)
{
    if (instanceId == 1)
    {
        return ImageScales.y;
    }
    if (instanceId == 2)
    {
        return ImageScales.z;
    }
    return ImageScales.x;
}

RotationOutput RotationVertex(QuadInput input, uint instanceId : SV_InstanceID)
{
    const float twoPi = 6.2831853071795864769;
    float angle = Time * twoPi / RotationTimeScale(instanceId);

    // View * R(-angle) * Translation * R(-angle).
    float2 position = input.Position.xy * ImageScale(instanceId);
    position = RotateClockwise(position, angle);
    position += ModelTranslation(instanceId);
    position = RotateClockwise(position, angle);
    position *= ViewScale;

    RotationOutput output;
    output.Position = float4(position, 0.0, 1.0);
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

float3 ApplySaturation(float3 color, float saturation)
{
    // Saturation matrix tuned for the background material.
    float3 redColumn = float3(
        0.2126 + 0.7873 * saturation,
        0.2126 - 0.2126 * saturation,
        0.2126 - 0.2126 * saturation);
    float3 greenColumn = float3(
        0.7152 - 0.7152 * saturation,
        0.7152 + 0.2848 * saturation,
        0.7152 - 0.7152 * saturation);
    float3 blueColumn = float3(
        0.0722 - 0.0722 * saturation,
        0.0722 - 0.0722 * saturation,
        0.0722 + 0.9278 * saturation);
    return
        redColumn * color.r +
        greenColumn * color.g +
        blueColumn * color.b;
}

float3 ApplyTreatedMaterial(float3 color)
{
    // Reduce saturation before the final composition pass.
    color = ApplySaturation(color, 1.4);
    color = clamp(color, -0.752941, 1.25098);
    color = ApplySaturation(color, 0.70);
    return lerp(color, 0.0.xxx, BlackScrimAlpha);
}

float4 RotationPixel(RotationOutput input) : SV_TARGET
{
    float3 current = Source0.Sample(LinearClampSampler, input.TextureCoordinate).rgb;
    float3 previous = Source1.Sample(LinearClampSampler, input.TextureCoordinate).rgb;
    float3 color = lerp(previous, current, TextureTransitionMix);
    return float4(color, 1.0);
}

QuadOutput FullscreenVertex(QuadInput input)
{
    QuadOutput output;
    output.Position = input.Position;
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

// Fill gaps exposed by the rotating layers.
QuadOutput ArtworkFillVertex(QuadInput input)
{
    QuadOutput output;
    output.Position = input.Position;
    output.TextureCoordinate =
        (input.TextureCoordinate - 0.5) / ViewScale + 0.5;
    return output;
}

// Normalized sigma-42.5 kernel with paired bilinear taps.
static const float BlurCenterWeight = 0.009389731878;
static const float BlurOffsets[77] =
{
    1.499792388, 3.499515571, 5.499238755, 7.498961939,
    9.498685124, 11.49840831, 13.498131497, 15.497854684,
    17.497577874, 19.497301064, 21.497024257, 23.496747451,
    25.496470647, 27.496193845, 29.495917046, 31.495640249,
    33.495363455, 35.495086663, 37.494809875, 39.49453309,
    41.494256308, 43.49397953, 45.493702755, 47.493425984,
    49.493149218, 51.492872455, 53.492595697, 55.492318943,
    57.492042195, 59.49176545, 61.491488712, 63.491211978,
    65.490935249, 67.490658527, 69.490381809, 71.490105098,
    73.489828393, 75.489551694, 77.489275002, 79.488998316,
    81.488721637, 83.488444964, 85.488168299, 87.487891641,
    89.487614991, 91.487338348, 93.487061713, 95.486785085,
    97.486508466, 99.486231855, 101.485955253, 103.485678659,
    105.485402074, 107.485125498, 109.484848931, 111.484572373,
    113.484295824, 115.484019286, 117.483742757, 119.483466238,
    121.483189729, 123.482913231, 125.482636742, 127.482360265,
    129.482083798, 131.481807343, 133.481530898, 135.481254465,
    137.480978043, 139.480701633, 141.480425235, 143.480148848,
    145.479872474, 147.479596112, 149.479319763, 151.479043426,
    153.0
};
static const float BlurWeights[77] =
{
    0.0187664737, 0.01871460399, 0.01862159953, 0.01848807512,
    0.01831490985, 0.01810323743, 0.01785443382, 0.01757010239,
    0.01725205665, 0.01690230103, 0.01652300989, 0.01611650499,
    0.01568523193, 0.01523173574, 0.01475863601, 0.01426860189,
    0.01376432738, 0.01324850701, 0.01272381248, 0.01219287029,
    0.0116582408, 0.01112239879, 0.01058771585, 0.01005644461,
    0.00953070502, 0.00901247274, 0.00850356966, 0.0080056566,
    0.00752022811, 0.0070486094, 0.00659195523, 0.00615125069,
    0.0057273138, 0.00532079962, 0.00493220595, 0.0045618802,
    0.0042100274, 0.00387671917, 0.00356190339, 0.00326541439,
    0.00298698364, 0.0027262505, 0.00248277319, 0.00225603955,
    0.00204547767, 0.00185046618, 0.00167034405, 0.00150441998,
    0.0013519811, 0.00121230117, 0.00108464796, 0.00096829001,
    0.00086250273, 0.00076657363, 0.00067980702, 0.00060152792,
    0.00053108534, 0.00046785492, 0.00041124106, 0.00036067838,
    0.0003156328, 0.00027560209, 0.00024011609, 0.0002087365,
    0.00018105641, 0.00015669956, 0.00013531939, 0.00011659788,
    0.0001002443, 0.00008599378, 0.00007360593, 0.00006286326,
    0.00005356972, 0.00004554915, 0.00003864377, 0.00003271276,
    0.00001440207
};

float4 GaussianBlur(
    float2 textureCoordinate,
    float2 direction,
    bool normalizeOutput)
{
    // Normalize zero-border samples by their accumulated coverage.
    float4 color =
        Source0.Sample(LinearZeroBorderSampler, textureCoordinate) *
        BlurCenterWeight;
    [unroll]
    for (int index = 0; index < 77; index++)
    {
        float2 offset = direction * BlurOffsets[index];
        color +=
            (Source0.Sample(
                LinearZeroBorderSampler,
                textureCoordinate + offset) +
             Source0.Sample(
                LinearZeroBorderSampler,
                textureCoordinate - offset)) *
            BlurWeights[index];
    }
    if (normalizeOutput)
    {
        color.rgb /= max(color.a, 1.0 / 65535.0);
        color.a = 1.0;
    }
    return color;
}

float4 BlurHorizontalPixel(QuadOutput input) : SV_TARGET
{
    uint width;
    uint height;
    Source0.GetDimensions(width, height);
    return GaussianBlur(
        input.TextureCoordinate,
        float2(BlurScale.x / width, 0.0),
        false);
}

float4 BlurVerticalPixel(QuadOutput input) : SV_TARGET
{
    uint width;
    uint height;
    Source0.GetDimensions(width, height);
    return GaussianBlur(
        input.TextureCoordinate,
        float2(0.0, BlurScale.y / height),
        true);
}

struct PinchInput
{
    float2 FromPosition : FROMPOS;
    float2 ToPosition : TOPOS;
    float2 TextureCoordinate : TEXCOORD0;
};

struct PinchOutput
{
    float4 Position : SV_POSITION;
    float2 TextureCoordinate : TEXCOORD0;
    float2 ScreenTextureCoordinate : TEXCOORD1;
};

PinchOutput PinchVertex(PinchInput input)
{
    const float pi = 3.14159265358979323846;
    const float meshWarpTimeScale = 5.0;
    float phase = acos(sin(Time * pi / meshWarpTimeScale)) / pi;
    float mixValue = phase * phase * (3.0 - 2.0 * phase);

    PinchOutput output;
    float2 warpedPosition = lerp(input.FromPosition, input.ToPosition, mixValue);
    output.Position = float4(warpedPosition, 0.0, 1.0);
    output.TextureCoordinate =
        input.TextureCoordinate * PinchTextureTransform.xy +
        PinchTextureTransform.zw;
    output.ScreenTextureCoordinate = float2(
        warpedPosition.x * 0.5 + 0.5,
        0.5 - warpedPosition.y * 0.5);
    return output;
}

float3 SampleTreatedMaterial(float2 textureCoordinate)
{
    float4 lyricSample =
        Source0.Sample(LinearClampSampler, textureCoordinate);
    float3 lyricColor =
        lyricSample.rgb / max(lyricSample.a, 1.0 / 65535.0);

    return ApplyTreatedMaterial(lyricColor);
}

float4 FinishMaterial(float3 color, float2 pixelPosition)
{
    // Half-LSB noise reduces banding when the result is quantized to BGRA8.
    float dither = frac(
        52.9829189 * frac(dot(pixelPosition, float2(0.06711056, 0.00583715)))) -
        0.5;
    color += dither * (OutputDitherStrength / 255.0);

    return float4(clamp(color, 0.07, 0.97), 1.0);
}

float4 OrdinaryMaterialPixel(QuadOutput input) : SV_TARGET
{
    // Ordinary mode keeps the treated backing image without mesh deformation.
    return FinishMaterial(
        SampleTreatedMaterial(input.TextureCoordinate),
        input.Position.xy);
}

float4 MaterialTreatedPixel(QuadOutput input) : SV_TARGET
{
    return FinishMaterial(
        SampleTreatedMaterial(input.TextureCoordinate),
        input.Position.xy);
}

float4 PinchPixel(PinchOutput input) : SV_TARGET
{
    return FinishMaterial(
        SampleTreatedMaterial(input.TextureCoordinate),
        input.Position.xy);
}

float4 CompositeMaterial(
    float2 lyricTextureCoordinate,
    float2 ordinaryTextureCoordinate,
    float2 pixelPosition)
{
    float4 ordinarySample =
        Source1.Sample(LinearClampSampler, ordinaryTextureCoordinate);
    float3 ordinaryColor =
        ordinarySample.rgb / max(ordinarySample.a, 1.0 / 65535.0);
    ordinaryColor = ApplyTreatedMaterial(ordinaryColor);
    float3 lyricColor = SampleTreatedMaterial(lyricTextureCoordinate);
    return FinishMaterial(
        lerp(ordinaryColor, lyricColor, LyricsModeMix),
        pixelPosition);
}

float4 MaterialCompositePixel(QuadOutput input) : SV_TARGET
{
    return CompositeMaterial(
        input.TextureCoordinate,
        input.TextureCoordinate,
        input.Position.xy);
}

float4 PinchCompositePixel(PinchOutput input) : SV_TARGET
{
    return CompositeMaterial(
        input.TextureCoordinate,
        input.ScreenTextureCoordinate,
        input.Position.xy);
}
