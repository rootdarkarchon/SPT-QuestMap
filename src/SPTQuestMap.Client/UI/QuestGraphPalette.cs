using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal static class QuestGraphPalette
{
    public static readonly Color Panel = Hex(0x111512, 0.94f);
    public static readonly Color Control = Hex(0x2E3433);
    public static readonly Color ControlActive = Hex(0x675622);
    public static readonly Color Border = Hex(0x3A3F38);
    public static readonly Color Text = Hex(0xE7E2D6);
    public static readonly Color MutedText = Hex(0xB7BAB0);
    public static readonly Color Terminal = Hex(0xD1B35F);
    public static readonly Color CompletedMarker = Hex(0x3F8F55);
    public static readonly Color Collector = Hex(0x945BC4);
    public static readonly Color Lightkeeper = Hex(0x234F7F);

    public static Color Status(QuestMapDisplayStateKind kind) => kind switch
    {
        QuestMapDisplayStateKind.Locked => Hex(0x78808B),
        QuestMapDisplayStateKind.PrerequisiteGated => Hex(0x827465),
        QuestMapDisplayStateKind.LevelGated => Hex(0x9D7A45),
        QuestMapDisplayStateKind.TraderGated => Hex(0x8B659E),
        QuestMapDisplayStateKind.TraderUnavailable => Hex(0xB06453),
        QuestMapDisplayStateKind.Available => Hex(0x4D9E72),
        QuestMapDisplayStateKind.InProgress => Hex(0x3F89B8),
        QuestMapDisplayStateKind.ReadyToFinish => Hex(0xD4A83F),
        QuestMapDisplayStateKind.Completed => Hex(0x557E5B),
        QuestMapDisplayStateKind.Failed => Hex(0x9C4F54),
        QuestMapDisplayStateKind.Excluded => Hex(0xB33F4A),
        QuestMapDisplayStateKind.RestartableFailure => Hex(0xB76C49),
        QuestMapDisplayStateKind.Expired => Hex(0x74535D),
        QuestMapDisplayStateKind.Pending => Hex(0xBA8752),
        _ => Hex(0x4F5552),
    };

    public static Color Edge(QuestEdgeRequirementKind kind, float alpha = 0.9f)
    {
        var color = kind switch
        {
            QuestEdgeRequirementKind.Success => Hex(0x67A77A),
            QuestEdgeRequirementKind.Failure => Hex(0xC55C62),
            QuestEdgeRequirementKind.Started => Hex(0x5798C3),
            QuestEdgeRequirementKind.AnyOutcome => Hex(0xB89A55),
            _ => Hex(0x8D8F88),
        };
        color.a = alpha;
        return color;
    }

    private static Color Hex(int rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xff) / 255f,
        ((rgb >> 8) & 0xff) / 255f,
        (rgb & 0xff) / 255f,
        alpha);
}
