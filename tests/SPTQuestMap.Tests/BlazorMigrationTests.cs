using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SPTQuestMap.Components;
using SPTQuestMap.Components.Pages;
using SPTQuestMap.Presentation;
using SPTQuestMap.Services;
using SPTarkov.Server.Core.Models.Enums;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoreProgressRules = SPTQuestMap.Core.Rules.QuestProgressRules;

namespace SPTQuestMap.Tests;

public sealed class BlazorMigrationTests
{
    [Test]
    public void QuestMapRouteIsOwnedByRazor()
    {
        var razorRoutes = typeof(QuestMap).GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), true).Cast<Microsoft.AspNetCore.Components.RouteAttribute>().Select(attribute => attribute.Template).ToArray();
        Assert.That(razorRoutes, Does.Contain("/questmap"));
    }

    [Test]
    public void QuestMapAssemblyDoesNotExposeAnMvcController()
    {
        var controllerTypes = typeof(QuestMap).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type));
        Assert.That(controllerTypes, Is.Empty);
    }

    [Test]
    public void QuestMetadataResolutionUsesThePostDatabaseStartupBoundary()
    {
        Assert.That(
            typeof(SPTarkov.Server.Core.DI.IOnLoad).IsAssignableFrom(typeof(QuestMapDataService)),
            Is.True,
            "Quest metadata must resolve only after SPT's database and post-DB mod loaders have run.");
    }

    [Test]
    public void SptMetadataVersionMatchesAssemblyVersion()
    {
        var assembly = typeof(QuestMapModMetadata).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var metadata = new QuestMapModMetadata();

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Version.ToString(), Is.EqualTo(informationalVersion));
            Assert.That(assembly.GetName().Version, Is.EqualTo(System.Version.Parse($"{informationalVersion}.0")));
        });
    }

    [Test]
    public void RendererAssetsAreEmbeddedAndColorsComeFromCssTheme()
    {
        const string colorLiteralPattern = @"(?:#[0-9a-fA-F]{3,8}\b|rgba?\s*\(|hsla?\s*\()";

        Assert.Multiple(() =>
        {
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-state-locked"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-renderer-outline-selected"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-renderer-repeatable-divider"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-renderer-compare-difference"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-renderer-compare-difference-text"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-dismiss-layer:hover:not(:disabled)"),
                "The full-screen profile-menu dismiss button must override the global button hover background.");
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("getComputedStyle"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("composeRepeatableLayout"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("effectiveQuestState"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("drawCompareNode"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("comparisonStateById"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("comparisonById"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("comparisonReason"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("displayMapReferences"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("usesActualMaps"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("'marathon'"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("drawAngledMapSlices"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("mapLeft=preserveQuestArtwork?layer.width*.6:0"),
                "Quest artwork must remain visible while map banners occupy angled slices in only the right 40 percent of a card.");
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("ctx.moveTo(leftTop,0)"),
                "Card slice boundaries must run from bottom-left to top-right.");
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("rightTop=index===maps.length-1?outerRight"),
                "The final map slice must meet the card's full right edge without exposing the quest artwork in either corner.");
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Not.Contain("fadeOverQuest"),
                "A single inferred map must use an explicit right-side slice rather than an unreliable soft fade.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".location-detail-images"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--location-map-count"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("dimmed?dimAlpha"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Not.Contain("drawDifferenceMarker"));
            Assert.That(QuestMapEmbeddedAssets.RendererModuleDataUrl, Does.StartWith("data:text/javascript;base64,"));
            Assert.That(Regex.IsMatch(QuestMapEmbeddedAssets.RendererSource, colorLiteralPattern), Is.False,
                "Renderer colors must be supplied by CSS custom properties, not JavaScript literals.");
        });
    }

    [Test]
    public void GraphSnapshotCarriesTheAlreadyLoadedProfiles()
    {
        var state = CreateComparisonState();
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], new Dictionary<string, string>()));

        var snapshot = state.BuildGraphSnapshot(localizer);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Topology, Is.SameAs(state.Topology));
            Assert.That(snapshot.Profile, Is.SameAs(state.Profile));
            Assert.That(snapshot.ComparisonProfile, Is.SameAs(state.ComparisonProfile));
            Assert.That(snapshot.View.ProfileId, Is.EqualTo("profile"));
            Assert.That(snapshot.View.ComparisonProfileId, Is.EqualTo("comparison"));
            Assert.That(snapshot.View.CompareMode, Is.True);
        });
    }

    [Test]
    public void ProfileSelectorNamesUseTheFullContentAreaAboveBackgroundLevels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("grid-template-columns: 48px minmax(0,1fr) 20px;"),
                "The ordinary selector must not reserve a foreground column for the level watermark.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("grid-template-columns: 24px 40px minmax(0,1fr) 18px;"),
                "Comparison selectors must not reserve a foreground column for the level watermark.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-name, .profile-option-name { position: relative; z-index: 1;"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("white-space: normal;"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-level, .profile-option-level { position: absolute; z-index: 0;"),
                "Level numbers must be decorative background content rather than layout columns.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("inset-block: 0; display: flex;"),
                "The oversized level glyph must be clipped vertically by the selector height.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("font: 700 5rem/1 Georgia,serif;"),
                "The background level must be oversized enough to clip at both vertical edges.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("opacity: .28; pointer-events: none;"),
                "The watermark must remain readable against the live selector background.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-level { right: calc(.55rem + 20px + .65rem - 13px); width: 90px; }"),
                "The ordinary watermark must preserve the original right-side center point.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".compare-profile .profile-level { right: calc(.55rem + 18px + .42rem - 13px); }"),
                "Comparison watermarks must preserve the original right-side center point.");
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-option-level { right: 0; width: 84px; font-size: 4.5rem; opacity: .25;"),
                "Dropdown watermarks must remain oversized on the right.");
        });
    }

    [Test]
    public void GraphSnapshotSerializesComparisonCategoriesForJavaScript()
    {
        var state = CreateComparisonState();
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], QuestMapUiCatalog.English));

        var json = JsonSerializer.Serialize(state.BuildGraphSnapshot(localizer), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"categories\":[\"Status\"]"));
            Assert.That(json, Does.Contain("\"hasDifferences\":true"));
        });
    }

    [Test]
    public void PageSettingsRoundTripInCSharp()
    {
        var expected = new QuestMapSettings("profile", "en", true, false, false, true, "search", "trader", new Dictionary<string, QuestProfileUiSettings> { ["profile"] = new("quest", "focus") })
        {
            ShowRepeatables = false,
            CompareEnabled = true,
            ComparisonProfileId = "comparison",
            ComparisonFilter = QuestComparisonFilter.Objectives,
            DetailTextTab = QuestDetailTextTab.Summary,
            ComparePairs = new Dictionary<string, QuestProfileUiSettings> { ["profile|comparison"] = new("other-quest", null) },
        };

        var actual = QuestMapSettingsStorage.Deserialize(QuestMapSettingsStorage.Serialize(expected), null);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.SelectedProfileId, Is.EqualTo(expected.SelectedProfileId));
            Assert.That(actual.Language, Is.EqualTo(expected.Language));
            Assert.That(actual.ShowAllFuture, Is.EqualTo(expected.ShowAllFuture));
            Assert.That(actual.ShowFinished, Is.EqualTo(expected.ShowFinished));
            Assert.That(actual.ShowRepeatables, Is.EqualTo(expected.ShowRepeatables));
            Assert.That(actual.LevelEligibleOnly, Is.EqualTo(expected.LevelEligibleOnly));
            Assert.That(actual.InProgressExpanded, Is.EqualTo(expected.InProgressExpanded));
            Assert.That(actual.Search, Is.EqualTo(expected.Search));
            Assert.That(actual.TraderFilter, Is.EqualTo(expected.TraderFilter));
            Assert.That(actual.Profiles, Is.EquivalentTo(expected.Profiles));
            Assert.That(actual.CompareEnabled, Is.True);
            Assert.That(actual.ComparisonProfileId, Is.EqualTo("comparison"));
            Assert.That(actual.ComparisonFilter, Is.EqualTo(QuestComparisonFilter.Objectives));
            Assert.That(actual.DetailTextTab, Is.EqualTo(QuestDetailTextTab.Summary));
            Assert.That(actual.ComparePairs, Is.EquivalentTo(expected.ComparePairs));
        });
    }

    [Test]
    public async Task QuestDetailsShowsPersistentTabsOnlyWhenSummaryExists()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var summarized = Node("summarized", "Summarized", "trader-a") with { Summary = "Short user summary." };
        var plain = Node("plain", "Plain", "trader-a");
        var topology = new QuestTopologyDto("version", [summarized, plain], [], [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 0, false, false,
            [State("summarized", "Available"), State("plain", "Available")], [], ["summarized", "plain"], ["summarized", "plain"]);
        var state = new QuestMapPageState();
        state.SetData(topology, profile);
        state.SetDetailTextTab(QuestDetailTextTab.Summary);
        state.SelectQuest("summarized");
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], QuestMapUiCatalog.English));

        var summarizedHtml = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<QuestDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestDetails.State)] = state,
                [nameof(QuestDetails.Localizer)] = localizer,
            }))).ToHtmlString());

        state.SelectQuest("plain");
        var plainHtml = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<QuestDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestDetails.State)] = state,
                [nameof(QuestDetails.Localizer)] = localizer,
            }))).ToHtmlString());
        var rendererPayload = JsonSerializer.Serialize(state.BuildGraphSnapshot(localizer), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Multiple(() =>
        {
            Assert.That(summarizedHtml, Does.Contain("role=\"tablist\""));
            Assert.That(summarizedHtml, Does.Contain("Short user summary."));
            Assert.That(summarizedHtml, Does.Contain("static, user-generated summary"));
            Assert.That(plainHtml, Does.Not.Contain("role=\"tablist\""));
            Assert.That(state.DetailTextTab, Is.EqualTo(QuestDetailTextTab.Summary), "Selecting a quest without a summary must not reset the remembered tab.");
            Assert.That(rendererPayload, Does.Not.Contain("Short user summary."), "Summary prose belongs to Blazor details and must not inflate the Canvas payload.");
        });
    }

    [Test]
    public void VersionTwoSettingsMigrateWithComparisonDisabled()
    {
        const string versionTwo = """{"selectedProfileId":"profile","language":"en","showFinished":true,"showRepeatables":false,"levelEligibleOnly":true,"profiles":{}}""";

        var actual = QuestMapSettingsStorage.Deserialize(null, versionTwo, null);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.SelectedProfileId, Is.EqualTo("profile"));
            Assert.That(actual.ShowRepeatables, Is.False);
            Assert.That(actual.CompareEnabled, Is.False);
            Assert.That(actual.ComparisonProfileId, Is.Null);
            Assert.That(actual.ComparisonFilter, Is.EqualTo(QuestComparisonFilter.AllQuests));
        });
    }

    [Test]
    public void VersionThreeDifferencesOnlyMigratesToComparisonFilter()
    {
        const string versionThree = """{"selectedProfileId":"profile","language":"en","compareEnabled":true,"comparisonProfileId":"comparison","differencesOnly":true,"profiles":{}}""";

        var actual = QuestMapSettingsStorage.Deserialize(null, versionThree, null, null);

        Assert.That(actual?.ComparisonFilter, Is.EqualTo(QuestComparisonFilter.AllChanges));
    }

    [Test]
    public void ComparisonClassifiesEveryDifferenceCategorySymmetrically()
    {
        var state = CreateComparisonState();

        Assert.Multiple(() =>
        {
            Assert.That(state.GetComparison("same").Categories, Is.Empty);
            Assert.That(state.GetComparison("state-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Status }));
            Assert.That(state.GetComparison("objective-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Objectives }));
            Assert.That(state.GetComparison("wait-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.AvailableAfter }));
            Assert.That(state.GetComparison("exclusion-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Exclusion }));
            Assert.That(state.GetComparison("combined-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Status, QuestDifferenceCategory.Objectives }));
            Assert.That(state.GetComparison("primary-only").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.PrimaryOnly }));
            Assert.That(state.GetComparison("comparison-only").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.ComparisonOnly }));
        });

        state.SetData(state.Topology!, state.ComparisonProfile, state.Profile);

        Assert.Multiple(() =>
        {
            Assert.That(state.GetComparison("same").Categories, Is.Empty);
            Assert.That(state.GetComparison("state-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Status }));
            Assert.That(state.GetComparison("objective-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Objectives }));
            Assert.That(state.GetComparison("wait-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.AvailableAfter }));
            Assert.That(state.GetComparison("exclusion-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Exclusion }));
            Assert.That(state.GetComparison("combined-difference").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.Status, QuestDifferenceCategory.Objectives }));
            Assert.That(state.GetComparison("primary-only").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.ComparisonOnly }));
            Assert.That(state.GetComparison("comparison-only").Categories, Is.EqualTo(new[] { QuestDifferenceCategory.PrimaryOnly }));
        });
    }

    [Test]
    public void OvercappedObjectiveDoesNotCreateAComparisonDifferenceInEitherDirection()
    {
        var topology = new QuestTopologyDto("version", [Node("quest", "Quest", "trader-a")], [],
            [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        var primaryObjective = new ObjectiveProgressDto(
            "objective",
            true,
            CoreProgressRules.CapCurrent(2, 1),
            1,
            true
        );
        var comparisonObjective = new ObjectiveProgressDto(
            "objective",
            true,
            CoreProgressRules.CapCurrent(1, 1),
            1,
            true
        );
        var primary = new ProfileStateDto("profile", "Primary", "Usec", 10, 0, false, false,
            [State("quest", "InProgress") with { Objectives = [primaryObjective] }], [], ["quest"], ["quest"]);
        var comparison = new ProfileStateDto("comparison", "Comparison", "Bear", 10, 0, false, false,
            [State("quest", "InProgress") with { Objectives = [comparisonObjective] }], [], ["quest"], ["quest"]);
        var state = new QuestMapPageState();

        state.SetData(topology, primary, comparison);
        Assert.That(state.GetComparison("quest").Categories, Is.Empty);

        state.SetData(topology, comparison, primary);
        Assert.That(state.GetComparison("quest").Categories, Is.Empty);
    }

    [Test]
    public void ComparisonVisibilityUsesUnionAndSymmetricFinishedAndLevelFilters()
    {
        var state = CreateComparisonState();
        state.SetShowFinished(false);
        state.SetLevelEligibleOnly(true);

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("primary-only"));
            Assert.That(state.VisibleIds, Does.Contain("comparison-only"));
            Assert.That(state.VisibleIds, Does.Contain("finished-active"), "One active side must keep the quest visible.");
            Assert.That(state.VisibleIds, Does.Not.Contain("both-finished"));
            Assert.That(state.VisibleIds, Does.Contain("level-available"), "One eligible side must keep the quest visible.");
            Assert.That(state.VisibleIds, Does.Not.Contain("both-level-gated"));
        });

        state.SetComparisonFilter(QuestComparisonFilter.AllChanges);
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Not.Contain("same"));
            Assert.That(state.VisibleIds, Does.Contain("state-difference"));
            Assert.That(state.VisibleIds, Does.Contain("objective-difference"));
        });

        state.SetData(state.Topology!, state.ComparisonProfile, state.Profile);
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("finished-active"), "Swapping A/B must preserve the active/finished boundary.");
            Assert.That(state.VisibleIds, Does.Contain("level-available"), "Swapping A/B must preserve the gated/eligible boundary.");
        });
    }

    [Test]
    public void ComparisonCategoryFilterShowsOnlyThatCategoryAndSelectionContext()
    {
        var state = CreateComparisonState();

        state.SetComparisonFilter(QuestComparisonFilter.Objectives);

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("objective-difference"));
            Assert.That(state.VisibleIds, Does.Contain("combined-difference"));
            Assert.That(state.VisibleIds, Does.Not.Contain("state-difference"));
            Assert.That(state.ComparisonCounts[QuestDifferenceCategory.Objectives], Is.EqualTo(2));
        });
    }

    [Test]
    public void ComparisonTraderContextCanBeSeededByEitherProfile()
    {
        var nodes = new[] { Node("source", "Source", "trader-a"), Node("target", "Target", "trader-b") };
        var topology = new QuestTopologyDto("version", nodes, [new QuestEdgeDto("source", "target", ["Success"], 0)],
            [new QuestTraderDto("trader-a", "Trader A", null), new QuestTraderDto("trader-b", "Trader B", null)], [], []);
        var primary = new ProfileStateDto("profile", "Primary", "Usec", 10, 0, false, false,
            [State("source", "Locked"), State("target", "PrerequisiteGated")], [], ["source"], ["source", "target"]);
        var comparison = new ProfileStateDto("comparison", "Comparison", "Usec", 20, 0, false, false,
            [State("source", "InProgress"), State("target", "PrerequisiteGated")], [], ["source"], ["source", "target"]);
        var state = new QuestMapPageState();
        state.SetData(topology, primary, comparison);
        state.SetTraderFilter("trader-a");

        Assert.That(state.VisibleIds, Does.Contain("target"));

        state.SetData(topology, comparison, primary);
        Assert.That(state.VisibleIds, Does.Contain("target"));
    }

    [Test]
    public void AllChangesPreservesSelectedPrerequisitesAndDirectSuccessors()
    {
        var nodes = new[] { Node("a", "A", "trader-a"), Node("b", "B", "trader-a"), Node("c", "C", "trader-a") };
        var topology = new QuestTopologyDto("version", nodes,
            [new QuestEdgeDto("a", "b", ["Success"], 0), new QuestEdgeDto("b", "c", ["Success"], 0)],
            [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        ProfileStateDto Profile(string id) => new(id, id, "Usec", 10, 0, false, false,
            [State("a", "Completed"), State("b", "InProgress"), State("c", "PrerequisiteGated")], [], ["a", "b", "c"], ["a", "b", "c"]);
        var state = new QuestMapPageState();
        state.SetData(topology, Profile("primary"), Profile("comparison"));
        state.SelectQuest("b");
        state.SetComparisonFilter(QuestComparisonFilter.AllChanges);

        Assert.That(state.VisibleIds, Is.EquivalentTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void ComparisonSelectionPersistsPerOrderedProfilePair()
    {
        var state = CreateComparisonState();
        state.SelectQuest("objective-difference");
        var settings = state.CaptureSettings("profile", "en", "comparison", true);
        var restored = CreateComparisonState();

        restored.ApplySettings(settings);

        Assert.That(restored.SelectedId, Is.EqualTo("objective-difference"));
    }

    [Test]
    public void ComparisonModeSuppressesRepeatablesWithoutChangingTheirPreference()
    {
        var ordinary = CreateStateWithRepeatables();
        var comparison = new ProfileStateDto("comparison", "Other", "Usec", 15, 100, false, false,
            [State("normal", "Completed")], [], ["normal"], ["normal"]);

        ordinary.SetData(ordinary.Topology!, ordinary.Profile, comparison);

        Assert.Multiple(() =>
        {
            Assert.That(ordinary.CompareMode, Is.True);
            Assert.That(ordinary.ShowRepeatables, Is.True);
            Assert.That(ordinary.VisibleIds, Does.Not.Contain("daily-available"));
            Assert.That(ordinary.VisibleIds, Does.Not.Contain("daily-completed"));
            Assert.That(ordinary.VisibleIds, Does.Not.Contain("weekly-expired"));
        });

        ordinary.SetData(ordinary.Topology!, ordinary.Profile, null);
        Assert.That(ordinary.VisibleIds, Does.Contain("daily-available"));
    }

    [Test]
    public async Task ComparisonComponentsInstantiateAndRenderAtRuntime()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var state = CreateComparisonState();
        state.SelectQuest("objective-difference");
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], QuestMapUiCatalog.English));
        var profiles = new[]
        {
            new ProfileSummaryDto("profile", "PrimaryProfileWithLongName", "Usec", 10),
            new ProfileSummaryDto("comparison", "ComparisonProfileWithLongName", "Bear", 20),
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var header = await renderer.RenderComponentAsync<QuestMapHeader>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestMapHeader.Localizer)] = localizer,
                [nameof(QuestMapHeader.Profiles)] = profiles,
                [nameof(QuestMapHeader.SelectedProfile)] = profiles[0],
                [nameof(QuestMapHeader.SelectedComparisonProfile)] = profiles[1],
                [nameof(QuestMapHeader.CompareMode)] = true,
                [nameof(QuestMapHeader.Languages)] = Array.Empty<QuestMapLanguageDto>(),
            }));
            var details = await renderer.RenderComponentAsync<QuestDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestDetails.State)] = state,
                [nameof(QuestDetails.Localizer)] = localizer,
            }));
            var filters = await renderer.RenderComponentAsync<QuestMapFilters>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestMapFilters.State)] = state,
                [nameof(QuestMapFilters.Localizer)] = localizer,
            }));
            return header.ToHtmlString() + filters.ToHtmlString() + details.ToHtmlString();
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("PrimaryProfileWithLongName"));
            Assert.That(html, Does.Contain("ComparisonProfileWithLongName"));
            Assert.That(html, Does.Contain("comparison-filterbar"));
            Assert.That(html, Does.Contain("comparison-summary"));
            Assert.That(html, Does.Contain("matching-comparison-details"));
            Assert.That(html, Does.Contain("comparison-table"));
            Assert.That(html, Does.Contain("objective-comparison"));
        });
    }

    [Test]
    public async Task OrdinaryQuestDetailsRendersCappedObjectiveProgress()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var objective = new ObjectiveDefinitionDto("objective", "Make progress", "CounterCreator", 0, null, 1, null, []);
        var node = Node("quest", "Quest", "trader-a") with { Objectives = [objective] };
        var topology = new QuestTopologyDto("version", [node], [], [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        var progress = new ObjectiveProgressDto(
            objective.Id,
            true,
            CoreProgressRules.CapCurrent(2, objective.RequiredValue),
            objective.RequiredValue,
            true
        );
        var profile = new ProfileStateDto("profile", "Primary", "Usec", 10, 0, false, false,
            [State("quest", "InProgress") with { Objectives = [progress] }], [], ["quest"], ["quest"]);
        var state = new QuestMapPageState();
        state.SetData(topology, profile);
        state.SelectQuest("quest");
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], QuestMapUiCatalog.English));

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var details = await renderer.RenderComponentAsync<QuestDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestDetails.State)] = state,
                [nameof(QuestDetails.Localizer)] = localizer,
            }));
            return details.ToHtmlString();
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("1 / 1"));
            Assert.That(html, Does.Not.Contain("2 / 1"));
        });
    }

    [Test]
    public void LegacyPageSettingsMigrationInvertsHideFinished()
    {
        const string legacy = """{"selectedProfileId":"profile","language":"en","hideFinished":true,"showAllFuture":true}""";

        var actual = QuestMapSettingsStorage.Deserialize(null, legacy);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.ShowFinished, Is.False);
            Assert.That(actual.ShowAllFuture, Is.True);
            Assert.That(actual.ShowRepeatables, Is.True);
            Assert.That(actual.LevelEligibleOnly, Is.True);
        });
    }

    [Test]
    public void HideFinishedStillRevealsSelectedQuestPrerequisites()
    {
        var state = CreateState();
        state.ApplySettings(new QuestMapSettings("profile", "en", false, false, true, false, string.Empty, string.Empty,
            new Dictionary<string, QuestProfileUiSettings> { ["profile"] = new("b", null) }));

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("a"), "Completed recursive prerequisite should remain visible for the selection chain.");
            Assert.That(state.VisibleIds, Does.Contain("b"));
            Assert.That(state.PrerequisiteIds, Is.EquivalentTo(new[] { "a" }));
        });
    }

    [Test]
    public void FocusModeIgnoresFinishedLevelAndTraderFilters()
    {
        var state = CreateState();
        state.ApplySettings(new QuestMapSettings("profile", "en", false, false, true, false, string.Empty, "other-trader",
            new Dictionary<string, QuestProfileUiSettings> { ["profile"] = new("b", null) }));
        state.ActivateFocus();

        Assert.Multiple(() =>
        {
            Assert.That(state.FocusIds, Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(state.VisibleIds, Is.EquivalentTo(new[] { "a", "b", "c" }));
        });
    }

    [Test]
    public void TraderFilterIncludesDirectSuccessorsOnlyFromCurrentBoundary()
    {
        var nodes = new[]
        {
            Node("visible", "Visible", "trader-a"),
            Node("direct", "Direct", "trader-b"),
            Node("deep", "Deep", "trader-c"),
            Node("other-gate", "Other gate", "trader-b"),
            Node("same-trader", "Same trader", "trader-a"),
            Node("level-gated", "Level gated", "trader-b"),
            Node("finished", "Finished", "trader-b"),
            Node("gated-source", "Visible gated source", "trader-a"),
            Node("gated-child", "Gated child", "trader-b"),
            Node("filtered-source", "Filtered source", "trader-a"),
            Node("filtered-child", "Filtered child", "trader-b"),
        };
        var topology = new QuestTopologyDto("version", nodes,
            [
                new QuestEdgeDto("visible", "direct", ["Success"], 0),
                new QuestEdgeDto("direct", "deep", ["Success"], 0),
                new QuestEdgeDto("visible", "other-gate", ["Success"], 0),
                new QuestEdgeDto("visible", "same-trader", ["Success"], 0),
                new QuestEdgeDto("visible", "level-gated", ["Success"], 0),
                new QuestEdgeDto("visible", "finished", ["Success"], 0),
                new QuestEdgeDto("gated-source", "gated-child", ["Success"], 0),
                new QuestEdgeDto("filtered-source", "filtered-child", ["Success"], 0),
            ],
            [
                new QuestTraderDto("trader-a", "Trader A", null),
                new QuestTraderDto("trader-b", "Trader B", null),
                new QuestTraderDto("trader-c", "Trader C", null),
            ], [], []);
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 0, false, false,
            [
                State("visible", "InProgress"),
                State("direct", "PrerequisiteGated"),
                State("deep", "PrerequisiteGated"),
                State("other-gate", "TraderGated"),
                State("same-trader", "Locked"),
                State("level-gated", "LevelGated"),
                State("finished", "Completed"),
                State("gated-source", "PrerequisiteGated"),
                State("gated-child", "Locked"),
                State("filtered-source", "Completed"),
                State("filtered-child", "PrerequisiteGated"),
            ],
            [], ["visible", "direct", "deep", "other-gate", "level-gated", "finished", "gated-source", "gated-child", "filtered-source", "filtered-child"], nodes.Select(node => node.Id).ToArray());
        var state = new QuestMapPageState();
        state.SetData(topology, profile);

        state.ApplySettings(new QuestMapSettings("profile", "en", false, false, true, false, "Visible", "trader-a", new Dictionary<string, QuestProfileUiSettings>()));

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("visible"));
            Assert.That(state.VisibleIds, Does.Contain("direct"), "A direct prerequisite-gated successor should provide cross-trader context.");
            Assert.That(state.VisibleIds, Does.Contain("other-gate"), "A direct successor should be included regardless of its gate state.");
            Assert.That(state.VisibleIds, Does.Contain("same-trader"), "A direct successor outside the normal future scope should still form part of the contextual tier.");
            Assert.That(state.VisibleIds, Does.Not.Contain("deep"), "Cross-trader context must stop after one successor tier.");
            Assert.That(state.VisibleIds, Does.Not.Contain("level-gated"), "Direct successors must still obey the level-eligibility filter.");
            Assert.That(state.VisibleIds, Does.Not.Contain("finished"), "Direct successors must still obey the finished filter.");
            Assert.That(state.VisibleIds, Does.Contain("gated-source"), "The selected trader's prerequisite-gated quest remains normally visible.");
            Assert.That(state.VisibleIds, Does.Not.Contain("gated-child"), "A prerequisite-gated quest must not seed the contextual successor tier.");
            Assert.That(state.VisibleIds, Does.Not.Contain("filtered-source"));
            Assert.That(state.VisibleIds, Does.Not.Contain("filtered-child"), "A successor must derive from a quest that survived the other configured filters.");
        });
    }

    [Test]
    public void TraderFilterSelectionMatchesUnfilteredPrerequisiteVisibilityAcrossTraders()
    {
        var unfiltered = CreateState();
        unfiltered.ApplySettings(new QuestMapSettings("profile", "en", false, false, true, false, string.Empty, string.Empty, new Dictionary<string, QuestProfileUiSettings>()));
        unfiltered.SelectQuest("c");

        var traderFiltered = CreateState();
        traderFiltered.ApplySettings(new QuestMapSettings("profile", "en", false, false, true, false, string.Empty, "trader-b", new Dictionary<string, QuestProfileUiSettings>()));
        traderFiltered.SelectQuest("c");

        Assert.Multiple(() =>
        {
            Assert.That(traderFiltered.PrerequisiteIds, Is.EquivalentTo(unfiltered.PrerequisiteIds));
            Assert.That(traderFiltered.SuccessorIds, Is.EquivalentTo(unfiltered.SuccessorIds));
            Assert.That(traderFiltered.VisibleIds, Is.EquivalentTo(unfiltered.VisibleIds),
                "The trader filter must not truncate the selected quest's recursive prerequisite chain.");
        });
    }

    [Test]
    public void TraderFilterShowsOnlyDirectUnmetPrerequisitesAcrossTraders()
    {
        var nodes = new[]
        {
            Node("target", "Target", "trader-a"),
            Node("satisfied", "Satisfied", "trader-b"),
            Node("blocking", "Blocking", "trader-b"),
            Node("ancestor", "Ancestor", "trader-c"),
        };
        var topology = new QuestTopologyDto("version", nodes,
            [
                new QuestEdgeDto("satisfied", "target", ["Success"], 0),
                new QuestEdgeDto("blocking", "target", ["Success"], 0),
                new QuestEdgeDto("ancestor", "blocking", ["Success"], 0),
            ],
            [
                new QuestTraderDto("trader-a", "Trader A", null),
                new QuestTraderDto("trader-b", "Trader B", null),
                new QuestTraderDto("trader-c", "Trader C", null),
            ], [], []);
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 0, false, false,
            [
                State("target", "PrerequisiteGated") with
                {
                    Blockers = [new QuestBlockerDto("Prerequisite", "blocking", null, null, ["Success"])],
                },
                State("satisfied", "Completed"),
                State("blocking", "PrerequisiteGated") with
                {
                    Blockers = [new QuestBlockerDto("Prerequisite", "ancestor", null, null, ["Success"])],
                },
                State("ancestor", "Available"),
            ],
            [], nodes.Select(node => node.Id).ToArray(), nodes.Select(node => node.Id).ToArray());
        var state = new QuestMapPageState();
        state.SetData(topology, profile);

        state.ApplySettings(new QuestMapSettings("profile", "en", false, false, false, false, string.Empty, "trader-a", new Dictionary<string, QuestProfileUiSettings>()));

        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("target"));
            Assert.That(state.VisibleIds, Does.Contain("blocking"), "The direct prerequisite that still blocks a visible merge quest must cross the trader filter.");
            Assert.That(state.VisibleIds, Does.Not.Contain("satisfied"), "A satisfied incoming prerequisite must not be added as a blocker.");
            Assert.That(state.VisibleIds, Does.Not.Contain("ancestor"), "Unmet-prerequisite context must remain direct rather than recursively expanding.");
        });
    }

    [Test]
    public void RepeatableQuestRulesIncludePmcAndScavGroupsWithNormalizedBands()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Daily"), Is.True);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Weekly"), Is.True);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Daily_Savage"), Is.True);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup(null), Is.False);
            Assert.That(RepeatableQuestRules.IsScavGroup("Daily_Savage"), Is.True);
            Assert.That(RepeatableQuestRules.IsScavGroup("Daily"), Is.False);
            Assert.That(RepeatableQuestRules.DisplayKind("Daily_Savage"), Is.EqualTo("Daily"));
            Assert.That(RepeatableQuestRules.DisplayKind("Weekly"), Is.EqualTo("Weekly"));
        });
    }

    [Test]
    public void DetailMetadataRemainsOutsideCanvasTopologyJson()
    {
        var node = Node("quest", "Quest", "trader") with
        {
            Summary = "Summary",
            WikiUrl = "https://example.test/wiki/quest",
            RelevantItems = [new QuestRelevantItemDto("item", "Relevant item", false)],
        };

        var json = JsonSerializer.Serialize(node);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("Summary"));
            Assert.That(json, Does.Not.Contain("WikiUrl"));
            Assert.That(json, Does.Not.Contain("RelevantItems"));
            Assert.That(QuestMapUiCatalog.English["details.relevantItems"], Is.EqualTo("Relevant items"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".wiki-button"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".flea-ineligible"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".relevant-items-section { border-top-color:"));
            Assert.That(QuestDetails.RelevantItemMarkup("<b><color=#ca741f>Item</color></b>"),
                Does.Contain("<strong>").And.Contain("style=\"color:#ca741f\"").And.Contain("Item"));
        });
    }

    [Test]
    public async Task ScavRepeatableDetailsRendersExplicitBannerDenomination()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var node = Node("scav-daily", "Scav Daily", "trader-a") with { ScavRepeatable = true };
        var topology = new QuestTopologyDto("version", [], [], [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 0, false, false, [], [], [], [])
        {
            RepeatableQuestGroups =
            [
                new RepeatableQuestGroupDto("Daily", 100,
                    [new RepeatableQuestEntryDto(node, State(node.Id, "Available"))]) { Scav = true },
            ],
        };
        var state = new QuestMapPageState();
        state.SetData(topology, profile);
        state.SelectQuest(node.Id);
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], QuestMapUiCatalog.English));

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<QuestDetails>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(QuestDetails.State)] = state,
                [nameof(QuestDetails.Localizer)] = localizer,
            }))).ToHtmlString());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("scav-quest-badge"));
            Assert.That(html, Does.Contain(">Scav</span>"));
        });
    }

    [Test]
    public void RepeatableQuestRulesClassifyUnacceptedAndExpiredQuests()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatableQuestRules.ExactStatus(null), Is.EqualTo(QuestStatusEnum.AvailableForStart));
            Assert.That(RepeatableQuestRules.Classify(QuestStatusEnum.AvailableForStart, false), Is.EqualTo("Available"));
            Assert.That(RepeatableQuestRules.Classify(QuestStatusEnum.Started, false), Is.EqualTo("InProgress"));
            Assert.That(RepeatableQuestRules.Classify(QuestStatusEnum.AvailableForFinish, false), Is.EqualTo("ReadyToFinish"));
            Assert.That(RepeatableQuestRules.Classify(QuestStatusEnum.Success, false), Is.EqualTo("Completed"));
            Assert.That(RepeatableQuestRules.Classify(QuestStatusEnum.Started, true), Is.EqualTo("Expired"),
                "The repeatable group's end time must override its previously active profile status.");
        });
    }

    [Test]
    public void RepeatableQuestsUseNormalSearchTraderAndFinishedFilters()
    {
        var state = CreateStateWithRepeatables();

        Assert.That(state.VisibleIds, Is.SupersetOf(new[] { "daily-available", "daily-completed", "weekly-expired" }));

        state.SetShowFinished(false);
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Contain("daily-available"));
            Assert.That(state.VisibleIds, Does.Not.Contain("daily-completed"));
            Assert.That(state.VisibleIds, Does.Not.Contain("weekly-expired"));
        });

        state.SetShowFinished(true);
        state.SetTraderFilter("trader-b");
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Not.Contain("daily-available"));
            Assert.That(state.VisibleIds, Does.Contain("daily-completed"));
            Assert.That(state.VisibleIds, Does.Contain("weekly-expired"));
        });

        state.SetTraderFilter(string.Empty);
        state.SetSearch("weekly");
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Does.Not.Contain("daily-available"));
            Assert.That(state.VisibleIds, Does.Not.Contain("daily-completed"));
            Assert.That(state.VisibleIds, Does.Contain("weekly-expired"));
        });
    }

    [Test]
    public void RepeatableVisibilityFilterDefaultsOnAndOnlyHidesRepeatables()
    {
        var state = CreateStateWithRepeatables();

        Assert.Multiple(() =>
        {
            Assert.That(state.ShowRepeatables, Is.True);
            Assert.That(state.VisibleIds, Does.Contain("normal"));
            Assert.That(state.VisibleIds, Does.Contain("daily-available"));
        });

        state.SetShowRepeatables(false);
        Assert.Multiple(() =>
        {
            Assert.That(state.VisibleIds, Is.EquivalentTo(new[] { "normal" }));
            Assert.That(state.CaptureSettings("profile", "en").ShowRepeatables, Is.False);
        });

        state.SetShowRepeatables(true);
        Assert.That(state.VisibleIds, Is.SupersetOf(new[] { "normal", "daily-available", "daily-completed", "weekly-expired" }));
    }

    [Test]
    public void RepeatableQuestSelectionOpensDetailsButCannotFocusAChain()
    {
        var state = CreateStateWithRepeatables();

        Assert.That(state.SelectQuest("daily-available"), Is.True);
        state.ActivateFocus();

        Assert.Multiple(() =>
        {
            Assert.That(state.SelectedNode?.Name, Is.EqualTo("Daily Elimination"));
            Assert.That(state.SelectedQuestState?.DisplayState, Is.EqualTo("Available"));
            Assert.That(state.CanFocusSelection, Is.False);
            Assert.That(state.FocusMode, Is.False);
            Assert.That(state.PrerequisiteIds, Is.Empty);
            Assert.That(state.SuccessorIds, Is.Empty);
        });
    }

    [Test]
    public void InProgressDrawerDataIgnoresGraphFilters()
    {
        var state = CreateState();
        state.ApplySettings(new QuestMapSettings("profile", "en", false, true, true, false, "does-not-match", "other-trader", new Dictionary<string, QuestProfileUiSettings>()));

        var groups = state.InProgressGroups();
        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Quests.Select(quest => quest.Node.Id), Is.EquivalentTo(new[] { "b" }));
            Assert.That(state.VisibleIds, Does.Not.Contain("b"));
        });
    }

    [Test]
    public void RichTextSanitizerPreservesParagraphsAndTarkovTags()
    {
        var value = "First line\nsecond line\n\n<color=#ffcc00>Gold</color> <size=200%>large</size> <align=center>middle</align>";
        var result = QuestRichTextSanitizer.Sanitize(value);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("First line<br>second line"));
            Assert.That(result, Does.Contain("style=\"color:#ffcc00\""));
            Assert.That(result, Does.Contain("style=\"font-size:180%\""));
            Assert.That(result, Does.Contain("style=\"text-align:center\""));
            Assert.That(result.Split("description-paragraph").Length - 1, Is.EqualTo(2));
        });
    }

    [Test]
    public void TraderObjectiveTextUsesTheSharedRichTextSanitizer()
    {
        var objective = new ObjectiveDefinitionDto("objective", "<color=#ffcc00>Eliminate <b>Scavs</b></color>", "CounterCreator", 0, null, 1, null, []);

        var result = QuestDetails.ObjectiveMarkup(objective);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("style=\"color:#ffcc00\""));
            Assert.That(result, Does.Contain("<strong>Scavs</strong>"));
            Assert.That(result, Does.Not.Contain("&lt;color"));
        });
    }

    [Test]
    public void RichTextSanitizerDropsExecutableMarkupAndUnsafeLinks()
    {
        var result = QuestRichTextSanitizer.Sanitize("<script>alert(1)</script><img src=x onerror=alert(2)><a href=javascript:alert(3) onclick=alert(4)>bad</a><a href='https://example.test'>safe</a>");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("alert(1)"));
            Assert.That(result, Does.Not.Contain("javascript:"));
            Assert.That(result, Does.Not.Contain("onclick"));
            Assert.That(result, Does.Contain("&lt;img src=x onerror=alert(2)&gt;"));
            Assert.That(result, Does.Contain("href=\"https://example.test\""));
            Assert.That(result, Does.Contain("rel=\"noopener noreferrer\""));
        });
    }

    private static QuestMapPageState CreateState()
    {
        var nodes = new[]
        {
            Node("a", "First", "trader-a"), Node("b", "Second", "trader-a"), Node("c", "Third", "trader-b"),
        };
        var topology = new QuestTopologyDto("version", nodes,
            [new QuestEdgeDto("a", "b", ["Success"], 0), new QuestEdgeDto("b", "c", ["Success"], 0)],
            [new QuestTraderDto("trader-a", "Trader A", null), new QuestTraderDto("trader-b", "Trader B", null)], [], []);
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 0, false, false,
            [State("a", "Completed"), State("b", "InProgress"), State("c", "LevelGated")],
            [], ["a", "b", "c"], ["a", "b", "c"]);
        var state = new QuestMapPageState();
        state.SetData(topology, profile);
        return state;
    }

    private static QuestMapPageState CreateStateWithRepeatables()
    {
        var staticNode = Node("normal", "Normal", "trader-a");
        var topology = new QuestTopologyDto("version", [staticNode], [],
            [new QuestTraderDto("trader-a", "Trader A", null), new QuestTraderDto("trader-b", "Trader B", null)], [], []);
        var dailyAvailable = Node("daily-available", "Daily Elimination", "trader-a");
        var dailyCompleted = Node("daily-completed", "Daily Completion", "trader-b");
        var weeklyExpired = Node("weekly-expired", "Weekly Exploration", "trader-b");
        var profile = new ProfileStateDto("profile", "PMC", "Usec", 10, 100, false, false,
            [State("normal", "Available")], [], ["normal"], ["normal"])
        {
            RepeatableQuestGroups =
            [
                new RepeatableQuestGroupDto("Daily", 200,
                [
                    new RepeatableQuestEntryDto(dailyAvailable, State("daily-available", "Available") with { InProfile = false, AuthoritativelyVisible = true }),
                    new RepeatableQuestEntryDto(dailyCompleted, State("daily-completed", "Completed")),
                ]),
                new RepeatableQuestGroupDto("Weekly", 50,
                [
                    new RepeatableQuestEntryDto(weeklyExpired, State("weekly-expired", "Expired")),
                ]),
            ],
        };
        var state = new QuestMapPageState();
        state.SetData(topology, profile);
        return state;
    }

    private static QuestMapPageState CreateComparisonState()
    {
        var ids = new[]
        {
            "same", "state-difference", "objective-difference", "primary-only", "comparison-only",
            "wait-difference", "exclusion-difference", "combined-difference",
            "finished-active", "both-finished", "level-available", "both-level-gated",
        };
        var nodes = ids.Select(id => id is "objective-difference" or "combined-difference"
            ? Node(id, id, "trader-a") with { Objectives = [new ObjectiveDefinitionDto("objective", "Make progress", "CounterCreator", 0, null, 10, null, [])] }
            : Node(id, id, "trader-a")).ToArray();
        var topology = new QuestTopologyDto("version", nodes, [], [new QuestTraderDto("trader-a", "Trader A", null)], [], []);
        var primary = new ProfileStateDto("profile", "Primary", "Usec", 10, 0, false, false,
            [
                State("same", "Available"),
                State("state-difference", "InProgress"),
                State("objective-difference", "InProgress") with { Objectives = [new ObjectiveProgressDto("objective", false, 0, 10, true)] },
                State("wait-difference", "Pending") with { AvailableAfter = 100 },
                State("exclusion-difference", "Excluded") with { Exclusion = new QuestExclusionDto("branch-a", "Success", true) },
                State("combined-difference", "InProgress") with { Objectives = [new ObjectiveProgressDto("objective", false, 0, 10, true)] },
                State("primary-only", "Available"),
                State("finished-active", "Completed"),
                State("both-finished", "Completed"),
                State("level-available", "LevelGated"),
                State("both-level-gated", "LevelGated"),
            ], [], ids.Except(["comparison-only"]).ToArray(), ids.Except(["comparison-only"]).ToArray());
        var comparison = new ProfileStateDto("comparison", "Comparison", "Bear", 20, 0, false, false,
            [
                State("same", "Available"),
                State("state-difference", "Completed"),
                State("objective-difference", "InProgress") with { Objectives = [new ObjectiveProgressDto("objective", true, 10, 10, true)] },
                State("wait-difference", "Pending") with { AvailableAfter = 200 },
                State("exclusion-difference", "Excluded") with { Exclusion = new QuestExclusionDto("branch-b", "Success", true) },
                State("combined-difference", "Completed") with { Objectives = [new ObjectiveProgressDto("objective", true, 10, 10, true)] },
                State("comparison-only", "Available"),
                State("finished-active", "InProgress"),
                State("both-finished", "Completed"),
                State("level-available", "Available"),
                State("both-level-gated", "LevelGated"),
            ], [], ids.Except(["primary-only"]).ToArray(), ids.Except(["primary-only"]).ToArray());
        var state = new QuestMapPageState();
        state.SetData(topology, primary, comparison);
        return state;
    }

    private static QuestNodeDto Node(string id, string name, string traderId) => new(id, name, string.Empty, traderId, traderId, null, string.Empty, "Any", new QuestLocationDto("any", null, true, null), null, false, [], [], [], [], []);
    private static QuestStateDto State(string id, string display) => new(id, null, display, display != "LevelGated", true, null, [], null, [], display == "InProgress" ? 50 : null);
}
