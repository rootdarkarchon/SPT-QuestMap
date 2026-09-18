using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Web;
using SPTQuestMap.Components.Pages;
using SPTQuestMap.Configuration;
using SPTQuestMap.Services;

namespace SPTQuestMap.Tests;

public sealed class Spt41MigrationTests
{
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    public async Task BrowserProfileAccessUsesConfiguredUserPolicy(bool requireAuthentication, bool authenticated, bool administrator)
    {
        var services = new ServiceCollection().AddLogging();
        await QuestMapBrowserAuthorization.OnDIConstructAsync(services, CancellationToken.None);
        services.AddSingleton(new QuestMapServerConfiguration { RequireBrowserAuthentication = requireAuthentication });
        // Post-configuration must use the final host policies regardless of registration order.
        services.AddAuthorizationCore(options =>
            options.AddPolicy("Administrator", p => p.RequireClaim("isAdministrator", "true")));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            administrator ? [new Claim("isAdministrator", "true")] : [], authenticated ? "SptWebCookie" : null));
        services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationState(principal));
        await using var provider = services.BuildServiceProvider();
        var pagePolicy = typeof(QuestMap).GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        var authorization = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, pagePolicy);
        var allowed = !requireAuthentication || authenticated;
        Assert.That(authorization.Succeeded, Is.EqualTo(allowed));
        var administratorAccess = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, null, "Administrator");
        Assert.That(administratorAccess.Succeeded, Is.EqualTo(administrator), "Other SPT policies must remain unchanged.");
        if (allowed) return;

        // Deliberately omit QuestMapDataService. A denied route must never instantiate the page or read profiles.
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<DeniedRouteHost>()).ToHtmlString());
        Assert.That(html, Does.Contain("Access denied"));
    }

    [Test]
    public void MetadataRegistersBrowserAndLimitsSptRange()
    {
        var metadata = new QuestMapModMetadata();
        Assert.That(metadata, Is.InstanceOf<IModBlazorMetadata>());
        Assert.That(metadata.HomePage, Is.EqualTo("/questmap"));
        Assert.That(metadata.HasPrepatcher, Is.False);
        foreach (var version in new[] { "4.1.6", "4.1.7", "4.1.99" })
            Assert.That(metadata.SptVersion.IsSatisfied(new SemanticVersioning.Version(version)), Is.True);
        foreach (var version in new[] { "4.0.13", "4.1.5", "4.2.0" })
            Assert.That(metadata.SptVersion.IsSatisfied(new SemanticVersioning.Version(version)), Is.False);
    }

    [Test]
    public void PreloadUsesInjectedTablesOnceAndCancellationDoesNotPublishPartialState()
    {
        // Only the table properties consumed by the preload are needed; no real profile or database is read.
        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        var locations = (LocationTable)RuntimeHelpers.GetUninitializedObject(typeof(LocationTable));
        var itemId = new MongoId("000000000000000000000001");
        var items = new Dictionary<MongoId, TemplateItem>
        {
            [itemId] = new() { Id = itemId, Properties = new() { QuestItem = true } }
        };
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates, items);
        var reads = 0;
        var location = new Location
        {
            Base = new() { Id = "bigmap" },
            LooseLoot = new LazyLoad<LooseLoot>(() =>
            {
                Interlocked.Increment(ref reads);
                return new LooseLoot
                {
                    SpawnpointsForced = [new() { Template = new() { Items = [new() { Id = itemId, Template = itemId }] } }]
                };
            }, cacheValue: false)
        };
        typeof(LocationTable).GetProperty(nameof(LocationTable.Bigmap))!.SetValue(locations, location);
        var preload = new QuestMapTopologyPreload(templates, locations, DispatchProxy.Create<ISptLogger<QuestMapDataService>, NullLogger>());
        Assert.ThrowsAsync<OperationCanceledException>(() => preload.OnLoadAsync(new CancellationToken(true)));
        Assert.Throws<InvalidOperationException>(() => _ = preload.Items);
        Assert.That(reads, Is.Zero);
        preload.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        for (var i = 0; i < 3; i++)
        {
            Assert.That(preload.Items, Is.SameAs(items));
            Assert.That(preload.Locations, Has.Count.EqualTo(1));
            Assert.That(preload.QuestItemSpawnMapIds[itemId], Is.EqualTo(new[] { "bigmap" }));
        }
        Assert.That(reads, Is.EqualTo(1), "Uncached 4.1 loose loot must not be deserialized on requests.");
    }

    [Test]
    public void StartupOrderingPlacesPreloadBeforeCatalogInitializationAfterMods()
    {
        var preload = typeof(QuestMapTopologyPreload).GetCustomAttribute<Injectable>()!.TypePriority;
        var catalogs = typeof(QuestMapDataService).GetCustomAttribute<Injectable>()!.TypePriority;
        Assert.That(preload, Is.GreaterThan(OnLoadOrder.PostLoad));
        Assert.That(catalogs, Is.GreaterThan(preload));
    }

    [Test]
    public void SptSerializerPreservesNativeFeedProgressAndRepeatableDeadline()
    {
        var serializer = new JsonUtil([]);
        var objective = new ObjectiveDefinitionDto("objective", "Eliminate targets", "CounterCreator", 0, null, 3, ">=", [])
        {
            IsKillObjective = true, MapIds = ["bigmap"],
            TaskLocations = [new("bigmap", "Customs", null)]
        };
        var quest = new QuestNodeDto("quest", "Fixture", "Description", "trader", "Prapor", null, "", "Any",
            new("any", "Any", true, null), null, false, [], [], [objective], [], []);
        var feed = new QuestMapClientRepeatableFeedDto("topology-version", [quest],
            new Dictionary<string, string> { ["quest"] = "Daily" }, ["quest"], ["quest"],
            new Dictionary<string, string> { ["quest"] = "InProgress" },
            new Dictionary<string, double?> { ["quest"] = 2d / 3d * 100 },
            new Dictionary<string, long> { ["quest"] = 2000000000 },
            new Dictionary<string, string[]>(), new Dictionary<string, string>(), new Dictionary<string, QuestMetaInfoDto>());
        var json = serializer.Serialize(feed)!;
        var transported = System.Text.Json.JsonSerializer.Deserialize<SPTQuestMap.Core.Transport.QuestRepeatableFeed>(json)!;
        Assert.Multiple(() =>
        {
            Assert.That(transported.StaticTopologyVersion, Is.EqualTo("topology-version"));
            Assert.That(transported.RepeatableEndTimes["quest"], Is.EqualTo(2000000000));
            Assert.That(transported.ProgressPercentages["quest"], Is.EqualTo(2d / 3d * 100));
            Assert.That(transported.ProfileGeneratedQuests.Single().Objectives.Single().IsKillObjective, Is.True);
            Assert.That(json, Does.Not.Contain("CharacterData"));
        });
    }

    [Test]
    public void EditionRestrictionsExcludeFutureQuestsButKeepEveryProfileKnownStatusWithoutMutation()
    {
        var trader = new MongoId("000000000000000000000001");
        var statuses = Enum.GetValues<QuestStatusEnum>();
        var known = statuses.Select((status, i) => new QuestStatus
        {
            QId = new MongoId((i + 2).ToString("x24")), Status = status, StatusTimers = [], StartTime = 0
        }).ToArray();
        var templates = known.Select(q => QuestTemplate(q.QId, trader)).ToList();
        var excludedId = new MongoId("0000000000000000000000ff");
        templates.Add(QuestTemplate(excludedId, trader));
        var editionChecks = new List<MongoId>();
        var projected = QuestAvailabilityProjection.Build(templates, known,
            QuestProfileStateBuilder.BuildProfileQuestLookup(known), "Usec", 50, [trader],
            (_, _) => false, _ => true, (_, _) => true, _ => true, _ => true,
            id => { editionChecks.Add(id); return false; });
        Assert.That(projected.Count, Is.EqualTo(known.Length));
        Assert.That(projected, Does.Not.ContainKey(excludedId.ToString()));
        Assert.That(editionChecks, Is.EqualTo(new[] { excludedId }));
        foreach (var q in known) Assert.That(projected[q.QId.ToString()], Is.EqualTo(q.Status));
        Assert.That(templates.Select(t => t.SptStatus), Has.All.Null);
        Assert.That(statuses.Select(s => (int)s), Is.EqualTo(Enumerable.Range(0, 10)));
    }

    public class NullLogger : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.ReturnType == typeof(bool) ? false : null;
    }

    private static Quest QuestTemplate(MongoId id, MongoId trader) => new()
    {
        Id = id, TraderId = trader, CanShowNotificationsInGame = true,
        Conditions = new() { AvailableForStart = [] }, Description = "Fixture", Name = "Fixture",
        Location = "any", Image = "", Type = QuestTypeEnum.Completion, Restartable = false, Side = "Pmc"
    };

    private sealed class TestAuthenticationState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    public sealed class DeniedRouteHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(content =>
            {
                content.OpenComponent<AuthorizeRouteView>(0);
                content.AddAttribute(1, "RouteData", new RouteData(typeof(QuestMap), new Dictionary<string, object?>()));
                content.AddAttribute(2, "NotAuthorized", (RenderFragment<AuthenticationState>)(_ => text => text.AddContent(0, "Access denied")));
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
