using System.Reflection;
using System.Text;

namespace SPTQuestMap.Presentation;

internal static class QuestMapEmbeddedAssets
{
    internal static string Css { get; } = LoadText("SPTQuestMap.Presentation.Assets.QuestMap.css");
    internal static string RendererSource { get; } = LoadText("SPTQuestMap.Presentation.Assets.QuestMapRenderer.mjs");
    internal static string RendererModuleDataUrl { get; } =
        $"data:text/javascript;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(RendererSource))}";

    private static string LoadText(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded QuestMap asset '{resourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
