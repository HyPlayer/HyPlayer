using ALRC.Abstraction;
using HyPlayer.Domain.Lyrics;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using TUnit.Core;
using Windows.UI.Xaml;

namespace HyPlayer.Playback.Tests;

public sealed class LrcConverterTests
{
    [Test]
    public void WordsAndExplicitLineTimes_ShouldTakePriority()
    {
        var source = File(
            new ALRCLine
            {
                Start = 500,
                End = 5000,
                RawText = "ignored",
                Transliteration = "ignored transliteration",
                Words =
                [
                    Word("Hello ", 1000, 2000, "he "),
                    Word("world", 2500, 4000)
                ]
            },
            new ALRCLine { Start = 6000, RawText = "next" });

        var result = LrcConverter.Convert(source);
        var first = (TextRenderingLyricLine)result[0];
        var second = (TextRenderingLyricLine)result[1];

        Ensure(first.Text == "Hello world", "Words must replace RawText when present.");
        Ensure(first.Transliteration == "he ", "Any Word transliteration must replace line transliteration.");
        Ensure(first.StartTime == 500 && first.EndTime == 5000,
            "Explicit line times must remain authoritative even outside the Word window.");
        Ensure(second.StartTime == 6000 && second.EndTime == 9000,
            "Lyric duration must close the final untimed line.");
    }

    [Test]
    public void ParentGroups_ShouldUseRootOrderAndKeepRootFirst()
    {
        var child = new ALRCLine { Id = "child", ParentLineId = "root", Start = 1200, End = 1800, RawText = "child" };
        var orphan = new ALRCLine { Id = "orphan", ParentLineId = "missing", Start = 2000, End = 2500, RawText = "orphan" };
        var root = new ALRCLine { Id = "root", Start = 1000, End = 3000, RawText = "root" };
        var grandchild = new ALRCLine { Id = "grand", ParentLineId = "child", Start = 1600, End = 2200, RawText = "grand" };

        var result = LrcConverter.Convert(File(child, orphan, root, grandchild));

        Ensure(result.Select(line => line.SourceLine?.Id).SequenceEqual(
                (string?[])["orphan", "root", "child", "grand"]),
            "Groups must be ordered by root position, with the root before every descendant.");
        Ensure(result[1].GroupIndex == result[2].GroupIndex && result[2].GroupIndex == result[3].GroupIndex,
            "Nested descendants must resolve to the top-level root Group.");
        Ensure(result[1].GroupStartTime == 1000 && result[1].GroupEndTime == 3000,
            "Group activity must use the continuous earliest-start/latest-end envelope.");
        Ensure(result[0].GroupIndex != result[1].GroupIndex,
            "A missing parent must leave the physical line in an independent Group.");
    }

    [Test]
    public void Style_ShouldApplyToBlankProgressLinesWithoutMappingAccent()
    {
        var style = new ALRCStyle
        {
            Id = "centered",
            Position = ALRCStylePosition.Center,
            Color = "#80402010",
            Type = ALRCStyleAccent.Emphasise,
            HiddenOnBlur = true
        };
        var source = File(new ALRCLine
        {
            Start = 1000,
            End = 3000,
            RawText = " ",
            LineStyle = style.Id
        });
        source.Header = new ALRCHeader { Styles = [style] };

        var line = LrcConverter.Convert(source).Single();

        Ensure(line is ProgressBarRenderingLyricLine, "A long blank ALRC line must remain a progress line.");
        Ensure(line.SourceLine == source.Lines[0] && line.SourceStyle == style,
            "Runtime lines must retain the original ALRC line and resolved Style.");
        Ensure(line.Typography?.Alignment == TextAlignment.Center,
            "Style Position must apply to progress lines.");
        Ensure(line.Typography?.FocusingColor is { A: 0x80, R: 0x40, G: 0x20, B: 0x10 },
            "Style Color must become the line FocusingColor.");
        Ensure(line.Typography?.FontWeight is null,
            "Style Accent must not be mapped to typography.");
        Ensure(line.HiddenOnBlur, "HiddenOnBlur must belong to every physical ALRC line type.");
    }

    [Test]
    public void BackgroundAccent_ShouldSelectSublineTypographyRegardlessOfParentRelationship()
    {
        var backgroundStyle = new ALRCStyle
        {
            Id = "background",
            Type = ALRCStyleAccent.Background
        };
        var normalStyle = new ALRCStyle
        {
            Id = "normal",
            Type = ALRCStyleAccent.Normal
        };
        var source = File(
            new ALRCLine
            {
                Id = "background-vocal",
                RawText = "background",
                LineStyle = backgroundStyle.Id
            },
            new ALRCLine
            {
                Id = "parent",
                RawText = "parent"
            },
            new ALRCLine
            {
                Id = "child",
                ParentLineId = "parent",
                RawText = "child",
                LineStyle = normalStyle.Id
            });
        source.Header = new ALRCHeader { Styles = [backgroundStyle, normalStyle] };
        var lines = LrcConverter.Convert(source);
        var context = new RenderContext
        {
            PreferTypography = new() { LyricFontSize = 40 },
            SublineTypography = new() { LyricFontSize = 18 }
        };

        Ensure(lines[0].TypographySelector(value => value?.LyricFontSize, context) == 18,
            "Background-accented lines must use the configured subline typography without requiring ParentLineId.");
        Ensure(lines[2].TypographySelector(value => value?.LyricFontSize, context) == 40,
            "ParentLineId alone must not make a normally styled line use subline typography.");
    }

    [Test]
    public void ZeroSublineSizes_ShouldUseHalfOfMainLyricSize()
    {
        var renderer = new LyricRenderView();

        renderer.ChangeRenderFontSize(40, 16, 14);

        Ensure(renderer.Context.SublineTypography.LyricFontSize == 20,
            "A zero subline lyric size must use half of the main lyric size.");
        Ensure(renderer.Context.SublineTypography.TranslationFontSize == 20,
            "A zero subline translation size must use half of the main lyric size.");
        Ensure(renderer.Context.SublineTypography.TransliterationFontSize == 20,
            "A zero subline transliteration size must use half of the main lyric size.");
    }

    private static ALRCFile File(params ALRCLine[] lines) => new()
    {
        Schema = "https://github.com/Steve-xmh/amll-ttml-tool/alrc.schema.json",
        LyricInfo = new ALRCLyricInfo { Duration = 9000 },
        Lines = [.. lines]
    };

    private static ALRCWord Word(string text, long start, long end, string? transliteration = null) => new()
    {
        Word = text,
        Start = start,
        End = end,
        Transliteration = transliteration
    };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
