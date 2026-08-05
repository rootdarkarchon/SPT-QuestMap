using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using SPTQuestMap.Components;
using SPTQuestMap.Components.Pages;
using SPTQuestMap.Presentation;
using SPTQuestMap.Services;
using SPTarkov.Server.Core.Models.Enums;
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
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain("--qm-renderer-repeatable-divider"));
            Assert.That(QuestMapEmbeddedAssets.Css, Does.Contain(".profile-dismiss-layer:hover:not(:disabled)"),
                "The full-screen profile-menu dismiss button must override the global button hover background.");
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("getComputedStyle"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("composeRepeatableLayout"));
            Assert.That(QuestMapEmbeddedAssets.RendererSource, Does.Contain("effectiveQuestState"));
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
        var expected = new QuestMapSettings("profile", "en", true, false, false, true, "search", "trader", new Dictionary<string, QuestProfileUiSettings> { ["profile"] = new("quest", "focus") })
        {
            ShowRepeatables = false,
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
    public void RepeatableQuestRulesIncludePmcDailyAndWeeklyOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Daily"), Is.True);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Weekly"), Is.True);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup("Daily_Savage"), Is.False);
            Assert.That(RepeatableQuestRules.ShouldIncludeGroup(null), Is.False);
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

    private static QuestNodeDto Node(string id, string name, string traderId) => new(id, name, string.Empty, traderId, traderId, null, string.Empty, "Any", new QuestLocationDto("any", null, true, null), null, false, [], [], [], [], []);
    private static QuestStateDto State(string id, string display) => new(id, null, display, display != "LevelGated", true, null, [], null, [], display == "InProgress" ? 50 : null);
}
