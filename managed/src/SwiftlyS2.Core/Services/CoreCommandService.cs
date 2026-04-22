using System.Runtime;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Core.Plugins;
using SwiftlyS2.Core.Natives;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Plugins;

namespace SwiftlyS2.Core.Services;

internal class CoreCommandService
{
    private readonly ILogger<CoreCommandService> logger;
    private readonly ISwiftlyCore core;
    private readonly PluginManager pluginManager;
    private readonly RootDirService rootDirService;
    private readonly ProfileService profileService;

    public CoreCommandService( ILogger<CoreCommandService> logger, ISwiftlyCore core, PluginManager pluginManager, RootDirService rootDirService, ProfileService profileService )
    {
        this.logger = logger;
        this.core = core;
        this.pluginManager = pluginManager;
        this.rootDirService = rootDirService;
        this.profileService = profileService;
        _ = core.Command.RegisterCommand("sw", OnCommand, true, helpText: "SwiftlyS2 Core Command");
    }

    private void EmitCommandOutput( ICommandContext context, string message, bool asWarning = false )
    {
        if (context.IsSentByPlayer)
        {
            context.Reply(message);
        }
        else if (asWarning)
        {
            logger.LogWarning("{Output}", message);
        }
        else
        {
            logger.LogInformation("{Output}", message);
        }
    }

    private void OnCommand( ICommandContext context )
    {
        void ShowPlayerList()
        {
            var output = string.Join("\n", [
                $"Connected players: {core.PlayerManager.PlayerCount}/{core.Engine.GlobalVars.MaxClients}",
                ..core.PlayerManager.GetAllValidPlayers().Select(player => $"{player.PlayerID}. {player.Controller?.PlayerName}{(player.IsFakeClient ? " (BOT)" : "")} (steamid={player.SteamID})")
            ]);
            EmitCommandOutput(context, output);
        }

        void ShowServerStatus()
        {
            var uptime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
            ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            var busyWorkerThreads = maxWorkerThreads - availableWorkerThreads;
            var processThreadCount = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;

            var output = string.Join("\n", [
                $"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
                $"Managed Heap Memory: {GC.GetTotalMemory(false) / 1024.0f / 1024.0f:0.00} MB",
                $"Process Threads: {processThreadCount}",
                $"ThreadPool Worker Threads: {busyWorkerThreads}/{maxWorkerThreads} (Busy/Max)",
                $"ThreadPool Completion Port Threads: {maxCompletionPortThreads - availableCompletionPortThreads}/{maxCompletionPortThreads} (Busy/Max)",
                $"Loaded Plugins: {pluginManager.GetPlugins().Count}",
                $"Players: {core.PlayerManager.PlayerCount}/{core.Engine.GlobalVars.MaxClients}",
                $"Map: {core.Engine.GlobalVars.MapName.Value}",
            ]);
            EmitCommandOutput(context, output);
        }

        void ShowVersionInfo()
        {
            var output = string.Join("\n", [
                $"SwiftlyS2 Version: {NativeEngineHelpers.GetNativeVersion()}",
                $"SwiftlyS2 Managed Version: {Assembly.GetExecutingAssembly().GetName().Version}",
                $"SwiftlyS2 Runtime Version: {Environment.Version}",
                $"SwiftlyS2 C++ Version: C++23",
                $"SwiftlyS2 .NET Version: {RuntimeInformation.FrameworkDescription}",
                $"GitHub URL: https://github.com/swiftly-solution/swiftlys2"
            ]);
            EmitCommandOutput(context, output);
        }

        void ShowGarbageCollectionInfo()
        {
            var output = string.Join("\n", [
                $"Garbage Collection Information:",
                $"  - Total Memory: {GC.GetTotalMemory(false) / 1024.0f / 1024.0f:0.00} MB",
                $"  - Is Server GC: {GCSettings.IsServerGC}",
                $"  - Max Generation: {GC.MaxGeneration}",
                ..Enumerable.Range(0, GC.MaxGeneration + 1).Select(i => $"    - Generation {i} Collection Count: {GC.CollectionCount(i)}"),
                $"  - Latency Mode: {GCSettings.LatencyMode}"
            ]);
            EmitCommandOutput(context, output);
        }

        void ShowCredits()
        {
            var output = string.Join("\n", [
                "SwiftlyS2 was created and developed by Swiftly Solution SRL and the contributors.",
                "SwiftlyS2 is licensed under the GNU General Public License v3.0 or later.",
                "Website: https://swiftlys2.net/",
                "GitHub: https://github.com/swiftly-solution/swiftlys2"
            ]);
            EmitCommandOutput(context, output);
        }

        bool RequireConsoleAccess()
        {
            if (context.IsSentByPlayer)
            {
                context.Reply("This command can only be executed from the server console.");
                return false;
            }
            return true;
        }

        try
        {
            var args = context.Args;
            if (args.Length == 0)
            {
                ShowHelp(context);
                return;
            }

            switch (args[0].Trim().ToLower())
            {
                case "help":
                    ShowHelp(context);
                    break;
                case "credits":
                    ShowCredits();
                    break;
                case "list":
                    ShowPlayerList();
                    break;
                case "status":
                    ShowServerStatus();
                    break;
                case "version":
                    ShowVersionInfo();
                    break;
                case "gc" when RequireConsoleAccess():
                    ShowGarbageCollectionInfo();
                    break;
                case "plugins" when RequireConsoleAccess():
                    PluginCommand(context);
                    break;
                case "profiler" when RequireConsoleAccess():
                    ProfilerCommand(context);
                    break;
                case "confilter" when RequireConsoleAccess():
                    ConfilterCommand(context);
                    break;
                case "translations" when RequireConsoleAccess():
                    TranslationsCommand(context);
                    break;
                case "cmds" when RequireConsoleAccess():
                    CommandsCommand(context);
                    break;
                default:
                    ShowHelp(context);
                    break;
            }
        }
        catch (Exception e)
        {
            if (!GlobalExceptionHandler.Handle(ref e))
            {
                return;
            }
            logger.LogError(e, "Failed to execute command");
        }
    }

