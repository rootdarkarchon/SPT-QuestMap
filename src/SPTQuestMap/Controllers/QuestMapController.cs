using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SPTarkov.Server.Core.Servers;
using SPTQuestMap.Services;

namespace SPTQuestMap.Controllers;

public sealed class QuestMapController(SaveServer saveServer, QuestMapDataService dataService) : Controller
{
    [HttpGet("/questmap")]
    public IActionResult Index()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(QuestMapController).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return Problem("SPT-QuestMap could not resolve its deployment directory.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var indexPath = Path.Combine(assemblyDirectory, "wwwroot", "questmap.html");
        if (!System.IO.File.Exists(indexPath))
        {
            return Problem("SPT-QuestMap is missing wwwroot/questmap.html. Redeploy the complete mod folder.", statusCode: StatusCodes.Status500InternalServerError);
        }

        Response.Headers.CacheControl = "no-cache";
        return PhysicalFile(indexPath, "text/html; charset=utf-8");
    }

    [HttpGet("/questmap/api/profiles")]
    public IActionResult Profiles([FromQuery] string? language = null)
    {
        var strings = dataService.GetBootstrap(language).Strings;
        var profiles = saveServer
            .GetProfiles()
            .Where(pair => !saveServer.IsProfileInvalidOrUnloadable(pair.Key))
            .Select(pair =>
            {
                var info = pair.Value.CharacterData?.PmcData?.Info;
                return new
                {
                    id = pair.Key.ToString(),
                    nickname = info?.Nickname ?? strings["profile.unnamed"],
                    side = info?.Side ?? strings["profile.unknownSide"],
                    level = info?.Level ?? 0,
                };
            })
            .Where(profile => ShouldIncludeProfile(profile.nickname, profile.level))
            .OrderBy(profile => profile.nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.id, StringComparer.Ordinal)
            .ToArray();

        Response.Headers.CacheControl = "no-store";
        return Ok(new { profiles });
    }

    internal static bool ShouldIncludeProfile(string? nickname, int level) =>
        level > 0 && !nickname?.StartsWith("headless_", StringComparison.OrdinalIgnoreCase) == true;

    [HttpGet("/questmap/api/topology")]
    public IActionResult Topology([FromQuery] string? language = null)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(dataService.GetTopology(language));
    }

    [HttpGet("/questmap/api/bootstrap")]
    public IActionResult Bootstrap([FromQuery] string? language = null)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(dataService.GetBootstrap(language));
    }

    [HttpGet("/questmap/api/profiles/{profileId}/state")]
    public IActionResult ProfileState(string profileId, [FromQuery] string? language = null)
    {
        var state = dataService.GetProfileState(profileId);
        if (state is null)
        {
            return NotFound(new { code = "profile_not_found", message = dataService.GetBootstrap(language).Strings["error.profileNotFound"] });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(state);
    }
}
