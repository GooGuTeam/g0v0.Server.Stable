// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Achievements;
using g0v0.Server.Common.Beatmaps;
using g0v0.Server.Common.Communication;
using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.MySQL;
using g0v0.Server.Common.Database.PostgreSQL;
using g0v0.Server.Common.Extensions;
using g0v0.Server.Common.Rulesets;
using g0v0.Server.Common.Scoring;
using g0v0.Server.Common.Screenshots;
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
        builder.Services.AddClientIp();
        builder.Services.AddHostedService<SessionCleanupService>();
        builder.Services.AddRedis(ServerIdentify.Stable);
        builder.Services.AddCache();
        builder.Services.AddFetcher();
        builder.Services.AddBeatmapSearch();
        builder.Services.AddAchievements();
        builder.Services.AddStorage();
        builder.Services.AddScoped<BeatmapFeedbackService>();
        builder.Services.AddScoped<BeatmapDownloadService>();
        builder.Services.AddScoped<ReplayService>();
        builder.Services.AddScoped<ScreenshotService>();

        builder.Services.AddSingleton<StableScoreSubmissionTracker>();
        builder.Services.AddSingleton<ScoreAttributeUtils>();
        builder.Services.AddScoped<ScoreProcessingService>();
        builder.Services.AddScoped<IScoreProcessingService>(provider => provider.GetRequiredService<ScoreProcessingService>());
        builder.Services.AddSingleton(provider => new ScoreProcessingQueue(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<ScoreProcessingQueue>>(),
            "stable",
            async (services, score, cancellationToken) =>
            {
                await services.GetRequiredService<ScoreProcessingService>().ProcessAsync(score, cancellationToken).ConfigureAwait(false);
                await services.GetRequiredService<IAchievementService>().UnlockAsync(score, cancellationToken).ConfigureAwait(false);
            },
            score => !score.Passed || score.HasReplay));
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ScoreProcessingQueue>());

        builder.Services.AddSingleton<IPlayerManager, PlayerManager>();
        builder.Services.AddSingleton<IChatServer, ChatServer>();
        builder.Services.AddSingleton<IRoomManager, RoomManager>();
        builder.Services.AddSingleton<BanchoBot>();
        builder.Services.AddSingleton<RoomCountdownService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<RoomCountdownService>());
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
        builder.Services.AddScoped<SeasonalRouteHandler>();
        builder.Services.AddScoped<RatingRouteHandler>();
        builder.Services.AddScoped<FavouritesRouteHandler>();
        builder.Services.AddScoped<AddFavouriteRouteHandler>();
        builder.Services.AddScoped<ReplayRouteHandler>();
        builder.Services.AddScoped<ScreenshotRouteHandler>();
        builder.Services.AddScoped<DifficultyRatingRouteHandler>();
        builder.Services.AddScoped<BeatmapDownloadRouteHandler>();
        builder.Services.AddScoped<BeatmapFileRouteHandler>();
        builder.Services.AddScoped<CallPeppyRouteHandler>();
        builder.Services.AddScoped<SearchRouteHandler>();
        builder.Services.AddScoped<SearchSetRouteHandler>();
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
        // Deliberately no UseForwardedHeaders: it overwrites Connection.RemoteIpAddress, which the
        // client IP resolver needs to verify that the forwarding headers came from a trusted proxy.
        // ClientIpResolver reads X-Forwarded-For/X-Real-IP itself (config/verification.json).
        app.UseHttpsRedirection();

        app.MapGet("/healthz", () => Results.Ok(new { Service = "g0v0.Server.Stable", Status = "ok" }));
        app.MapPost("/users", (HttpContext context, RegisterRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/login", (HttpContext context, LoginRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/packet", (HttpContext context, PacketRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/web/osu-submit-modular-selector.php", (HttpContext context, SubmitScoreRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/web/osu-getbeatmapinfo.php", (HttpContext context, BeatmapInfoRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-osz2-getscores.php", (HttpContext context, LeaderboardRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-getfriends.php", (HttpContext context, FriendsRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-search.php", (HttpContext context, SearchRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-search-set.php", (HttpContext context, SearchSetRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-getseasonal.php", (HttpContext context, SeasonalRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-rate.php", (HttpContext context, RatingRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-getreplay.php", (HttpContext context, ReplayRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-getfavourites.php", (HttpContext context, FavouritesRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/osu-addfavourite.php", (HttpContext context, AddFavouriteRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/web/osu-screenshot.php", (HttpContext context, ScreenshotRouteHandler handler) => handler.HandleAsync(context));
        app.MapPost("/difficulty-rating", (HttpContext context, DifficultyRatingRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/d/{map_set_id}", (HttpContext context, BeatmapDownloadRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/web/maps/{map_filename}", (HttpContext context, BeatmapFileRouteHandler handler) => handler.HandleAsync(context));
        app.MapGet("/p/doyoureallywanttoaskpeppy", (HttpContext context, CallPeppyRouteHandler handler) => handler.HandleAsync(context));

        // TODO: reverse-engineer found `/web/bancho_connect.php` will upload system registry and other information to the server.
        app.MapGet("/web/bancho_connect.php", () => Results.Empty);

        return app;
    }
}