    private void ShowHelp( ICommandContext context )
    {
        if (context.IsSentByPlayer)
        {
            context.Reply(string.Join(Environment.NewLine, [
                "credits              List Swiftly credits",
                "help                 Show the help for Swiftly Commands",
                "list                 Show the list of online players",
                "status               Show the status of the server",
                "version              Display Swiftly version",
            ]));
            return;
        }
        var table = new Table()
            .AddColumn("Command").AddColumn("Description")
            .AddRow("credits", "List Swiftly credits")
            .AddRow("help", "Show the help for Swiftly Commands")
            .AddRow("list", "Show the list of online players")
            .AddRow("status", "Show the status of the server")
            .AddRow("cmds", "List all plugin commands")
            .AddRow("confilter", "Console Filter Menu")
            .AddRow("plugins", "Plugin Management Menu")
            .AddRow("gc", "Show garbage collection information on managed")
            .AddRow("profiler", "Profiler Menu")
            .AddRow("translations", "Translations Menu")
            .AddRow("version", "Display Swiftly version");
        AnsiConsole.Write(table);
    }

    private void TranslationsCommand( ICommandContext context )
    {
        void ShowTranslationsHelp()
        {
            if (context.IsSentByPlayer)
            {
                context.Reply("reload                Reload all translations");
                return;
            }
            var table = new Table()
                .AddColumn("Command")
                .AddColumn("Description")
                .AddRow("reload", "Reload all translations");
            AnsiConsole.Write(table);
        }

        void ReloadTranslations()
        {
            pluginManager.RegenerateTranslations();

            EmitCommandOutput(context, "Succesfully reloaded the translations");
        }

        var args = context.Args;
        if (args.Length == 1)
        {
            ShowTranslationsHelp();
            return;
        }

        switch (args[1].Trim().ToLower())
        {
            case "reload":
                ReloadTranslations();
                break;
            default:
                EmitCommandOutput(context, "Unknown command", asWarning: true);
                break;
        }
    }

