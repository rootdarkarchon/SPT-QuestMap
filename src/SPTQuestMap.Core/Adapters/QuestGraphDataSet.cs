using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Adapters;

public sealed class QuestGraphDataSet
{
    public QuestGraphDataSet(QuestGraphTopology topology)
    {
        Topology = topology ?? throw new ArgumentNullException(nameof(topology));
        Layout = DeterministicGraphLayout.Build(topology);
    }

    public QuestGraphTopology Topology { get; }

    public QuestGraphLayout Layout { get; }

    public QuestProfileOverlay? Overlay { get; private set; }

    public QuestProfileOverlay RefreshOverlay(LiveProfileSnapshot snapshot)
    {
        Overlay = QuestOverlayBuilder.Build(Topology, snapshot);
        return Overlay;
    }
}
