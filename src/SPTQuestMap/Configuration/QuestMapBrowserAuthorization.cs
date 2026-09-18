using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SPTarkov.Server.Core.DI;

namespace SPTQuestMap.Configuration;

public sealed class QuestMapBrowserAuthorization(
    QuestMapServerConfiguration configuration,
    ILogger<QuestMapBrowserAuthorization> logger) : IOnDIConstruct, IOnLoad
{
    public const string PolicyName = "QuestMapBrowser";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("QuestMap browser authentication: {Mode}",
            configuration.RequireBrowserAuthentication ? "authenticated SPT users" : "anonymous access");
        return Task.CompletedTask;
    }

    public static Task OnDIConstructAsync(IServiceCollection services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        services.AddSingleton(provider =>
        {
            var directory = Path.GetDirectoryName(typeof(QuestMapServerConfiguration).Assembly.Location)
                ?? throw new InvalidOperationException("Could not locate the QuestMap server configuration directory.");
            var logger = provider.GetRequiredService<ILogger<QuestMapServerConfiguration>>();
            return QuestMapServerConfiguration.Load(Path.Combine(directory, "config.json"),
                message => logger.LogWarning("{Message}", message));
        });
        // Resolve configuration during normal startup, creating its default file before the first page request.
        services.AddSingleton<IOnLoad, QuestMapBrowserAuthorization>();
        // Reuse SPT's authenticated-user policy without changing other pages' authorization.
        services.AddOptions<AuthorizationOptions>().PostConfigure<QuestMapServerConfiguration>((options, configuration) =>
            options.AddPolicy(PolicyName, configuration.RequireBrowserAuthentication
                ? options.DefaultPolicy
                : new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build()));
        return Task.CompletedTask;
    }
}
