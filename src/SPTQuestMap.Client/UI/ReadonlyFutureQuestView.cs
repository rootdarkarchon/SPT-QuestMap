using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class ReadonlyFutureQuestView
{
    private readonly TextMeshProUGUI _text;

    private ReadonlyFutureQuestView(RectTransform root, TextMeshProUGUI text)
    {
        Root = root;
        _text = text;
    }

    public RectTransform Root { get; }

    public static ReadonlyFutureQuestView Create(RectTransform nativeDetailRect)
    {
        var root = UnityUiFactory.CreateRect("QuestMapReadonlyFutureDetail", nativeDetailRect.parent);
        UnityUiFactory.CopyRect(nativeDetailRect, root);
        root.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.062f, 0.066f, 0.99f);

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 28, 28, 26, 26);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var text = UnityUiFactory.AddText(
            content.gameObject,
            string.Empty,
            17,
            TextAlignmentOptions.TopLeft,
            new Color(0.86f, 0.87f, 0.86f, 1));
        text.margin = new Vector4(8, 8, 8, 8);
        text.enableAutoSizing = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34;
        root.gameObject.SetActive(false);
        return new ReadonlyFutureQuestView(root, text);
    }

    public void Show(string content)
    {
        _text.text = content;
        Root.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_text.transform);
    }

    public void Hide()
    {
        if (Root != null) Root.gameObject.SetActive(false);
    }

    public void Destroy()
    {
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
    }
}
