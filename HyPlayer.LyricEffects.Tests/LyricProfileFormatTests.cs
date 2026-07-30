using AwesomeAssertions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using HyPlayer.LyricEffects.Serialization;
using System.Text.Json;

namespace HyPlayer.LyricEffects.Tests;

public class LyricProfileFormatTests
{
    [Test]
    public async Task JsonRoundTrip_ShouldPreserveOrderDisabledScriptAndExtensionData()
    {
        var source = LyricEffectPresets.CreateDefaultProfile();
        source.Operations[1].IsEnabled = false;
        source.Operations.Add(new LyricRenderOperationDefinition
        {
            TypeId = "vendor.effect.future",
            DisplayName = "Future",
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["vendorData"] = JsonDocument.Parse("{\"answer\":42}").RootElement.Clone()
            }
        });

        var json = JsonSerializer.Serialize(source, LyricEffectJsonContext.Default.LyricEffectProfileDocument);
        json.Should().Contain("\"format\": \"hyplayer.lyric-effects\"");
        var result = JsonSerializer.Deserialize(json, LyricEffectJsonContext.Default.LyricEffectProfileDocument)!;

        result.Operations.Select(item => item.TypeId).Should().Equal(source.Operations.Select(item => item.TypeId));
        result.Operations[1].IsEnabled.Should().BeFalse();
        result.Operations.First(item => item.TypeId == LyricBuiltInOperationTypes.DrawScript).Script.Should().NotBeEmpty();
        result.Operations[^1].ExtensionData!["vendorData"].GetProperty("answer").GetInt32().Should().Be(42);
        await Task.CompletedTask;
    }

    [Test]
    public async Task OnlySchemaV3_ShouldBeAccepted()
    {
        var old = LyricEffectPresets.CreateDefaultProfile();
        old.SchemaVersion = 2;
        old.ExpressionApiVersion = 2;
        ((Action)(() => LyricEffectProfileValidation.MigrateToCurrent(old)))
            .Should().Throw<NotSupportedException>();

        var future = LyricEffectPresets.CreateDefaultProfile();
        future.SchemaVersion = 4;
        var action = () => LyricEffectProfileValidation.MigrateToCurrent(future);
        action.Should().Throw<NotSupportedException>();

        var current = LyricEffectPresets.CreateDefaultProfile();
        LyricEffectProfileValidation.MigrateToCurrent(current).SchemaVersion.Should().Be(3);
        await Task.CompletedTask;
    }

    [Test]
    public async Task FocusedTextRoundTrip_ShouldKeepRevealAndLiftIndependent()
    {
        var source = LyricEffectPresets.CreateDefaultProfile();
        var reveal = source.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal);
        reveal.Options["revealMode"] = nameof(HighlightRevealMode.RectangleClip);
        var lift = source.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift);
        lift.Options["motion"] = "Pulse";
        lift.Targets.Should().Contain(FocusedTextTargets.LyricCurrentHighlighted);
        lift.Targets.Should().Contain(FocusedTextTargets.LyricCurrentPending);

        var json = JsonSerializer.Serialize(source, LyricEffectJsonContext.Default.LyricEffectProfileDocument);
        var result = JsonSerializer.Deserialize(json, LyricEffectJsonContext.Default.LyricEffectProfileDocument)!;

        result.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal)
            .Options["revealMode"].Should().Be(nameof(HighlightRevealMode.RectangleClip));
        result.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift).Options["motion"].Should().Be("Pulse");
        await Task.CompletedTask;
    }

    [Test]
    public async Task TransitionExpressions_ShouldRoundTripForScalarAndColorParameters()
    {
        var source = LyricEffectPresets.CreateDefaultProfile();
        var opacity = source.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.Opacity);
        opacity.Parameters["opacity"].Transition = new LyricTransitionDefinition
        {
            DurationMs = "word.DurationMs * 0.25",
            EasingId = "elastic",
            Mode = "out",
            Arguments =
            {
                ["springiness"] = "6 + glyph.IndexInWord",
                ["oscillations"] = "1.5"
            }
        };
        var color = new FocusedTextOperationDefinition
        {
            TypeId = FocusedTextBuiltInOperationTypes.Color,
            DisplayName = "颜色",
            Targets = [FocusedTextTargets.LyricHighlighted],
            Parameters =
            {
                ["color"] = new LyricOperationParameterDefinition
                {
                    Expression = "fx.Rgba(255, 80, 40, 1)",
                    Transition = new LyricTransitionDefinition { DurationMs = "line.DurationMs / 8" }
                }
            }
        };
        source.FocusedText.Operations.Add(color);

        var json = JsonSerializer.Serialize(source, LyricEffectJsonContext.Default.LyricEffectProfileDocument);
        var result = JsonSerializer.Deserialize(json, LyricEffectJsonContext.Default.LyricEffectProfileDocument)!;

        var opacityTransition = result.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.Opacity).Parameters["opacity"].Transition!;
        opacityTransition.DurationMs.Should().Be("word.DurationMs * 0.25");
        opacityTransition.Arguments["springiness"].Should().Be("6 + glyph.IndexInWord");
        opacityTransition.Arguments["oscillations"].Should().Be("1.5");
        result.FocusedText.Operations.Single(item => item.InstanceId == color.InstanceId)
            .Parameters["color"].Transition!.DurationMs.Should().Be("line.DurationMs / 8");
        await Task.CompletedTask;
    }

    [Test]
    public async Task HighlightReveal_ShouldBeRequiredEnabledAndUnique()
    {
        var missing = LyricEffectPresets.CreateDefaultProfile();
        missing.FocusedText.Operations.RemoveAll(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal);
        LyricEffectProfileValidation.Validate(missing)
            .Should().Contain(item => item.Message.Contains("且只能包含一个"));

        var disabled = LyricEffectPresets.CreateDefaultProfile();
        disabled.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal).IsEnabled = false;
        LyricEffectProfileValidation.Validate(disabled)
            .Should().Contain(item => item.Property == "isEnabled");

        var duplicate = LyricEffectPresets.CreateDefaultProfile();
        duplicate.FocusedText.Operations.Add(LyricEffectPresets.CreateHighlightReveal());
        LyricEffectProfileValidation.Validate(duplicate)
            .Should().Contain(item => item.Message.Contains("且只能包含一个"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task FocusedUnknownNodeAndExtensions_ShouldRoundTripUnchanged()
    {
        var source = LyricEffectPresets.CreateDefaultProfile();
        source.FocusedText.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["vendorFocused"] = JsonDocument.Parse("{\"enabled\":true}").RootElement.Clone()
        };
        source.FocusedText.Operations.Add(new FocusedTextOperationDefinition
        {
            TypeId = "vendor.focus.future",
            DisplayName = "Future focused node",
            Targets = [FocusedTextTargets.Translation],
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["payload"] = JsonDocument.Parse("[1,2,3]").RootElement.Clone()
            }
        });

        var json = JsonSerializer.Serialize(source, LyricEffectJsonContext.Default.LyricEffectProfileDocument);
        var result = JsonSerializer.Deserialize(json, LyricEffectJsonContext.Default.LyricEffectProfileDocument)!;

        result.FocusedText.ExtensionData!["vendorFocused"].GetProperty("enabled").GetBoolean().Should().BeTrue();
        result.FocusedText.Operations.Single(item => item.TypeId == "vendor.focus.future")
            .ExtensionData!["payload"].GetArrayLength().Should().Be(3);
        await Task.CompletedTask;
    }

    [Test]
    public async Task CloneAndPresetComposition_ShouldKeepUniqueInstanceIds()
    {
        var first = LyricEffectPresets.CloneProfile(LyricEffectPresets.CreateDefaultProfile(), renewInstanceIds: true);
        var second = LyricEffectPresets.CloneProfile(LyricEffectPresets.CreateDefaultProfile(), renewInstanceIds: true);
        var combined = first.Operations.Concat(second.Operations).ToList();
        combined.Select(item => item.InstanceId).Should().OnlyHaveUniqueItems();
        combined.Count(item => item.TypeId == LyricBuiltInOperationTypes.Opacity).Should().Be(2);
        await Task.CompletedTask;
    }

    [Test]
    public async Task AllProfilePresets_ShouldContainRequiredDrawingNodes()
    {
        foreach (var preset in LyricEffectPresets.ProfilePresets)
        {
            preset.Profile.Operations.Count(item => item.TypeId == LyricBuiltInOperationTypes.Source).Should().Be(1);
            preset.Profile.Operations.Count(item => item.TypeId == LyricBuiltInOperationTypes.Debug).Should().Be(1);
            LyricEffectProfileValidation.Validate(preset.Profile).Should().BeEmpty();
        }

        foreach (var preset in LyricEffectPresets.FocusedTextProfilePresets)
        {
            var profile = LyricEffectPresets.CreateDefaultProfile();
            profile.FocusedText = LyricEffectPresets.CloneFocusedText(preset.Profile);
            profile.FocusedText.Operations.Count(item =>
                item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal).Should().Be(1);
            LyricEffectProfileValidation.Validate(profile).Should().BeEmpty();
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task LimitsAndDuplicateIds_ShouldBeRejected()
    {
        var profile = new LyricEffectProfileDocument
        {
            Operations = Enumerable.Range(0, LyricEffectProfileValidation.MaximumOperationCount + 1)
                .Select(_ => LyricEffectPresets.CreateOpacity()).ToList()
        };
        LyricEffectProfileValidation.Validate(profile).Should().NotBeEmpty();

        profile = LyricEffectPresets.CreateDefaultProfile();
        profile.Operations[1].InstanceId = profile.Operations[0].InstanceId;
        LyricEffectProfileValidation.Validate(profile).Should().Contain(item => item.Message.Contains("instanceId"));
        await Task.CompletedTask;
    }
}
