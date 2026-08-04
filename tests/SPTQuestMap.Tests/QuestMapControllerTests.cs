using NUnit.Framework;
using SPTQuestMap.Controllers;

namespace SPTQuestMap.Tests;

public sealed class QuestMapControllerTests
{
    [TestCase("PMC", 1, true)]
    [TestCase("headless_6937e7e248ea76002adbb379", 1, false)]
    [TestCase("HEADLESS_fixture", 20, false)]
    [TestCase("Uninitialized", 0, false)]
    public void ProfileDropdownEligibilityFiltersHeadlessAndLevelZeroProfiles(string nickname, int level, bool expected)
    {
        Assert.That(QuestMapController.ShouldIncludeProfile(nickname, level), Is.EqualTo(expected));
    }
}
