using System;
using System.Collections.Generic;
using System.Linq;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal static class QuestMapLocationVisuals
{
    public static IReadOnlyList<QuestMapReference> DisplayMaps(
        QuestGraphNode node,
        string fallbackBannerUrl)
    {
        if (QuestObjectiveMapRules.UsesActualMaps(node)) return node.ActualMaps;
        var name = node.Location.Any
            ? ClientLocale.Text("common.any")
            : node.Location.Name ?? node.Location.Id;
        var banner = QuestObjectiveMapRules.IsActualMapPlaceholder(node.Location)
            ? fallbackBannerUrl
            : node.Location.BannerImageUrl ?? fallbackBannerUrl;
        return [new QuestMapReference(node.Location.Id, name, banner)];
    }

    public static TMP_Text AddCompactMapText(
        RectTransform parent,
        IReadOnlyList<QuestMapReference> maps,
        int maximumLines,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        if (maximumLines < 1) throw new ArgumentOutOfRangeException(nameof(maximumLines));
        if (parent.GetComponent<Graphic>() is null)
        {
            var hitTarget = parent.gameObject.AddComponent<Image>();
            hitTarget.color = Color.clear;
            hitTarget.raycastTarget = true;
        }
        var names = maps.Select(map => map.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var truncated = names.Length > maximumLines;
        var visible = truncated
            ? names.Take(maximumLines - 1).Concat(["…"])
            : names.AsEnumerable();
        var text = UnityUiFactory.AddText(parent.gameObject, string.Join("\n", visible), fontSize, alignment, color);
        text.fontStyle = FontStyles.Bold;
        text.enableWordWrapping = false;
        text.lineSpacing = -10;
        text.alpha = 1f;
        text.faceColor = new Color32(255, 255, 255, 255);
        AddTextShadow(text.gameObject);
        if (truncated)
            QuestMapNativeTooltips.Bind(parent.gameObject, () => string.Join("\n", names));
        return text;
    }

    public static IReadOnlyList<string> MapNames(
        QuestGraphNode node,
        IEnumerable<string> mapIds)
    {
        var requested = mapIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0) return [];

        var names = node.ActualMaps
            .Where(map => requested.Remove(map.Id))
            .Select(map => map.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        names.AddRange(requested.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        return names;
    }

    public static void AddArtworkSlices(
        RectTransform parent,
        IReadOnlyList<QuestMapReference> maps,
        string fallbackBannerUrl,
        Color fallback,
        float shadeAlpha,
        QuestAssetSpriteCache assetCache)
    {
        var background = parent.GetComponent<Image>() ?? parent.gameObject.AddComponent<Image>();
        background.color = fallback;
        if (parent.GetComponent<RectMask2D>() is null) parent.gameObject.AddComponent<RectMask2D>();
        var count = Math.Max(1, maps.Count);
        for (var index = 0; index < maps.Count; index++)
        {
            var map = maps[index];
            var slice = UnityUiFactory.CreateRect($"MapArtwork-{index}", parent);
            slice.anchorMin = new Vector2(index / (float)count, 0);
            slice.anchorMax = new Vector2((index + 1) / (float)count, 1);
            slice.offsetMin = Vector2.zero;
            slice.offsetMax = Vector2.zero;
            slice.gameObject.AddComponent<RectMask2D>();

            var imageRect = UnityUiFactory.CreateRect("Artwork", slice);
            UnityUiFactory.Stretch(imageRect);
            var image = imageRect.gameObject.AddComponent<Image>();
            image.preserveAspect = false;
            image.raycastTarget = false;
            var fitter = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            imageRect.gameObject.SetActive(false);
            var url = map.BannerImageUrl ?? fallbackBannerUrl;
            assetCache.Request(url, sprite =>
            {
                if (image == null || sprite is null) return;
                image.sprite = sprite;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                imageRect.gameObject.SetActive(true);
            });
        }

        var shade = UnityUiFactory.CreateRect("Shade", parent);
        UnityUiFactory.Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, shadeAlpha);
    }

    private static void AddTextShadow(GameObject target)
    {
        var outline = target.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.98f);
        outline.effectDistance = new Vector2(1.25f, -1.25f);
        outline.useGraphicAlpha = true;
        var shadow = target.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.92f);
        shadow.effectDistance = new Vector2(2, -2);
    }
}
