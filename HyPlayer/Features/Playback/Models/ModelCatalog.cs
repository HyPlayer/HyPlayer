using System;
using System.Collections.Generic;

namespace HyPlayer.Features.Playback.Models;

public enum ModelPurpose
{
    Beat,
    Key,
    Vocal
}

public sealed class ModelFile
{
    public required string Name { get; init; }
    public required string Repository { get; init; }
    public required string RemotePath { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public bool IsModel { get; init; }
}

public sealed class ModelDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ModelPurpose Purpose { get; init; }
    public required string Description { get; init; }
    public required string License { get; init; }
    public required string Homepage { get; init; }
    public required IReadOnlyList<ModelFile> Files { get; init; }
    public required string InputName { get; init; }
    public required long[] InputShape { get; init; }
    public required string[] OutputNames { get; init; }
    public IReadOnlyDictionary<string, long> FixedDimensions { get; init; } = new Dictionary<string, long>();
    public bool DisableGpuGraphOptimization { get; init; }

    public ModelFile ModelFile
    {
        get
        {
            foreach (var file in Files)
                if (file.IsModel) return file;
            throw new InvalidOperationException(Id);
        }
    }

    public long TotalSize
    {
        get
        {
            long total = 0;
            foreach (var file in Files) total += file.Size;
            return total;
        }
    }
}

public static class ModelCatalog
{
    public const string BeatThisId = "beat-this";
    public const string SKeyId = "s-key";
    public const string VocalId = "mdx-voc-ft";

    public static IReadOnlyList<ModelDescriptor> All { get; } = (ModelDescriptor[])
    [
        new ModelDescriptor
        {
            Id = BeatThisId,
            Name = "Beat This!",
            Purpose = ModelPurpose.Beat,
            Description = "节拍与小节线检测，替代内置算法，复杂节奏与变速歌曲更准确",
            License = "MIT",
            Homepage = "https://github.com/CPJKU/beat_this",
            InputName = "spect",
            InputShape = [-1, 1500, 128],
            OutputNames = ["beat", "downbeat"],
            FixedDimensions = new Dictionary<string, long> { ["windows"] = 1, ["frames"] = 1500 },
            DisableGpuGraphOptimization = true,
            Files = (ModelFile[])
            [
                new ModelFile
                {
                    Name = "beat_this.onnx",
                    Repository = "musetric/beat-this-onnx",
                    RemotePath = "beat_this.onnx",
                    Size = 190649026,
                    Sha256 = "1337dc6c21257ed6b803418efc320df42b5d8eb296ef16648ef60457cd4b9e55",
                    IsModel = true
                },
                new ModelFile
                {
                    Name = "mel-filterbank.bin",
                    Repository = "musetric/beat-this-onnx",
                    RemotePath = "mel-filterbank.bin",
                    Size = 262656,
                    Sha256 = "1ee975d96f44ccf2c3bfe37825c1c1f0b089f5703c7a12a84b1f0a3bce004533"
                }
            ]
        },
        new ModelDescriptor
        {
            Id = SKeyId,
            Name = "S-KEY",
            Purpose = ModelPurpose.Key,
            Description = "调性检测，替代内置算法，用于避开调性冲突的混音",
            License = "MIT",
            Homepage = "https://github.com/deezer/skey",
            InputName = "audio",
            InputShape = [1, -1],
            OutputNames = ["scores"],
            Files = (ModelFile[])
            [
                new ModelFile
                {
                    Name = "skey.onnx",
                    Repository = "aaatmy/skey-onnx",
                    RemotePath = "skey.onnx",
                    Size = 324536,
                    Sha256 = "50bac66d5b02868afad07a0b159f08495cc3f5d8492654587e828ed4686412b1",
                    IsModel = true
                }
            ]
        },
        new ModelDescriptor
        {
            Id = VocalId,
            Name = "UVR MDX-Net Voc FT",
            Purpose = ModelPurpose.Vocal,
            Description = "人声检测，补充歌词时间轴，避免两首歌的人声在过渡中重叠",
            License = "MIT (UVR)",
            Homepage = "https://github.com/Anjok07/ultimatevocalremovergui",
            InputName = "input",
            InputShape = [-1, 4, 3072, 256],
            OutputNames = ["output"],
            FixedDimensions = new Dictionary<string, long> { ["batch_size"] = 1 },
            Files = (ModelFile[])
            [
                new ModelFile
                {
                    Name = "UVR-MDX-NET-Voc_FT.onnx",
                    Repository = "Blane187/all_public_uvr_models",
                    RemotePath = "UVR-MDX-NET-Voc_FT.onnx",
                    Size = 66762490,
                    Sha256 = "534b2070fcc7df514b13ef660dc8cbb328679c2374d04354a5c42bb14ecce111",
                    IsModel = true
                }
            ]
        }
    ];

    public static ModelDescriptor? Find(string id)
    {
        foreach (var model in All)
            if (model.Id == id) return model;
        return null;
    }
}
