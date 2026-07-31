using ComputeSharp;
using ComputeSharp.D2D1;

namespace HyPlayer.UI.Effects;

/// <summary>
///     Ported by jayfunc from
///     <see href="https://github.com/Storyteller-Studios/Isolation/blob/main/ShaderTest.UWP/Shaders/effect.hlsl" />.
///     Edited By RaspberryKan.
/// </summary>
/// <param name="resolution"></param>
/// <param name="time"></param>
/// <param name="color1"></param>
/// <param name="color2"></param>
/// <param name="color3"></param>
/// <param name="color4"></param>
/// <param name="randomValue1"></param>
/// <param name="randomValue2"></param>
/// <param name="randomValue3"></param>
/// <param name="enableLightWave"></param>
/// <param name="enableDithering"></param>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
public readonly partial struct IsolationEffect(
    float2 resolution,
    float time,
    float3 color1,
    float3 color2,
    float3 color3,
    float3 color4,
    float randomValue1,
    float randomValue2,
    float randomValue3,
    bool enableLightWave,
    bool enableDithering = true) : ID2D1PixelShader
{
    const float RAD_TO_DEG = 180.0f / 3.1415926f;
    const float DEG_TO_RAD = 3.1415926f / 180.0f;

    private float2 Rotate(float2 p, float a)
    {
        var c = Hlsl.Cos(a);
        var s = Hlsl.Sin(a);
        return new float2(
            p.X * c - p.Y * s,
            p.X * s + p.Y * c
        );
    }

    private float2 F_Hash(float2 p)
    {
        p = new float2(
            Hlsl.Dot(p, new float2(2127.1f, 81.17f)),
            Hlsl.Dot(p, new float2(1269.5f, 283.37f))
        );
        return Hlsl.Frac(Hlsl.Sin(p) * 43758.5453f);
    }

    private float F_Noise(float2 p)
    {
        var i = Hlsl.Floor(p);
        var f = Hlsl.Frac(p);
        var u = f * f * (3.0f - 2.0f * f);

        var n = Hlsl.Lerp(
            Hlsl.Lerp(
                Hlsl.Dot(-1.0f + 2.0f * F_Hash(i + new float2(0.0f, 0.0f)), f - new float2(0.0f, 0.0f)),
                Hlsl.Dot(-1.0f + 2.0f * F_Hash(i + new float2(1.0f, 0.0f)), f - new float2(1.0f, 0.0f)),
                u.X),
            Hlsl.Lerp(
                Hlsl.Dot(-1.0f + 2.0f * F_Hash(i + new float2(0.0f, 1.0f)), f - new float2(0.0f, 1.0f)),
                Hlsl.Dot(-1.0f + 2.0f * F_Hash(i + new float2(1.0f, 1.0f)), f - new float2(1.0f, 1.0f)),
                u.X),
            u.Y);
        return 0.5f + 0.5f * n;
    }

    private float NormalizeHueDegrees(float h)
    {
        h = Hlsl.Fmod(h, 360.0f);

        if (h < 0.0f) h += 360.0f;

        return h;
    }

    //Color Utilities
    private float3 Srgb2OkLab(float3 c)
    {
        c = Hlsl.Saturate(c);

        // sRGB 解码，封装在函数内部，调用者不需要关心 Linear RGB
        var r = c.X <= 0.04045f
            ? c.X / 12.92f
            : Hlsl.Pow((c.X + 0.055f) / 1.055f, 2.4f);

        var g = c.Y <= 0.04045f
            ? c.Y / 12.92f
            : Hlsl.Pow((c.Y + 0.055f) / 1.055f, 2.4f);

        var b = c.Z <= 0.04045f
            ? c.Z / 12.92f
            : Hlsl.Pow((c.Z + 0.055f) / 1.055f, 2.4f);
        // 基础的线性近似矩阵（假设输入已经是线性 RGB，若原本是 sRGB，理想情况下需先做 Gamma 逆校正）
        var l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        var m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        var s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        // 核心的非线性映射（开立方根）
        var l_ = Hlsl.Pow(Hlsl.Max(l, 0.0f), 1.0f / 3.0f);
        var m_ = Hlsl.Pow(Hlsl.Max(m, 0.0f), 1.0f / 3.0f);
        var s_ = Hlsl.Pow(Hlsl.Max(s, 0.0f), 1.0f / 3.0f);

        return new float3(
            0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
            1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
            0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
        );
    }

    private float3 OkLab2Srgb(float3 c)
    {
        var l_ = c.X + 0.3963377774f * c.Y + 0.2158037573f * c.Z;
        var m_ = c.X - 0.1055613458f * c.Y - 0.0638541728f * c.Z;
        var s_ = c.X - 0.0894841775f * c.Y - 1.2914855480f * c.Z;

        // 逆映射（立方）
        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        var r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        var g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        var b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        r = r <= 0.0031308f
            ? 12.92f * r
            : 1.055f * Hlsl.Pow(Hlsl.Max(r, 0.0f), 1.0f / 2.4f) - 0.055f;

        g = g <= 0.0031308f
            ? 12.92f * g
            : 1.055f * Hlsl.Pow(Hlsl.Max(g, 0.0f), 1.0f / 2.4f) - 0.055f;

        b = b <= 0.0031308f
            ? 12.92f * b
            : 1.055f * Hlsl.Pow(Hlsl.Max(b, 0.0f), 1.0f / 2.4f) - 0.055f;

        return Hlsl.Saturate(new float3(r, g, b));
    }

    private float3 OkLab2OkLch(float3 lab)
    {
        var L = lab.X;
        var a = lab.Y;
        var b = lab.Z;

        var C = Hlsl.Sqrt(a * a + b * b);

        var H = Hlsl.Atan2(b, a) * RAD_TO_DEG;
        H = NormalizeHueDegrees(H);

        // 无彩度颜色没有实际 hue
        if (C <= 1e-6f) H = 0.0f;

        return new float3(L, C, H);
    }

    private float3 OkLch2OkLab(float3 lch)
    {
        var L = lch.X;
        var C = lch.Y;
        var H = lch.Z * DEG_TO_RAD;

        var a = C * Hlsl.Cos(H);
        var b = C * Hlsl.Sin(H);

        return new float3(L, a, b);
    }

    //Effect Utilities
    private float3 LightWave(float3 input, float2 uv)
    {
        var p = -1.0f + 1.5f * uv.XY;
        var x = p.X;
        var y = p.Y;

        var lch = OkLab2OkLch(input);
        var t = time * 0.2f;

        var yPhase = y / 0.3f;
        var xPhase = x / 0.2f;

        var timeWarp = Hlsl.Cos(Hlsl.Sin(t) * 2.0f) * 0.1f;

        var mov0Scaled = (x + y) * 0.001f
                         + timeWarp
                         + Hlsl.Sin(x * 0.01f);

        var c1 = Hlsl.Sin(yPhase + 2.0f * t + randomValue1) * 0.5f
                 - yPhase
                 - xPhase * 0.5f;

        var c2 = Hlsl.Cos(
            c1
            + Hlsl.Sin(mov0Scaled + t)
            + Hlsl.Sin(y * 0.025f + t)
            + Hlsl.Sin((x + y) * 0.01f) * 3.0f
            + randomValue2
        );

        var c3 = Hlsl.Abs(
            Hlsl.Sin(
                c2
                + Hlsl.Cos(yPhase + t + xPhase + c2)
                + Hlsl.Cos(xPhase)
                + Hlsl.Sin(x * 0.001f)
                + randomValue3
            )
        );

        var L = lch.X * (1.1f - 0.1f * c3);

        L = Hlsl.Clamp(L, 0.0f, 1.0f);

        var lab = OkLch2OkLab(new float3(L, lch.Y, lch.Z));
        return OkLab2Srgb(lab);
    }

    // Dithering Utilities
    private float RemapTri(float v)
    {
        // Convert uniform distribution into triangle-shaped distribution.
        var orig = v * 2.0f - 1.0f;
        v = orig / Hlsl.Sqrt(Hlsl.Abs(orig));
        v = Hlsl.Max(-1.0f, v); // Nerf the NaN generated by 0*rsqrt(0)
        v = v - Hlsl.Sign(orig) + 0.5f;
        return v;
    }

    private float3 RemapTri(float3 c)
    {
        return new float3(RemapTri(c.X), RemapTri(c.Y), RemapTri(c.Z));
    }

    private float3 ScreenSpaceDither(float2 vScreenPos, float time)
    {
        var colorDepth = 32.0f;
        var dotValue = Hlsl.Dot(new float2(131.0f, 312.0f), vScreenPos.XY + time);
        var vDither = new float3(dotValue, dotValue, dotValue);
        vDither.XYZ = Hlsl.Frac(vDither.XYZ / new float3(103.0f, 71.0f, 97.0f));
        return RemapTri(vDither.XYZ) / colorDepth;
    }

    public float4 Execute()
    {
        var scene = D2D.GetScenePosition().XY;
        var uv = scene / resolution;

        var tuv = uv;
        tuv -= 0.5f;

        var degree = F_Noise(new float2(time * 0.1f, tuv.X * tuv.Y));

        tuv = Rotate(tuv, Hlsl.Radians((degree - 0.5f) * 720.0f + 180.0f));

        var frequency = 5.0f;
        var amplitude = 25.0f;
        var speed = time * 0.75f;

        var diter = enableDithering ? ScreenSpaceDither(scene, time) : new float3(0.0f, 0.0f, 0.0f);

        tuv.X += Hlsl.Sin(tuv.Y * frequency + speed) / amplitude;
        tuv.Y += Hlsl.Sin(tuv.X * frequency * 1.5f + speed) / (amplitude * 0.5f);

        float3 c1, c2, c3, c4;
        c1 = Srgb2OkLab(color1);
        c2 = Srgb2OkLab(color2);
        c3 = Srgb2OkLab(color3);
        c4 = Srgb2OkLab(color4);

        var rotatedX = Rotate(tuv, Hlsl.Radians(-5.0f)).X;

        var layer1 = Hlsl.Lerp(c1, c2, Hlsl.SmoothStep(-0.3f, 0.2f, rotatedX));
        var layer2 = Hlsl.Lerp(c3, c4, Hlsl.SmoothStep(-0.3f, 0.2f, rotatedX));

        var finalComp = Hlsl.Lerp(layer1, layer2, Hlsl.SmoothStep(0.5f, -0.3f, tuv.Y));

        if (enableLightWave)
        {
            return new float4(Hlsl.Saturate(LightWave(finalComp, uv) + diter), 1.0f);
        }
        else
        {
            return new float4(Hlsl.Saturate(OkLab2Srgb(finalComp) + diter), 1.0f);
        }
    }
}