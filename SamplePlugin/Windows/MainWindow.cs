using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.Automation;
using Nala.Models;

namespace Nala.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Nala - DJ Tracker##NalaMain", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Header with refresh button
        DrawHeader();

        ImGui.Separator();

        // DJ List
        using (var child = ImRaii.Child("DJList", Vector2.Zero, true))
        {
            if (child.Success)
            {
                DrawDjList();
            }
        }
    }

    private void DrawHeader()
    {
        var liveCount = 0;
        foreach (var dj in plugin.DjStreamers)
        {
            if (dj.IsLive)
            {
                liveCount++;
            }
        }

        ImGui.Text($"Tracking {plugin.Configuration.DjList.Count} DJs");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), $"({liveCount} LIVE)");

        // Scheduler status indicator
        if (plugin.Configuration.SchedulerEnabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "[SCHEDULER ON]");
        }

        ImGui.SameLine(ImGui.GetWindowWidth() - 230);

        if (ImGui.Button("Refresh"))
        {
            _ = plugin.RefreshStreamsAsync();
        }

        ImGui.SameLine();

        if (ImGui.Button("Schedule"))
        {
            plugin.ToggleSchedulingUi();
        }

        ImGui.SameLine();

        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
    }

    private void DrawDjList()
    {
        if (plugin.Configuration.DjList.Count == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "No DJs configured!");
            ImGui.Text("Click 'Settings' to add Twitch streamers to track.");
            return;
        }

        if (string.IsNullOrEmpty(plugin.Configuration.TwitchClientId) ||
            string.IsNullOrEmpty(plugin.Configuration.TwitchClientSecret))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), "Twitch API not configured!");
            ImGui.Text("Click 'Settings' to add your Twitch Client ID and Secret.");
            return;
        }

        // Sort: live streamers first
        var sortedStreamers = new System.Collections.Generic.List<DjStreamer>(plugin.DjStreamers);
        sortedStreamers.Sort((a, b) =>
        {
            if (a.IsLive && !b.IsLive) return -1;
            if (!a.IsLive && b.IsLive) return 1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var dj in sortedStreamers)
        {
            DrawDjEntry(dj);
            ImGui.Separator();
        }
    }

    private void DrawDjEntry(DjStreamer dj)
    {
        using var id = ImRaii.PushId(dj.TwitchUsername);

        // Status indicator
        if (dj.IsLive)
        {
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "[LIVE]");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "[OFFLINE]");
        }

        ImGui.SameLine();

        // Display name
        if (dj.IsLive)
        {
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), dj.DisplayName);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), dj.DisplayName);
        }

        ImGui.SameLine(ImGui.GetWindowWidth() - 160);

        if (ImGui.Button("Watch"))
        {
            Plugin.OpenTwitchStream(dj.TwitchUsername);
        }

        ImGui.SameLine();

        if (ImGui.Button("Shout"))
        {
            var twitchUrl = $"https://twitch.tv/{dj.TwitchUsername}";
            var message = plugin.FormatShoutMessage(dj.DisplayName, twitchUrl, dj.IsLive);
            Chat.ExecuteCommand($"/yell {message}");
        }

        if (dj.IsLive)
        {
            // Stream title
            if (!string.IsNullOrEmpty(dj.StreamTitle))
            {
                using (ImRaii.PushIndent(30f))
                {
                    ImGui.TextWrapped(dj.StreamTitle);
                }
            }

            // Game and viewers
            using (ImRaii.PushIndent(30f))
            {
                if (!string.IsNullOrEmpty(dj.GameName))
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 1.0f, 1.0f), $"Playing: {dj.GameName}");
                }

                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), $"{dj.ViewerCount:N0} viewers");

                // Stream duration
                if (dj.StartedAt.HasValue)
                {
                    var duration = DateTime.UtcNow - dj.StartedAt.Value;
                    var durationStr = duration.TotalHours >= 1
                        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                        : $"{duration.Minutes}m";
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), $"({durationStr})");
                }
            }
        }
    }
}
