using System;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal static class QuestGraphPalette
{
    public static readonly Color Panel = Hex(0x111512, 0.94f);
    public static readonly Color Control = Hex(0x2E3433);
    public static readonly Color Border = Hex(0x3A3F38);
    public static readonly Color Text = Hex(0xE7E2D6);
    public static readonly Color MutedText = Hex(0xB7BAB0);

    private static QuestMapClientConfiguration? _configuration;

    public static Color Selected => Configured(_configuration?.SelectedHighlightColor.Value, 0xEFD470);
    public static Color Prerequisite => Configured(_configuration?.PrerequisiteHighlightColor.Value, 0x7AB5D9);
    public static Color Successor => Configured(_configuration?.SuccessorHighlightColor.Value, 0xD6A35C);
    public static Color Available => Configured(_configuration?.AvailableColor.Value, 0x4D9E72);
    public static Color InProgress => Configured(_configuration?.InProgressColor.Value, 0x3F89B8);
    public static Color ReadyToTurnIn => Configured(_configuration?.ReadyToTurnInColor.Value, 0xD4A83F);
    public static Color Completed => Configured(_configuration?.CompletedColor.Value, 0x67A77A);
    public static Color Failed => Configured(_configuration?.FailedColor.Value, 0xC55C62);
    public static Color LevelGate => Configured(_configuration?.LevelGateColor.Value, 0x9D7A45);
    public static Color TraderGate => Configured(_configuration?.TraderGateColor.Value, 0x8B659E);
    public static Color Locked => Configured(_configuration?.LockedColor.Value, 0x78808B);
    public static Color Collector => Configured(_configuration?.CollectorColor.Value, 0x945BC4);
    public static Color Lightkeeper => Configured(_configuration?.LightkeeperColor.Value, 0x234F7F);
    public static Color Terminal => Configured(_configuration?.EndOfLineColor.Value, 0xD1B35F);
    public static Color CompletedMarker => Completed;
    public static Color ControlActive => DarkSurface(Selected);
    public static Color WarningControl => DarkSurface(Failed);
    public static Color WarningControlActive => new(
        Failed.r * 0.62f,
        Failed.g * 0.48f,
        Failed.b * 0.42f,
        1f);

    public static void Configure(QuestMapClientConfiguration configuration) => _configuration = configuration;

    public static Color Status(QuestMapDisplayStateKind kind) => kind switch
    {
        QuestMapDisplayStateKind.Locked => Locked,
        QuestMapDisplayStateKind.PrerequisiteGated => Locked,
        QuestMapDisplayStateKind.PrestigeGated => LevelGate,
        QuestMapDisplayStateKind.LevelGated => LevelGate,
        QuestMapDisplayStateKind.TraderGated => TraderGate,
        QuestMapDisplayStateKind.TraderUnavailable => TraderGate,
        QuestMapDisplayStateKind.Available => Available,
        QuestMapDisplayStateKind.InProgress => InProgress,
        QuestMapDisplayStateKind.ReadyToFinish => ReadyToTurnIn,
        QuestMapDisplayStateKind.Completed => Completed,
        QuestMapDisplayStateKind.Failed => Failed,
        QuestMapDisplayStateKind.Excluded => Failed,
        QuestMapDisplayStateKind.RestartableFailure => ReadyToTurnIn,
        QuestMapDisplayStateKind.Expired => Failed,
        QuestMapDisplayStateKind.Pending => Locked,
        _ => Locked,
    };

    public static Color Edge(QuestEdgeRequirementKind kind, float alpha = 0.9f)
    {
        var color = kind switch
        {
            QuestEdgeRequirementKind.Success => Available,
            QuestEdgeRequirementKind.Failure => Failed,
            QuestEdgeRequirementKind.Started => InProgress,
            QuestEdgeRequirementKind.AnyOutcome => ReadyToTurnIn,
            _ => Locked,
        };
        color.a = alpha;
        return color;
    }

    private static Color Hex(int rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xff) / 255f,
        ((rgb >> 8) & 0xff) / 255f,
        (rgb & 0xff) / 255f,
        alpha);

    private static Color Configured(Color? configured, int fallback) => configured ?? Hex(fallback);

    private static Color DarkSurface(Color highlight) => new(
        highlight.r * 0.43f,
        highlight.g * 0.38f,
        highlight.b * 0.24f,
        1f);
}
