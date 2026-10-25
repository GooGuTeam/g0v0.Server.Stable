// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.MySQL;
using g0v0.Server.Common.Database.PostgreSQL;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Utils;
using g0v0.Server.Stable.Manager;
using g0v0.Server.Stable.Packets;
using g0v0.Server.Stable.Routes;
using g0v0.Server.Stable.Services;
using Microsoft.EntityFrameworkCore;

namespace g0v0.Server.Stable;

/// <summary>
/// Application entry point for the g0v0 stable server.
/// </summary>
public static class Program
{
    /// <summary>
    /// Configures and runs the web host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        ConfigureApp(builder.Build()).Run();
    }

    /// <summary>
    /// Registers every service the stable protocol needs.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <remarks>
    /// The stable client speaks three protocols to this server: the unframed login and packet forms
    /// on <c>POST /</c> and <c>POST /packet</c>, and the legacy <c>/web/*.php</c> endpoints. Sessions
    /// live in <see cref="IPlayerManager"/>, channels in <see cref="IChatServer"/>; both are
    /// singletons because they outlive a request, while every handler is scoped so it can resolve a
    /// database context per request.
    /// </remarks>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IPathProvider, WebPathProvider>();

        ConfigManager configs = new(builder.Environment.ContentRootPath);
        GeneralConfig general = configs.Get<GeneralConfig>();
        builder.Services.AddSingleton(configs);

        builder.Services.AddRepositories(general.UseLegacyDatabase);
        if (general.UseLegacyDatabase)
        {
            builder.Services.AddDbContext<MysqlDbContext>(options =>
                options.UseMySql(general.MySqlConnectionString, new MySqlServerVersion(new Version(8, 0, 36))));
        }
        else
        {
            builder.Services.AddDbContext<PostgreSqlDbContext>(options =>
                options.UseNpgsql(general.PostgresqlConnectionString));
        }

        builder.Services.AddGeoIP();
        builder.Services.AddRedis(ServerIdentify.Stable);
        builder.Services.AddCache();
        builder.Services.AddFetcher();
        builder.Services.AddStorage();

        builder.Services.AddSingleton<StableScoreSubmissionTracker>();
        builder.Services.AddSingleton<ScoreAttributeUtils>();
        builder.Services.AddScoped<ScoreProcessingService>();
        builder.Services.AddScoped<IScoreProcessingService>(provider => provider.GetRequiredService<ScoreProcessingService>());
        builder.Services.AddSingleton(provider => new ScoreProcessingQueue(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<ScoreProcessingQueue>>(),
            "stable",
            (services, score, cancellationToken) => services.GetRequiredService<ScoreProcessingService>().ProcessAsync(score, cancellationToken),
            score => !score.Passed || score.HasReplay));
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ScoreProcessingQueue>());

        builder.Services.AddSingleton<IPlayerManager, PlayerManager>();
        builder.Services.AddSingleton<IChatServer, ChatServer>();
        builder.Services.AddSingleton<IRoomManager, RoomManager>();
        builder.Services.AddSingleton(_ => new RulesetManager(Path.Combine(builder.Environment.ContentRootPath, "rulesets")));
        builder.Services.AddScoped<IPacketFacade>(provider => new PacketFacade
        {
            ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>(),
            PlayerManager = provider.GetRequiredService<IPlayerManager>(),
            ChatServer = provider.GetRequiredService<IChatServer>(),
            RoomManager = provider.GetRequiredService<IRoomManager>(),
            RulesetManager = provider.GetRequiredService<RulesetManager>()
        });

        builder.Services.AddScoped<StableWebAuthenticator>();
        builder.Services.AddScoped<RegisterRouteHandler>();
        builder.Services.AddScoped<LoginRouteHandler>();
        builder.Services.AddScoped<PacketRouteHandler>();
        builder.Services.AddScoped<SubmitScoreRouteHandler>();
        builder.Services.AddScoped<BeatmapInfoRouteHandler>();
        builder.Services.AddScoped<LeaderboardRouteHandler>();
        builder.Services.AddScoped<FriendsRouteHandler>();
    }

    /// <summary>
    /// Builds the request pipeline and maps every stable endpoint.
    /// </summary>
    /// <param name="app">The configured application.</param>
    /// <returns>The application, ready to run.</returns>
    /// <remarks>
    /// The <c>/web/*.php</c> endpoints authenticate inside their handler with the username and
    /// password MD5 the client puts in the query string, so they are deliberately not behind an
    /// authorization middleware.
    /// </remarks>
    public static WebApplication ConfigureApp(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseHttpsRedirection();

        app.MapGet("/healthz", () => Results.Ok(new { Service = "g0v0.Server.Stable", Status = "ok" }));
        app.MapPost("/users", (HttpContext context, RegisterRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/", (HttpContext context, LoginRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/packet", (HttpContext context, PacketRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/web/osu-submit-modular-selector.php", (HttpContext context, SubmitScoreRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/web/osu-getbeatmapinfo.php", (HttpContext context, BeatmapInfoRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-osz2-getscores.php", (HttpContext context, LeaderboardRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-getfriends.php", (HttpContext context, FriendsRouteHandler handler) => handler.HandleAsync(context));

        return app;
    }
}