    private void ConfilterCommand( ICommandContext context )
    {
        void ShowConfilterHelp()
        {
            if (context.IsSentByPlayer)
            {
                context.Reply(string.Join(Environment.NewLine, [
                    "enable               Enable console filtering",
                    "disable              Disable console filtering",
                    "status               Show the status of the console filter",
                    "reload               Reload console filter configuration",
                ]));
                return;
            }
            var table = new Table()
                .AddColumn("Command")
                .AddColumn("Description")
                .AddRow("enable", "Enable console filtering")
                .AddRow("disable", "Disable console filtering")
                .AddRow("status", "Show the status of the console filter")
                .AddRow("reload", "Reload console filter configuration");
            AnsiConsole.Write(table);
        }

        void EnableFilter()
        {
            if (!core.ConsoleOutput.IsFilterEnabled())
            {
                core.ConsoleOutput.ToggleFilter();
            }
            EmitCommandOutput(context, "Console filtering has been enabled.");
        }

        void DisableFilter()
        {
            if (core.ConsoleOutput.IsFilterEnabled())
            {
                core.ConsoleOutput.ToggleFilter();
            }
            EmitCommandOutput(context, "Console filtering has been disabled.");
        }

        void ShowFilterStatus()
        {
            var status = core.ConsoleOutput.IsFilterEnabled() ? "enabled" : "disabled";
            var output = string.Join("\n", [
                $"Console filtering is currently {status}.",
                "Below are some statistics for the filtering process:",
                core.ConsoleOutput.GetCounterText()
            ]);
            EmitCommandOutput(context, output);
        }

        void ReloadFilter()
        {
            core.ConsoleOutput.ReloadFilterConfiguration();
            EmitCommandOutput(context, "Console filter configuration reloaded.");
        }

        var args = context.Args;
        if (args.Length == 1)
        {
            ShowConfilterHelp();
            return;
        }

        switch (args[1].Trim().ToLower())
        {
            case "enable":
                EnableFilter();
                break;
            case "disable":
                DisableFilter();
                break;
            case "status":
                ShowFilterStatus();
                break;
            case "reload":
                ReloadFilter();
                break;
            default:
                EmitCommandOutput(context, "Unknown command", asWarning: true);
                break;
        }
    }

    private void ProfilerCommand( ICommandContext context )
    {
        var args = context.Args;
        if (args.Length == 1)
        {
            if (context.IsSentByPlayer)
            {
                context.Reply(string.Join(Environment.NewLine, [
                    "enable               Enable the profiler",
                    "disable              Disable the profiler",
                    "status               Show the status of the profiler",
                    "save                 Save the profiler data to a file",
                ]));
            }
            else
            {
                var table = new Table().AddColumn("Command").AddColumn("Description")
                    .AddRow("enable", "Enable the profiler")
                    .AddRow("disable", "Disable the profiler")
                    .AddRow("status", "Show the status of the profiler")
                    .AddRow("save", "Save the profiler data to a file");
                AnsiConsole.Write(table);
            }
            return;
        }

        switch (args[1].Trim().ToLower())
        {
            case "enable":
                profileService.Enable();
                EmitCommandOutput(context, "The profiler has been enabled.");
                break;
            case "disable":
                profileService.Disable();
                EmitCommandOutput(context, "The profiler has been disabled.");
                break;
            case "status":
                EmitCommandOutput(context, $"Profiler is currently {(profileService.IsEnabled() ? "enabled" : "disabled")}.");
                break;
            case "save":
                var pluginId = args.Length >= 3 ? args[2] : "core";
                var profilerDir = Path.Combine(rootDirService.GetRoot(), "profilers");

                if (!Directory.Exists(profilerDir))
                {
                    _ = Directory.CreateDirectory(profilerDir);
                }

                var fileName = $"{DateTime.Now:yyyyMMdd}.{Guid.NewGuid()}.{pluginId}.json";
                var filePath = Path.Combine(profilerDir, fileName);

                File.WriteAllText(filePath, profileService.GenerateJSONPerformance(args.Length >= 3 ? args[2] : string.Empty));
                EmitCommandOutput(context, $"Profile saved to {filePath}.");
                break;
            default:
                EmitCommandOutput(context, "Unknown command", asWarning: true);
                break;
        }
    }

