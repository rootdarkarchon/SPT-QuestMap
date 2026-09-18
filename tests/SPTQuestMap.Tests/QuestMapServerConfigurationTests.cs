using NUnit.Framework;
using SPTQuestMap.Configuration;

namespace SPTQuestMap.Tests;

public sealed class QuestMapServerConfigurationTests
{
    [Test]
    public void MissingConfigurationCreatesAuthenticatedDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"questmap-config-{Guid.NewGuid():N}.json");
        try
        {
            var warnings = new List<string>();
            var configuration = QuestMapServerConfiguration.Load(path, warnings.Add);
            Assert.That(configuration.RequireBrowserAuthentication, Is.True);
            Assert.That(File.ReadAllText(path), Does.Contain("\"requireBrowserAuthentication\": true"));
            Assert.That(warnings, Is.Empty);
        }
        finally { File.Delete(path); }
    }

    [TestCase("{}", true, false)]
    [TestCase("{\"requireBrowserAuthentication\":true}", true, false)]
    [TestCase("{\"requireBrowserAuthentication\":false}", false, false)]
    [TestCase("null", true, true)]
    [TestCase("{", true, true)]
    [TestCase("{\"requireBrowserAuthentication\":\"false\"}", true, true)]
    [TestCase("{\"requireBrowserAuthentication\":null}", true, true)]
    public void ConfigurationRequiresExplicitBooleanOptOut(string json, bool requireAuthentication, bool shouldWarn)
    {
        var path = Path.Combine(Path.GetTempPath(), $"questmap-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var warnings = new List<string>();
            Assert.That(QuestMapServerConfiguration.Load(path, warnings.Add).RequireBrowserAuthentication,
                Is.EqualTo(requireAuthentication));
            Assert.That(warnings.Count, Is.EqualTo(shouldWarn ? 1 : 0));
            Assert.That(File.ReadAllText(path), Is.EqualTo(json), "Existing user settings must not be overwritten.");
        }
        finally { File.Delete(path); }
    }
}
