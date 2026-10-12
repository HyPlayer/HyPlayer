using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using HyPlayer.Playback.Tests;
using Microsoft.Graphics.Canvas;
using System.Diagnostics;
using Windows.UI;
#if BEFORE
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricRenderer.Pipeline;
#endif

Win2DTestHost.Initialize();
using var device = new CanvasDevice();
using var target = new CanvasRenderTarget(device, 800, 600, 96);
var preset = args.Length > 0 ? args[0] : "SoftDepth";
var scenario = args.Length > 1 ? args[1] : "words";
var context = new RenderContext { ItemWidth = 660, ViewWidth = 800, ViewHeight = 600, CurrentKeyframe = 0, CurrentLyricTime = 1000, PreferTypography = new() { TransliterationFontSize = 14 } };
#if BEFORE
var registry = new LyricRenderOperationRegistry(new LyricExpressionCompiler(), new LyricDrawScriptParser(), new LyricDrawCommandRegistry());
var profile = LyricEffectPresets.ProfilePresets.Single(p => p.Name == (preset switch { "Clear" => "清晰", "Layered" => "立体层叠", "None" => "无特效", _ => "柔和景深" })).Profile;
profile.FocusedText = LyricEffectPresets.CreateSoftLiftFocusedText();
context.EffectProfile = registry.Compile(profile).Profile ?? throw new Exception("Profile did not compile.");
#else
context.Settings = new(LyricLineEffectSettings.ForPreset(Enum.Parse<LyricLinePreset>(preset)), new());
#endif
context.IsScrolling = scenario == "scroll";
var text = scenario == "long" ? string.Concat(Enumerable.Repeat("这一行包含连续汉字和 office ligatures العربية hello ", 4)) : "沿着光亮缓缓向前 office العربية hello";
var lines = Enumerable.Range(0, scenario == "sublines" ? 9 : 5).Select(i => new TextRenderingLyricLine
{
    Text = text, Translation = "Translation stays visible", StartTime = i == 0 ? 0 : 5000, EndTime = i == 0 ? 4000 : 9000,
    GroupIndex = i, RuntimeIndex = i,
    Tokens = scenario == "words" ? new() { new("沿着光亮", 0, 1000, null), new("缓缓向前", 1000, 2200, null), new(" office العربية hello", 2200, 4000, null) } : new()
}).ToArray();
context.CurrentLyricLine = lines[0];
context.CurrentLyricLineIndex = 0;
var offsets = lines.Select((_,i) => new LineRenderOffset { X = 35, Y = i * 105 + 40 }).ToArray();
context.RenderOffsets.AddRange(offsets);
using (var session = target.CreateDrawingSession())
foreach (var line in lines) { line.OnRenderSizeChanged(session, context); line.OnKeyFrame(session, context); }
void Frame(int index)
{
    context.CurrentLyricTime = 1000 + index % 1000;
    using var session = target.CreateDrawingSession();
    session.Clear(Colors.Black);
    for (var i = 0; i < lines.Length; i++) lines[i].Render(session, offsets[i], context);
}
for (var i = 0; i < 300; i++) Frame(i);
_ = target.GetPixelBytes();
Console.WriteLine("round,median_us,p95_us,bytes_per_frame,cpu_ms,private_bytes");
for (var round = 0; round < (args.Length > 2 ? int.Parse(args[2]) : 5); round++)
{
    var samples = new double[200];
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
    for (var i = 0; i < samples.Length; i++)
    {
        var start = Stopwatch.GetTimestamp(); Frame(i);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
    }
    var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)samples.Length;
    _ = target.GetPixelBytes(); // drain GPU outside CPU submission measurements
    var usedCpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
    Array.Sort(samples);
    Console.WriteLine($"{round},{samples[100]:F2},{samples[190]:F2},{bytes:F2},{usedCpu:F2},{Process.GetCurrentProcess().PrivateMemorySize64}");
}
await target.SaveAsync(Path.Combine(AppContext.BaseDirectory, $"{preset}-{scenario}.png"), CanvasBitmapFileFormat.Png);
foreach (var line in lines) line.Dispose();