    private void PluginCommand( ICommandContext context )
    {
        void ShowPluginList()
        {
            if (context.IsSentByPlayer)
            {
                var sb = new StringBuilder();
                foreach (var plugin in pluginManager.GetPlugins())
                {
                    var pluginId = plugin.Metadata?.Id ?? "<Unknown>";
                    var version = plugin.Metadata?.Version is { } v ? $" {v}" : string.Empty;
                    var statusText = GetColoredStatus(plugin.Status);
                    var author = plugin.Metadata?.Author ?? "Anonymous";
                    var website = plugin.Metadata?.Website ?? string.Empty;
                    var location = plugin.PluginDirectory is { } dir ? Path.Join("(swRoot)", Path.GetRelativePath(rootDirService.GetRoot(), dir)) : string.Empty;
                    _ = sb.AppendLine($"{statusText} | {pluginId}{version} | {author} | {website} | {location}");
                }
                var playerListLoadErrors = pluginManager.GetPluginLoadErrors();
                if (playerListLoadErrors.Count > 0)
                {
                    _ = sb.AppendLine();
                    _ = sb.AppendLine("Plugin Load Errors:");
                    foreach (var error in playerListLoadErrors)
                    {
                        _ = sb.AppendLine($"  {error.Key}: {error.Value}");
                    }
                }
                context.Reply(sb.ToString().TrimEnd());
                return;
            }
            var table = new Table()
                .AddColumn("Status")
                .AddColumn("PluginId (ver.)")
                .AddColumn("Author")
                .AddColumn("Website")
                .AddColumn("Location");

            foreach (var plugin in pluginManager.GetPlugins())
            {
                var pluginId = Markup.Escape(plugin.Metadata?.Id ?? "<Unknown>");
                var version = Markup.Escape(plugin.Metadata?.Version is { } v ? $" {v}" : string.Empty);
                var statusText = GetColoredStatus(plugin.Status);

                _ = table.AddRow(
                    statusText,
                    $"{pluginId}{version}",
                    Markup.Escape(plugin.Metadata?.Author ?? "Anonymous"),
                    Markup.Escape(plugin.Metadata?.Website ?? string.Empty),
                    Markup.Escape(plugin.PluginDirectory is { } dir ? Path.Join("(swRoot)", Path.GetRelativePath(rootDirService.GetRoot(), dir)) : string.Empty));
            }

            AnsiConsole.Write(table);

            var loadErrors = pluginManager.GetPluginLoadErrors();
            if (loadErrors.Count > 0)
            {
                Console.WriteLine("\n");
                var errorString = "Plugin Load Errors:";
                foreach (var error in loadErrors)
                {
                    errorString += $"\n  {error.Key}: {error.Value}";
                }
                logger.LogWarning(errorString);
            }
        }

        void ShowPluginHelp()
        {
            if (context.IsSentByPlayer)
            {
                context.Reply(string.Join(Environment.NewLine, [
                    "list                 List all plugins",
                    "load                 Load a plugin",
                    "unload               Unload a plugin",
                    "reload               Reload a plugin",
                ]));
                return;
            }
            var table = new Table()
                .AddColumn("Command")
                .AddColumn("Description")
                .AddRow("list", "List all plugins")
                .AddRow("load", "Load a plugin")
                .AddRow("unload", "Unload a plugin")
                .AddRow("reload", "Reload a plugin");
            AnsiConsole.Write(table);
        }

        bool ValidatePluginId( string[] args, string command, string usage )
        {
            if (args.Length >= 3)
            {
                return true;
            }
            if (context.IsSentByPlayer)
            {
                context.Reply($"Usage: sw plugins {command} {usage}");
            }
            else
            {
                logger.LogWarning("Usage: sw plugins {Command} {Usage}", command, usage);
            }
            return false;
        }

        string GetColoredStatus( PluginStatus? status ) => status switch {
            // PluginStatus.Loaded => "[green]Loaded[/]",
            // PluginStatus.Error => "[red]Error[/]",
            // PluginStatus.Loading => "[yellow]Loading[/]",
            // PluginStatus.Unloaded => "[grey]Unloaded[/]",
            // _ => "[grey]Unknown[/]"
            PluginStatus.Loaded => "Loaded",
            PluginStatus.Error => "Error",
            PluginStatus.Loading => "Loading",
            PluginStatus.Unloaded => "Unloaded",
            PluginStatus.Indeterminate => "Indeterminate",
            _ => "Unknown"
        };

        var args = context.Args;
        if (args.Length == 1)
        {
            ShowPluginHelp();
            return;
        }

        switch (args[1].Trim().ToLower())
        {
            case "list":
                ShowPluginList();
                break;
            case "load":
                if (ValidatePluginId(args, "load", "<dllName>"))
                {
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                    if (pluginManager.GetPluginStatusByDllName(args[2]) == PluginStatus.Loaded)
                    {
                        EmitCommandOutput(context, $"Plugin is already loaded: {args[2]}", asWarning: true);
                        if (!context.IsSentByPlayer)
                        {
                            Console.WriteLine("\n");
                        }
                        break;
                    }

                    if (pluginManager.LoadPluginByDllName(args[2], true))
                    {
                        EmitCommandOutput(context, $"Loaded plugin: {args[2]}");
                    }
                    else
                    {
                        EmitCommandOutput(context, $"Failed to load plugin: {args[2]}", asWarning: true);
                    }
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                }
                break;
            case "unload":
                if (ValidatePluginId(args, "unload", "<dllName>"))
                {
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                    if (pluginManager.UnloadPluginByDllName(args[2], true))
                    {
                        EmitCommandOutput(context, $"Unloaded plugin: {args[2]}");
                    }
                    else
                    {
                        EmitCommandOutput(context, $"Failed to unload plugin: {args[2]}", asWarning: true);
                    }
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                }
                break;
            case "reload":
                if (ValidatePluginId(args, "reload", "<dllName>"))
                {
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                    if (pluginManager.ReloadPluginByDllName(args[2], true))
                    {
                        EmitCommandOutput(context, $"Reloaded plugin: {args[2]}");
                    }
                    else
                    {
                        EmitCommandOutput(context, $"Failed to reload plugin: {args[2]}", asWarning: true);
                    }
                    if (!context.IsSentByPlayer)
                    {
                        Console.WriteLine("\n");
                    }
                }
                break;
            default:
                EmitCommandOutput(context, "Unknown command", asWarning: true);
                break;
        }
    }

