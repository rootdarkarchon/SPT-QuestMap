using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using SPTQuestMap.Core.Transport;

namespace SPTQuestMap.Core.Adapters;

public static class QuestTopologyNormalizer
{
    public static string GetStaticTopologyVersion(QuestGraphTopology topology)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        return topology.Version.Split(new[] { ":generated:" }, StringSplitOptions.None)[0];
    }

    public static bool MatchesStaticTopologyVersion(QuestGraphTopology topology, string? staticTopologyVersion) =>
        !string.IsNullOrWhiteSpace(staticTopologyVersion)
        && string.Equals(GetStaticTopologyVersion(topology), staticTopologyVersion, StringComparison.Ordinal);

    public static QuestGraphTopology ApplyProfileGeneratedDelta(
        QuestGraphTopology current,
        QuestRepeatableFeed feed)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (feed is null) throw new ArgumentNullException(nameof(feed));
        var staticNodes = current.Nodes.Where(node => !node.ProfileGenerated).ToArray();
        var generatedNodes = (feed.ProfileGeneratedQuests ?? [])
            .Select((node, index) => MapNode(
                node,
                true,
                feed.RepeatableKinds?.GetValueOrDefault(node.Id),
                feed.QuestSummaries?.GetValueOrDefault(node.Id),
                feed.QuestMetaInfo?.GetValueOrDefault(node.Id),
                staticNodes.Length + index))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var nodes = staticNodes.Concat(generatedNodes).OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var staticVersion = GetStaticTopologyVersion(current);
        var diagnostics = current.Diagnostics with
        {
            UnknownConditionCount = staticNodes.Sum(node => node.UnknownConditions.Count)
                + generatedNodes.Sum(node => node.UnknownConditions.Count),
        };
        return new QuestGraphTopology(
            BuildTopologyVersion(staticVersion, feed.ProfileGeneratedQuests ?? []),
            nodes,
            current.Edges,
            current.Traders,
            KnownIds(feed.DefaultVisibleQuestIds, nodeIds),
            KnownIds(feed.AllApplicableQuestIds, nodeIds),
            current.CollectorPathQuestIds,
            current.LightkeeperPathQuestIds,
            diagnostics);
    }

    public static QuestGraphTopology Normalize(QuestTopologyFeed feed)
    {
        if (feed is null) throw new ArgumentNullException(nameof(feed));
        if (feed.Topology is null) throw new ArgumentException("Topology feed must contain a topology payload.", nameof(feed));

        var staticNodes = feed.Topology.Quests ?? [];
        var generatedNodes = feed.ProfileGeneratedQuests ?? [];
        var nodes = staticNodes
            .Select((node, index) => MapNode(
                node,
                false,
                null,
                feed.QuestSummaries?.GetValueOrDefault(node.Id),
                feed.QuestMetaInfo?.GetValueOrDefault(node.Id),
                index))
            .Concat(generatedNodes.Select((node, index) => MapNode(
                node,
                true,
                feed.RepeatableKinds?.GetValueOrDefault(node.Id),
                feed.QuestSummaries?.GetValueOrDefault(node.Id),
                feed.QuestMetaInfo?.GetValueOrDefault(node.Id),
                staticNodes.Length + index)))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(node => node.ProfileGenerated).First())
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        var edges = (feed.Topology.Edges ?? [])
            .Select(edge => new QuestGraphEdge(
                edge.SourceId,
                edge.TargetId,
                (edge.RequiredStatuses ?? []).OrderBy(status => status, StringComparer.Ordinal).ToArray(),
                edge.AvailableAfterSeconds,
                QuestGraphRules.ClassifyEdgeRequirement(edge.RequiredStatuses ?? [])))
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToArray();
        var traders = (feed.Topology.Traders ?? [])
            .Select(trader => new QuestTrader(trader.Id, trader.Name, trader.ImageUrl))
            .OrderBy(trader => trader.Id, StringComparer.Ordinal)
            .ToArray();

        var missingPredecessors = edges
            .Where(edge => !nodeIds.Contains(edge.SourceId))
            .Select(edge => edge.SourceId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var missingTargets = edges
            .Where(edge => !nodeIds.Contains(edge.TargetId))
            .Select(edge => edge.TargetId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = new QuestTopologyDiagnostics(
            missingPredecessors,
            missingTargets,
            nodes.Sum(node => node.UnknownConditions.Count));

        var defaultVisible = KnownIds(feed.DefaultVisibleQuestIds, nodeIds);
        var applicable = KnownIds(feed.AllApplicableQuestIds, nodeIds);
        if (applicable.Count == 0) applicable = nodes.Select(node => node.Id).ToArray();
        if (defaultVisible.Count == 0)
        {
            defaultVisible = nodes.Where(node => !node.ProfileGenerated).Select(node => node.Id).ToArray();
        }

        return new QuestGraphTopology(
            BuildTopologyVersion(feed.Topology.Version, generatedNodes),
            nodes,
            edges,
            traders,
            defaultVisible,
            applicable,
            KnownIds(feed.Topology.CollectorPathQuestIds, nodeIds),
            KnownIds(feed.Topology.LightkeeperPathQuestIds, nodeIds),
            diagnostics);
    }

    private static IReadOnlyCollection<string> KnownIds(IEnumerable<string>? values, HashSet<string> nodeIds) =>
        (values ?? [])
            .Where(nodeIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private static string BuildTopologyVersion(string staticVersion, IEnumerable<QuestNodePayload> generatedNodes)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        var count = 0;
        foreach (var questId in generatedNodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal))
        {
            count++;
            foreach (var character in questId)
            {
                hash ^= character;
                hash *= prime;
            }
            hash ^= 0xff;
            hash *= prime;
        }

        return count == 0 ? staticVersion : $"{staticVersion}:generated:{hash:X16}";
    }

    private static QuestGraphNode MapNode(
        QuestNodePayload node,
        bool profileGenerated,
        string? repeatableKind,
        string? summary,
        QuestMetaInfoPayload? metaInfo,
        int naturalOrder)
    {
        var location = node.Location ?? new QuestLocationPayload();
        return new QuestGraphNode(
            node.Id,
            node.Name,
            node.Description,
            node.TraderId,
            node.TraderName,
            node.TraderImageUrl,
            node.ImageUrl,
            node.Faction,
            new QuestLocation(location.Id, location.Name, location.Any, location.BannerImageUrl),
            node.EventSeason,
            node.Restartable,
            (node.DirectRequirements ?? []).Select(MapRequirement).ToArray(),
            (node.EffectiveRequirements ?? []).Select(MapRequirement).ToArray(),
            (node.Objectives ?? []).Select(objective => new QuestObjectiveDefinition(
                objective.Id,
                objective.Text,
                objective.ConditionType,
                objective.Index,
                objective.ParentId,
                objective.RequiredValue,
                objective.Compare,
                objective.DependsOn ?? [],
                objective.ZoneIds ?? [],
                objective.OneSessionOnly,
                objective.DoNotResetIfCounterCompleted)).ToArray(),
            (node.ExclusionRules ?? []).Select(rule => new QuestExclusionRule(
                rule.CausedByQuestId,
                rule.RequiredStatuses ?? [])).ToArray(),
            (node.Rewards ?? []).Select(reward => new QuestReward(
                reward.Id,
                reward.Type,
                reward.TargetId,
                reward.TargetName,
                reward.Value,
                reward.LoyaltyLevel,
                reward.TraderName,
                reward.Unknown,
                reward.Hidden,
                (reward.Items ?? []).Select(item => new QuestRewardItem(item.TemplateId, item.Name, item.Count)).ToArray())).ToArray(),
            (node.UnknownConditions ?? []).Select(condition => new QuestUnknownCondition(
                condition.Stage,
                condition.ConditionType,
                condition.ConditionId)).ToArray(),
            profileGenerated,
            repeatableKind)
        {
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            WikiUrl = string.IsNullOrWhiteSpace(metaInfo?.WikiUrl) ? null : metaInfo.WikiUrl.Trim(),
            RelevantItems = (metaInfo?.RelevantItems ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.TemplateId) && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => new QuestRelevantItem(item.TemplateId, item.Name, item.FleaEligible))
                .ToArray(),
            ScavRepeatable = node.ScavRepeatable,
            NaturalOrder = naturalOrder,
        };
    }

    private static QuestRequirement MapRequirement(QuestRequirementPayload requirement) =>
        new(requirement.Kind, requirement.TraderId, requirement.Compare, requirement.Value);
}
