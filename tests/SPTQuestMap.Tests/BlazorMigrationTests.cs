using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using SPTQuestMap.Components.Pages;
using SPTQuestMap.Presentation;
using SPTQuestMap.Services;
using System.Reflection;
using System.Text.RegularExpressions;

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
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("getComputedStyle"));
            Assert.That(QuestMapEmbeddedAssets.RendererModuleDataUrl, Does.StartWith("data:text/javascript;base64,"));
            Assert.That(Regex.IsMatch(QuestMapEmbeddedAssets.RendererSource, colorLiteralPattern), Is.False,
                "Renderer colors must be supplied by CSS custom properties, not JavaScript literals.");
        });
    }

    [Test]
    public void GraphSnapshotCarriesTheAlreadyLoadedTopologyAndProfile()
    {
        var state = CreateState();
        var localizer = new QuestMapLocalizer(new QuestMapBootstrapDto("en", "en", [], new Dictionary<string, string>()));

        var snapshot = state.BuildGraphSnapshot(localizer);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Topology, Is.SameAs(state.Topology));
            Assert.That(snapshot.Profile, Is.SameAs(state.Profile));
            Assert.That(snapshot.View.ProfileId, Is.EqualTo("profile"));
        });
    }

    [Test]
    public void PageSettingsRoundTripInCSharp()
    {
        var expected = new QuestMapSettings("profile", "en", true, false, false, true, "search", "trader", new Dictionary<string, QuestProfileUiSettings> { ["profile"] = new("quest", "focus") });

        var actual = QuestMapSettingsStorage.Deserialize(QuestMapSettingsStorage.Serialize(expected), null);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.SelectedProfileId, Is.EqualTo(expected.SelectedProfileId));
            Assert.That(actual.Language, Is.EqualTo(expected.Language));
            Assert.That(actual.ShowAllFuture, Is.EqualTo(expected.ShowAllFuture));
            Assert.That(actual.ShowFinished, Is.EqualTo(expected.ShowFinished));
            Assert.That(actual.LevelEligibleOnly, Is.EqualTo(expected.LevelEligibleOnly));
            Assert.That(actual.InProgressExpanded, Is.EqualTo(expected.InProgressExpanded));
            Assert.That(actual.Search, Is.EqualTo(expected.Search));
            Assert.That(actual.TraderFilter, Is.EqualTo(expected.TraderFilter));
            Assert.That(actual.Profiles, Is.EquivalentTo(expected.Profiles));
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

    private static QuestNodeDto Node(string id, string name, string traderId) => new(id, name, string.Empty, traderId, traderId, null, string.Empty, "Any", new QuestLocationDto("any", null, true, null), null, false, [], [], [], [], []);
    private static QuestStateDto State(string id, string display) => new(id, null, display, display != "LevelGated", true, null, [], null, [], display == "InProgress" ? 50 : null);
}