    private void CommandsCommand( ICommandContext context )
    {
        var commandsByPlugin = core.Command.GetAllCommandsByPlugin();

        if (commandsByPlugin.Count == 0)
        {
            EmitCommandOutput(context, "No commands registered.");
            return;
        }

        if (context.IsSentByPlayer)
        {
            var sb = new StringBuilder();
            foreach (var pluginEntry in commandsByPlugin.OrderBy(x => x.Key))
            {
                var pluginName = pluginEntry.Key;
                var isFirstRow = true;

                foreach (var command in pluginEntry.Value.OrderBy(x => x.CommandName))
                {
                    var perm = string.IsNullOrWhiteSpace(command.Permission) ? "(none)" : command.Permission;
                    _ = sb.AppendLine($"{(isFirstRow ? pluginName : string.Empty)} | {command.CommandName} | {command.HelpText} | {perm}");
                    isFirstRow = false;
                }
            }
            context.Reply(sb.ToString().TrimEnd());
            return;
        }

        var table = new Table()
            .AddColumn("Plugin")
            .AddColumn("Command Name")
            .AddColumn("Help Text")
            .AddColumn("Permission");

        foreach (var pluginEntry in commandsByPlugin.OrderBy(x => x.Key))
        {
            var pluginName = pluginEntry.Key;
            var isFirstRow = true;

            foreach (var command in pluginEntry.Value.OrderBy(x => x.CommandName))
            {
                if (isFirstRow)
                {
                    _ = table.AddRow(
                        pluginName,
                        command.CommandName,
                        Markup.Escape(command.HelpText),
                        string.IsNullOrWhiteSpace(command.Permission) ? "(none)" : command.Permission);
                    isFirstRow = false;
                }
                else
                {
                    _ = table.AddRow(
                        string.Empty,
                        command.CommandName,
                        Markup.Escape(command.HelpText),
                        string.IsNullOrWhiteSpace(command.Permission) ? "(none)" : command.Permission);
                }
            }
        }

        AnsiConsole.Write(table);
    }
}