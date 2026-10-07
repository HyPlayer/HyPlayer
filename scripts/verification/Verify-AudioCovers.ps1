# Exercises the production TagLib helper against real MP3/FLAC/JPEG fixtures.
# Requires .NET 10, ffmpeg, and the restored TagLibSharp package.
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('HyPlayer-AudioCovers-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDirectory | Out-Null
$tagLibAssembly = Join-Path $env:USERPROFILE '.nuget/packages/taglibsharp/2.3.0/lib/netstandard2.0/TagLibSharp.dll'
if (-not (Test-Path -LiteralPath $tagLibAssembly)) { throw 'Restore HyPlayer.slnx first.' }
$helperPath = [Security.SecurityElement]::Escape((Join-Path $repoRoot 'HyPlayer/Platform/Storage/Audio/TagLibHelper.cs'))
$storagePath = [Security.SecurityElement]::Escape((Join-Path $repoRoot 'HyPlayer/Platform/Storage/Audio/UwpStorageFileAbstraction.cs'))
$assemblyPath = [Security.SecurityElement]::Escape($tagLibAssembly)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$helperPath" Link="TagLibHelper.cs" />
    <Compile Include="$storagePath" Link="UwpStorageFileAbstraction.cs" />
    <Reference Include="TagLibSharp"><HintPath>$assemblyPath</HintPath></Reference>
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $workDirectory 'Verify.csproj')
@'
using HyPlayer.Platform.Storage.Audio;
using TagLib;
using Windows.Storage;
var folder = args[0];
var coverBytes = System.IO.File.ReadAllBytes(Path.Combine(folder, "cover.jpg"));
using var encoded = new MemoryStream(coverBytes);
encoded.Position = encoded.Length;
var picture = TagLibHelper.CreateCoverPicture(encoded);
if (!picture.Data.Data.SequenceEqual(coverBytes) || picture.Type != PictureType.FrontCover)
    throw new Exception("Encoded JPEG was read incorrectly.");
Console.WriteLine("PASS: encoded JPEG at EOF is read in full as FrontCover");
foreach (var extension in new[] { ".mp3", ".flac" })
{
    var path = Path.Combine(folder, "audio" + extension);
    var storageFile = await StorageFile.GetFileFromPathAsync(path);
    await Task.Run(async () =>
    {
        using var abstraction = await UwpStorageFileAbstraction.OpenAsync(storageFile, writable: true);
        if (!ReferenceEquals(abstraction.ReadStream, abstraction.WriteStream))
            throw new Exception("Expected a single storage handle.");
        using var file = TagLibHelper.Create(abstraction, extension);
        file.Tag.Title = "HyPlayer cover regression";
        TagLibHelper.SetCover(file, picture);
        file.Save();
    });
    using var readAbstraction = await UwpStorageFileAbstraction.OpenAsync(storageFile);
    using var reopened = TagLibHelper.Create(readAbstraction, extension);
    if (reopened.Tag.Pictures.Length != 1) throw new Exception("Expected one cover.");
    var saved = reopened.Tag.Pictures[0];
    if (!saved.Data.Data.SequenceEqual(coverBytes) || saved.Type != PictureType.FrontCover || saved.MimeType != "image/jpeg")
        throw new Exception("Saved picture data/type/MIME is incorrect.");
    if (extension == ".mp3" && ((TagLib.Id3v2.Tag)reopened.GetTag(TagTypes.Id3v2)).Version != 3)
        throw new Exception("Expected ID3v2.3.");
    Console.WriteLine($"PASS: {extension} cover survives save/reopen byte-for-byte");
}
'@ | Set-Content -LiteralPath (Join-Path $workDirectory 'Program.cs')

& ffmpeg -v error -y -f lavfi -i 'color=c=red:s=64x64' -frames:v 1 (Join-Path $workDirectory 'cover.jpg')
if ($LASTEXITCODE) { throw 'JPEG fixture generation failed.' }
foreach ($extension in @('mp3', 'flac')) {
    # Seeded noise makes FLAC large enough to exercise multiple 64 KiB I/O buffers.
    & ffmpeg -v error -y -f lavfi -i 'anoisesrc=color=white:sample_rate=48000:duration=10:seed=71' (Join-Path $workDirectory "audio.$extension")
    if ($LASTEXITCODE) { throw "Audio fixture generation failed: $extension" }
}
$before = @{}
foreach ($extension in @('mp3', 'flac')) {
    $before[$extension] = & ffmpeg -v error -i (Join-Path $workDirectory "audio.$extension") -map 0:a:0 -f hash -hash md5 -
    if ($LASTEXITCODE) { throw "Audio decoding failed: $extension" }
}
& dotnet run --project (Join-Path $workDirectory 'Verify.csproj') --configuration Release --runtime win-x64 -- $workDirectory
if ($LASTEXITCODE) { throw 'Production cover helper regression failed.' }
foreach ($extension in @('mp3', 'flac')) {
    $after = & ffmpeg -v error -i (Join-Path $workDirectory "audio.$extension") -map 0:a:0 -f hash -hash md5 -
    if ($LASTEXITCODE -or $before[$extension] -ne $after) { throw "Audio payload changed: $extension" }
    $extracted = Join-Path $workDirectory "extracted-$extension.jpg"
    & ffmpeg -v error -y -i (Join-Path $workDirectory "audio.$extension") -map 0:v:0 -c copy $extracted
    if ($LASTEXITCODE) { throw "Independent decoder cannot recognize cover: $extension" }
    if ((Get-FileHash -LiteralPath $extracted).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workDirectory 'cover.jpg')).Hash) {
        throw "Extracted cover differs: $extension"
    }
    Write-Output "PASS: ffmpeg recognizes $extension cover; decoded audio MD5 unchanged"
}
Write-Output "Fixtures: $workDirectory"
