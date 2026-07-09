using System.Reflection;
using FiveStack.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using FiveStack.Entities;

namespace FiveStack;

[PluginMetadata(
    Id = "FiveStack",
    Version = "__RELEASE_VERSION__",
    Name = "5stack",
    Author = "5Stack.gg",
    Description = "5Stack creates and managements custom matches"
)]
public partial class FiveStackPlugin : BasePlugin
{
    private ILogger<FiveStackPlugin> _logger = null!;
    private ILocalizer _localizer = null!;
    private IServiceProvider _serviceProvider = null!;

    private SteamService _steamService = null!;
    private GameDemos _gameDemos = null!;
    private GameServer _gameServer = null!;
    private MatchEvents _matchEvents = null!;
    private MatchService _matchService = null!;
    private CaptainSystem _captainSystem = null!;
    private CoachSystem _coachSystem = null!;
    private RankSystem _rankSystem = null!;
    private SurrenderSystem _surrenderSystem = null!;
    private GameBackUpRounds _gameBackupRounds = null!;
    private EnvironmentService _environmentService = null!;

    private CancellationTokenSource? _pingTimer;
    private Guid _chatHookId;
    private Guid _commandHookId;
    private EventDelegates.OnPrecacheResource? _precacheHandler;

    public FiveStackPlugin(ISwiftlyCore core)
        : base(core) { }

    private ISwiftlyCore _core => Core;

    public string ModuleVersion =>
        typeof(FiveStackPlugin).GetCustomAttribute<PluginMetadata>()?.Version ?? "unknown";

    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeJsonWithModel<FiveStackConfig>("config.jsonc", "FiveStack")
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: true, reloadOnChange: true);
            });

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.jsonc", optional: true, reloadOnChange: true)
            .Build();

        ServiceCollection services = new();
        services
            .AddSwiftly(Core)
            .AddSingleton<IConfiguration>(configuration)
            .Configure<FiveStackConfig>(config =>
                configuration.GetSection("FiveStack").Bind(config)
            )
            .AddSingleton<EnvironmentService>()
            .AddSingleton<SteamService>()
            .AddSingleton<GameServer>()
            .AddSingleton<MatchService>()
            .AddSingleton<MatchEvents>()
            .AddSingleton<GameDemos>()
            .AddSingleton<GameBackUpRounds>()
            .AddSingleton<SurrenderSystem>()
            .AddSingleton<CoachSystem>()
            .AddSingleton<CaptainSystem>()
            .AddSingleton<RankSystem>()
            .AddTransient<MatchManager>()
            .AddTransient<VoteSystem>()
            .AddTransient<KnifeSystem>()
            .AddTransient<ReadySystem>()
            .AddTransient<TimeoutSystem>();

        _serviceProvider = services.BuildServiceProvider();

        _logger = _serviceProvider.GetRequiredService<ILogger<FiveStackPlugin>>();
        _localizer = _serviceProvider.GetRequiredService<ILocalizer>();

        MatchUtility.Initialize(Core);
        TimerUtility.Initialize(Core);

        _environmentService = _serviceProvider.GetRequiredService<EnvironmentService>();
        _steamService = _serviceProvider.GetRequiredService<SteamService>();
        _gameServer = _serviceProvider.GetRequiredService<GameServer>();
        _matchService = _serviceProvider.GetRequiredService<MatchService>();
        _matchEvents = _serviceProvider.GetRequiredService<MatchEvents>();
        _gameDemos = _serviceProvider.GetRequiredService<GameDemos>();
        _gameBackupRounds = _serviceProvider.GetRequiredService<GameBackUpRounds>();
        _surrenderSystem = _serviceProvider.GetRequiredService<SurrenderSystem>();
        _coachSystem = _serviceProvider.GetRequiredService<CoachSystem>();
        _captainSystem = _serviceProvider.GetRequiredService<CaptainSystem>();
        _rankSystem = _serviceProvider.GetRequiredService<RankSystem>();

        _environmentService.Load();

        CommandUtility.Initialize(
            _environmentService.GetPublicChatTrigger(),
            _environmentService.GetSilentChatTrigger()
        );

        _logger.LogInformation($"Server ID: {_environmentService.GetServerId()}");

        InitializeConnectClientHook();

        Core.Event.OnTick += _rankSystem.OnTick;

        Core.Event.OnMapLoad += OnMapLoad;

        _precacheHandler = (@event) =>
        {
            @event.AddItem(ModelPathCtmSas);
            @event.AddItem(ModelPathTmPhoenix);
        };
        Core.Event.OnPrecacheResource += _precacheHandler;

        _chatHookId = Core.Command.HookClientChat(
            (playerId, text, teamonly) =>
            {
                IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

                string message = text.Trim('"');

                HookResult result = HookResult.Continue;

                if (!teamonly)
                {
                    HookResult chatResult = OnPlayerChat(player, message, teamonly);
                    if (chatResult != HookResult.Continue)
                    {
                        result = chatResult;
                    }
                }

                HookResult gagResult = GagPlayer(player, message, teamonly);
                if (gagResult != HookResult.Continue)
                {
                    result = gagResult;
                }

                return result;
            }
        );

        _commandHookId = Core.Command.HookClientCommand(
            (playerId, commandLine) =>
            {
                string[] args = commandLine.Split(' ');
                if (args.Length > 0 && args[0] == "jointeam")
                {
                    IPlayer? player = Core.PlayerManager.GetPlayer(playerId);
                    return HandleJoinTeam(player, args);
                }

                return HookResult.Continue;
            }
        );

        _ = _matchService.GetMatchConfigs();

        if (_environmentService.IsOfflineMode())
        {
            _matchService.GetMatchFromOffline();
        }
        else
        {
            _gameServer.Ping(ModuleVersion);
            _pingTimer = TimerUtility.Repeat(15, () => _gameServer.Ping(ModuleVersion));
        }
    }

    public override void Unload()
    {
        try
        {
            Core.Event.OnTick -= _rankSystem.OnTick;
            Core.Event.OnMapLoad -= OnMapLoad;

            if (_precacheHandler != null)
            {
                Core.Event.OnPrecacheResource -= _precacheHandler;
            }

            if (_chatHookId != Guid.Empty)
            {
                Core.Command.UnhookClientChat(_chatHookId);
            }

            if (_commandHookId != Guid.Empty)
            {
                Core.Command.UnhookClientCommand(_commandHookId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe hooks during unload");
        }

        UninstallConnectClientHook();

        TimerUtility.Kill(_pingTimer);

        _matchService.GetCurrentMatch()?.Reset();

        _ = _matchEvents.Disconnect();

        TimerUtility.ClearAll();
    }
}
