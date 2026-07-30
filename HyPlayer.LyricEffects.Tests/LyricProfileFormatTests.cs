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
    public async Task VersionMigrationAndFutureVersionRejection_ShouldBeExplicit()
    {
        var old = LyricEffectPresets.CreateDefaultProfile();
        old.SchemaVersion = 0;
        old.ExpressionApiVersion = 1;
        old.Operations.RemoveAll(item =>
            item.TypeId is LyricBuiltInOperationTypes.Source or LyricBuiltInOperationTypes.Debug);
        var migrated = LyricEffectProfileValidation.MigrateToCurrent(old);
        migrated.SchemaVersion.Should().Be(2);
        migrated.ExpressionApiVersion.Should().Be(2);
        migrated.FocusedText.Operations.Should().Contain(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift);
        migrated.Operations.Count(item => item.TypeId == LyricBuiltInOperationTypes.Source).Should().Be(1);
        migrated.Operations.Count(item => item.TypeId == LyricBuiltInOperationTypes.Debug).Should().Be(1);
        migrated.Operations.First().TypeId.Should().Be(LyricBuiltInOperationTypes.Source);
        migrated.Operations.Last().TypeId.Should().Be(LyricBuiltInOperationTypes.Debug);

        var future = LyricEffectPresets.CreateDefaultProfile();
        future.SchemaVersion = 3;
        var action = () => LyricEffectProfileValidation.MigrateToCurrent(future);
        action.Should().Throw<NotSupportedException>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task FocusedTextRoundTrip_ShouldKeepRevealAndLiftIndependent()
    {
        var source = LyricEffectPresets.CreateDefaultProfile();
        source.FocusedText.HighlightRevealMode = HighlightRevealMode.RectangleClip;
        var lift = source.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift);
        lift.Options["motion"] = "Pulse";
        lift.Targets.Should().Contain(FocusedTextTargets.LyricCurrentHighlighted);
        lift.Targets.Should().Contain(FocusedTextTargets.LyricCurrentPending);

        var json = JsonSerializer.Serialize(source, LyricEffectJsonContext.Default.LyricEffectProfileDocument);
        var result = JsonSerializer.Deserialize(json, LyricEffectJsonContext.Default.LyricEffectProfileDocument)!;

        result.FocusedText.HighlightRevealMode.Should().Be(HighlightRevealMode.RectangleClip);
        result.FocusedText.Operations.Single(item =>
            item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift).Options["motion"].Should().Be("Pulse");
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
