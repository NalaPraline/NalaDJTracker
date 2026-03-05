using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using Nala.Windows;
using Nala.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Nala;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private const string CommandName = "/dj";
    private const string ConfigCommandName = "/djconfig";
    private const string ScheduleCommandName = "/djschedule";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("DJTracker");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private SchedulingWindow SchedulingWindow { get; init; }

    public TwitchService TwitchService { get; init; }

    private TwitchPlayerWindow twitchPlayer;

    // Current state of DJ streams
    public List<DjStreamer> DjStreamers { get; private set; } = new();

    // Timer tracking
    private DateTime lastRefresh = DateTime.MinValue;
    private bool isRefreshing;

    public Plugin()
    {
        // Initialize ECommons
        ECommonsMain.Init(PluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize non-serialized fields after deserialization
        Configuration.NotifiedStreamers ??= new HashSet<string>();

        TwitchService = new TwitchService(Log);
        twitchPlayer = new TwitchPlayerWindow();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        SchedulingWindow = new SchedulingWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(SchedulingWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the DJ Tracker window"
        });

        CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the DJ Tracker configuration window"
        });

        CommandManager.AddHandler(ScheduleCommandName, new CommandInfo(OnScheduleCommand)
        {
            HelpMessage = "Open the DJ Tracker scheduler window"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Subscribe to framework update for periodic refresh
        Framework.Update += OnFrameworkUpdate;

        Log.Information("DJ Tracker loaded!");

        // Trigger initial refresh
        _ = RefreshStreamsAsync();
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        SchedulingWindow.Dispose();

        TwitchService.Dispose();
        twitchPlayer.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(ScheduleCommandName);

        // Dispose ECommons
        ECommonsMain.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Twitch API refresh
        if (Configuration.DjList.Count > 0)
        {
            var elapsed = DateTime.UtcNow - lastRefresh;
            if (elapsed.TotalSeconds >= Configuration.RefreshIntervalSeconds)
            {
                _ = RefreshStreamsAsync();
            }
        }

        // Scheduler logic
        ProcessScheduler();
    }

    private void ProcessScheduler()
    {
        if (!Configuration.SchedulerEnabled || Configuration.ShoutSchedule.Count == 0)
        {
            return;
        }

        // Check if we're in a transition period
        if (Configuration.TransitionStartTime.HasValue)
        {
            var transitionElapsed = DateTime.UtcNow - Configuration.TransitionStartTime.Value;
            if (transitionElapsed.TotalSeconds >= Configuration.TransitionDelaySeconds)
            {
                // Transition complete, start the next DJ
                CompleteTransition();
            }
            // During transition, do nothing
            return;
        }

        var currentShout = Configuration.ShoutSchedule[0];

        // Initialize slot start time if not set
        if (!currentShout.SlotStartTime.HasValue)
        {
            currentShout.SlotStartTime = DateTime.UtcNow;
        }

        // Check if current slot has expired
        var slotElapsed = DateTime.UtcNow - currentShout.SlotStartTime.Value;
        if (slotElapsed.TotalMinutes >= currentShout.DurationMinutes)
        {
            // Start transition to next DJ
            StartTransition();
            return;
        }

        // Check if we should yell
        if (!currentShout.LastYellTime.HasValue ||
            (DateTime.UtcNow - currentShout.LastYellTime.Value).TotalMinutes >= currentShout.YellIntervalMinutes)
        {
            // Check if DJ is live (if required)
            if (Configuration.OnlyShoutLiveDjs)
            {
                var djInfo = DjStreamers.FirstOrDefault(d =>
                    d.TwitchUsername.Equals(currentShout.TwitchUsername, StringComparison.OrdinalIgnoreCase));

                if (djInfo == null || !djInfo.IsLive)
                {
                    // Skip this yell, DJ is offline
                    currentShout.LastYellTime = DateTime.UtcNow;
                    Log.Information($"Skipped shout for {currentShout.DisplayName} - offline");
                    return;
                }
            }

            // Send the yell
            SendScheduledShout(currentShout);
            currentShout.LastYellTime = DateTime.UtcNow;
        }
    }

    private void StartTransition()
    {
        if (Configuration.ShoutSchedule.Count == 0)
        {
            Configuration.SchedulerEnabled = false;
            Configuration.Save();
            return;
        }

        // Remove the current DJ from the queue
        var finished = Configuration.ShoutSchedule[0];
        Configuration.ShoutSchedule.RemoveAt(0);

        Log.Information($"Finished promoting {finished.DisplayName}");
        ChatGui.Print($"[DJ Tracker] Finished promoting {finished.DisplayName}");

        if (Configuration.ShoutSchedule.Count > 0)
        {
            // Start transition period
            Configuration.TransitionStartTime = DateTime.UtcNow;
            var next = Configuration.ShoutSchedule[0];
            ChatGui.Print($"[DJ Tracker] Next up: {next.DisplayName} (starting in {Configuration.TransitionDelaySeconds}s)");
        }
        else
        {
            Configuration.SchedulerEnabled = false;
            Configuration.TransitionStartTime = null;
            ChatGui.Print("[DJ Tracker] Schedule complete! All DJs have been promoted.");
        }

        Configuration.Save();
    }

    private void CompleteTransition()
    {
        Configuration.TransitionStartTime = null;

        if (Configuration.ShoutSchedule.Count > 0)
        {
            var next = Configuration.ShoutSchedule[0];
            next.SlotStartTime = DateTime.UtcNow;
            next.LastYellTime = null;
            ChatGui.Print($"[DJ Tracker] Now promoting: {next.DisplayName}");
            Log.Information($"Started promoting {next.DisplayName}");
        }
        else
        {
            Configuration.SchedulerEnabled = false;
            ChatGui.Print("[DJ Tracker] Schedule complete! All DJs have been promoted.");
        }
    }

    private void SendScheduledShout(ScheduledShout shout)
    {
        var twitchUrl = $"https://twitch.tv/{shout.TwitchUsername}";

        // Get live info if available
        var djInfo = DjStreamers.FirstOrDefault(d =>
            d.TwitchUsername.Equals(shout.TwitchUsername, StringComparison.OrdinalIgnoreCase));

        var displayName = djInfo?.DisplayName ?? shout.DisplayName;
        var message = FormatShoutMessage(displayName, twitchUrl, djInfo?.IsLive ?? false);

        try
        {
            Chat.ExecuteCommand($"/yell {message}");
            Log.Information($"Scheduled shout sent for {shout.DisplayName}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to send scheduled shout: {ex.Message}");
        }
    }

    public string FormatShoutMessage(string displayName, string twitchUrl, bool isLive)
    {
        var template = isLive ? Configuration.ShoutMessageLive : Configuration.ShoutMessageOffline;
        return template
            .Replace("{name}", displayName)
            .Replace("{url}", twitchUrl);
    }

    public async Task RefreshStreamsAsync()
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        lastRefresh = DateTime.UtcNow;

        try
        {
            var newStreamers = await TwitchService.GetStreamsAsync(
                Configuration.TwitchClientId,
                Configuration.TwitchClientSecret,
                Configuration.DjList);

            // Check for new live streams and send notifications
            if (Configuration.NotificationsEnabled)
            {
                foreach (var streamer in newStreamers)
                {
                    if (streamer.IsLive && !Configuration.NotifiedStreamers.Contains(streamer.TwitchUsername))
                    {
                        Configuration.NotifiedStreamers.Add(streamer.TwitchUsername);
                        SendLiveNotification(streamer);
                    }
                    else if (!streamer.IsLive)
                    {
                        // Remove from notified list if they went offline
                        Configuration.NotifiedStreamers.Remove(streamer.TwitchUsername);
                    }
                }
            }

            DjStreamers = newStreamers;
        }
        catch (Exception ex)
        {
            Log.Error($"Error refreshing streams: {ex.Message}");
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void SendLiveNotification(DjStreamer streamer)
    {
        var title = $"{streamer.DisplayName} is now LIVE!";
        var message = !string.IsNullOrEmpty(streamer.GameName)
            ? $"{streamer.StreamTitle}\nPlaying: {streamer.GameName}"
            : streamer.StreamTitle;

        // Send notification via Dalamud's notification system
        NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
        {
            Title = title,
            Content = message,
            Type = Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            Minimized = false,
        });

        // Also send to chat
        ChatGui.Print($"[DJ Tracker] {streamer.DisplayName} is now LIVE! - {streamer.StreamTitle}");

        Log.Information($"Notification sent: {streamer.DisplayName} is live");
    }

    public static void OpenTwitchStream(string username)
    {
        var url = $"https://twitch.tv/{username}";
        OpenUrl(url);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open URL: {ex.Message}");
        }
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnConfigCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    private void OnScheduleCommand(string command, string args)
    {
        SchedulingWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleSchedulingUi() => SchedulingWindow.Toggle();

    public void OpenTwitchPlayer(string username) => twitchPlayer.OpenStream(username);
}
