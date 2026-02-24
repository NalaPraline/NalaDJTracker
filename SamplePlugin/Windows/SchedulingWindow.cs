using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Nala.Models;

namespace Nala.Windows;

public class SchedulingWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private int? itemToRemove;
    private int? itemToMoveUp;
    private int? itemToMoveDown;

    public SchedulingWindow(Plugin plugin) : base("Nala - Shout Scheduler###NalaScheduler")
    {
        Flags = ImGuiWindowFlags.None;

        Size = new Vector2(550, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Process deferred operations
        ProcessDeferredOperations();

        // Header with scheduler controls
        DrawSchedulerControls();

        ImGui.Separator();

        // Two columns: Available DJs | Scheduled Queue
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columnWidth = (availableWidth - 20) / 2;

        using (var table = ImRaii.Table("SchedulerLayout", 2, ImGuiTableFlags.None))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Available", ImGuiTableColumnFlags.WidthFixed, columnWidth);
                ImGui.TableSetupColumn("Queue", ImGuiTableColumnFlags.WidthFixed, columnWidth);

                ImGui.TableNextRow();

                // Left column: Available DJs
                ImGui.TableNextColumn();
                DrawAvailableDjs(columnWidth);

                // Right column: Scheduled Queue
                ImGui.TableNextColumn();
                DrawScheduleQueue(columnWidth);
            }
        }

        ImGui.Separator();

        // Settings
        DrawSettings();
    }

    private void ProcessDeferredOperations()
    {
        if (itemToRemove.HasValue && itemToRemove.Value < configuration.ShoutSchedule.Count)
        {
            configuration.ShoutSchedule.RemoveAt(itemToRemove.Value);
            configuration.Save();
            itemToRemove = null;
        }

        if (itemToMoveUp.HasValue && itemToMoveUp.Value > 0 && itemToMoveUp.Value < configuration.ShoutSchedule.Count)
        {
            var item = configuration.ShoutSchedule[itemToMoveUp.Value];
            configuration.ShoutSchedule.RemoveAt(itemToMoveUp.Value);
            configuration.ShoutSchedule.Insert(itemToMoveUp.Value - 1, item);
            configuration.Save();
            itemToMoveUp = null;
        }

        if (itemToMoveDown.HasValue && itemToMoveDown.Value >= 0 && itemToMoveDown.Value < configuration.ShoutSchedule.Count - 1)
        {
            var item = configuration.ShoutSchedule[itemToMoveDown.Value];
            configuration.ShoutSchedule.RemoveAt(itemToMoveDown.Value);
            configuration.ShoutSchedule.Insert(itemToMoveDown.Value + 1, item);
            configuration.Save();
            itemToMoveDown = null;
        }
    }

    private void DrawSchedulerControls()
    {
        // Current status
        if (configuration.SchedulerEnabled)
        {
            // Check if we're in transition
            if (configuration.TransitionStartTime.HasValue)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "TRANSITION");
                var transitionElapsed = DateTime.UtcNow - configuration.TransitionStartTime.Value;
                var transitionRemaining = configuration.TransitionDelaySeconds - (int)transitionElapsed.TotalSeconds;
                if (transitionRemaining > 0)
                {
                    ImGui.SameLine();
                    ImGui.Text($"- Next DJ in {transitionRemaining}s");
                }

                if (configuration.ShoutSchedule.Count > 0)
                {
                    var next = configuration.ShoutSchedule[0];
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 1.0f, 1.0f), $"Up next: {next.DisplayName}");
                }
            }
            else if (configuration.ShoutSchedule.Count > 0)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "SCHEDULER ACTIVE");

                var current = configuration.ShoutSchedule[0];
                ImGui.SameLine();
                ImGui.Text($"- Now promoting: {current.DisplayName}");

                // Show time remaining
                if (current.SlotStartTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - current.SlotStartTime.Value;
                    var remaining = TimeSpan.FromMinutes(current.DurationMinutes) - elapsed;
                    if (remaining.TotalSeconds > 0)
                    {
                        var remainingStr = remaining.TotalHours >= 1
                            ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                            : $"{remaining.Minutes}m {remaining.Seconds}s";
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), $"({remainingStr} left)");
                    }
                }

                // Show next yell time
                if (current.LastYellTime.HasValue)
                {
                    var nextYell = current.LastYellTime.Value.AddMinutes(current.YellIntervalMinutes) - DateTime.UtcNow;
                    if (nextYell.TotalSeconds > 0)
                    {
                        ImGui.Text($"Next shout in: {(int)nextYell.TotalMinutes}m {nextYell.Seconds}s");
                    }
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "SCHEDULER ACTIVE");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "SCHEDULER STOPPED");
        }

        ImGui.Spacing();

        // Start/Stop button
        if (configuration.SchedulerEnabled)
        {
            if (ImGui.Button("Stop Scheduler", new Vector2(150, 0)))
            {
                configuration.SchedulerEnabled = false;
                configuration.Save();
            }
        }
        else
        {
            var canStart = configuration.ShoutSchedule.Count > 0;
            using (ImRaii.Disabled(!canStart))
            {
                if (ImGui.Button("Start Scheduler", new Vector2(150, 0)))
                {
                    configuration.SchedulerEnabled = true;
                    // Initialize the first slot
                    if (configuration.ShoutSchedule.Count > 0)
                    {
                        configuration.ShoutSchedule[0].SlotStartTime = DateTime.UtcNow;
                        configuration.ShoutSchedule[0].LastYellTime = null;
                    }
                    configuration.Save();
                }
            }
            if (!canStart)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "Add DJs to the queue first!");
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear Queue"))
        {
            configuration.ShoutSchedule.Clear();
            configuration.SchedulerEnabled = false;
            configuration.Save();
        }
    }

    private void DrawAvailableDjs(float width)
    {
        ImGui.Text("Available DJs");
        ImGui.Spacing();

        using var child = ImRaii.Child("AvailableDJsChild", new Vector2(width - 10, 250), true);
        if (!child.Success) return;

        if (plugin.DjStreamers.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No DJs tracked yet.");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Add some in the main window!");
            return;
        }

        foreach (var dj in plugin.DjStreamers)
        {
            using var id = ImRaii.PushId(dj.TwitchUsername);

            // Check if already in queue
            var alreadyScheduled = configuration.ShoutSchedule.Any(s =>
                s.TwitchUsername.Equals(dj.TwitchUsername, StringComparison.OrdinalIgnoreCase));

            // Status indicator
            if (dj.IsLive)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "[LIVE]");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "[OFF]");
            }
            ImGui.SameLine();

            ImGui.Text(dj.DisplayName);
            ImGui.SameLine(width - 80);

            using (ImRaii.Disabled(alreadyScheduled))
            {
                if (ImGui.Button("Add >>"))
                {
                    configuration.ShoutSchedule.Add(new ScheduledShout
                    {
                        TwitchUsername = dj.TwitchUsername,
                        DisplayName = dj.DisplayName,
                        DurationMinutes = configuration.DefaultShoutDurationMinutes,
                        YellIntervalMinutes = configuration.DefaultYellIntervalMinutes
                    });
                    configuration.Save();
                }
            }
        }
    }

    private void DrawScheduleQueue(float width)
    {
        ImGui.Text($"Schedule Queue ({configuration.ShoutSchedule.Count})");
        ImGui.Spacing();

        using var child = ImRaii.Child("ScheduleQueueChild", new Vector2(width - 10, 250), true);
        if (!child.Success) return;

        if (configuration.ShoutSchedule.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Queue is empty.");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Add DJs from the left!");
            return;
        }

        for (var i = 0; i < configuration.ShoutSchedule.Count; i++)
        {
            var shout = configuration.ShoutSchedule[i];
            using var id = ImRaii.PushId(i);

            // Highlight the active DJ
            if (i == 0 && configuration.SchedulerEnabled)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), $">> {shout.DisplayName}");
            }
            else
            {
                ImGui.Text($"{i + 1}. {shout.DisplayName}");
            }

            // Duration and interval info
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"({shout.DurationMinutes}m / every {shout.YellIntervalMinutes}m)");

            // Control buttons
            ImGui.SameLine(width - 100);

            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.Button("^"))
                {
                    itemToMoveUp = i;
                }
            }

            ImGui.SameLine();

            using (ImRaii.Disabled(i == configuration.ShoutSchedule.Count - 1))
            {
                if (ImGui.Button("v"))
                {
                    itemToMoveDown = i;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("X"))
            {
                itemToRemove = i;
            }

            // Editable settings for this DJ
            if (ImGui.TreeNode($"Settings##{i}"))
            {
                var duration = shout.DurationMinutes;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Duration (min)", ref duration))
                {
                    shout.DurationMinutes = Math.Max(1, duration);
                    configuration.Save();
                }

                var interval = shout.YellIntervalMinutes;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Yell Interval (min)", ref interval))
                {
                    shout.YellIntervalMinutes = Math.Max(1, interval);
                    configuration.Save();
                }

                ImGui.TreePop();
            }
        }
    }

    private void DrawSettings()
    {
        if (ImGui.CollapsingHeader("Default Settings"))
        {
            var defaultDuration = configuration.DefaultShoutDurationMinutes;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Default Duration (min)", ref defaultDuration))
            {
                configuration.DefaultShoutDurationMinutes = Math.Max(1, defaultDuration);
                configuration.Save();
            }

            var defaultInterval = configuration.DefaultYellIntervalMinutes;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Default Yell Interval (min)", ref defaultInterval))
            {
                configuration.DefaultYellIntervalMinutes = Math.Max(1, defaultInterval);
                configuration.Save();
            }

            var transitionDelay = configuration.TransitionDelaySeconds;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Transition Delay (sec)", ref transitionDelay))
            {
                configuration.TransitionDelaySeconds = Math.Max(0, transitionDelay);
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delay in seconds between DJs (0 = no delay)");
            }

            ImGui.Spacing();

            var onlyLive = configuration.OnlyShoutLiveDjs;
            if (ImGui.Checkbox("Only shout about LIVE DJs", ref onlyLive))
            {
                configuration.OnlyShoutLiveDjs = onlyLive;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("If enabled, the scheduler will skip DJs who are offline");
            }
        }

        if (ImGui.CollapsingHeader("Shout Messages"))
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Use {name} for DJ name and {url} for Twitch link");
            ImGui.Spacing();

            ImGui.Text("Message when DJ is LIVE:");
            var msgLive = configuration.ShoutMessageLive;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##MsgLive", ref msgLive, 500))
            {
                configuration.ShoutMessageLive = msgLive;
                configuration.Save();
            }

            ImGui.Spacing();

            ImGui.Text("Message when DJ is OFFLINE:");
            var msgOffline = configuration.ShoutMessageOffline;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##MsgOffline", ref msgOffline, 500))
            {
                configuration.ShoutMessageOffline = msgOffline;
                configuration.Save();
            }

            ImGui.Spacing();

            if (ImGui.Button("Reset to Default"))
            {
                configuration.ShoutMessageLive = "Come check out {name} who is LIVE right now! {url}";
                configuration.ShoutMessageOffline = "Go follow {name} on Twitch! {url}";
                configuration.Save();
            }

            // Preview
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Preview:");
            var previewLive = configuration.ShoutMessageLive.Replace("{name}", "ExampleDJ").Replace("{url}", "https://twitch.tv/exampledj");
            var previewOffline = configuration.ShoutMessageOffline.Replace("{name}", "ExampleDJ").Replace("{url}", "https://twitch.tv/exampledj");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), $"LIVE: {previewLive}");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"OFFLINE: {previewOffline}");
        }
    }
}
