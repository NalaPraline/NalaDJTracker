using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Nala.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private string newDjInput = string.Empty;
    private string clientIdInput = string.Empty;
    private string clientSecretInput = string.Empty;
    private int? djToRemove;

    public ConfigWindow(Plugin plugin) : base("DJ Tracker Configuration###DJTrackerConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(500, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = plugin.Configuration;

        // Initialize inputs from config
        clientIdInput = configuration.TwitchClientId;
        clientSecretInput = configuration.TwitchClientSecret;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        // Process deferred removal outside the loop
        if (djToRemove.HasValue)
        {
            configuration.DjList.RemoveAt(djToRemove.Value);
            configuration.Save();
            _ = plugin.RefreshStreamsAsync();
            djToRemove = null;
        }

        using var tabBar = ImRaii.TabBar("ConfigTabs");
        if (!tabBar.Success)
        {
            return;
        }

        using (var djTab = ImRaii.TabItem("DJ List"))
        {
            if (djTab.Success)
            {
                DrawDjListTab();
            }
        }

        using (var twitchTab = ImRaii.TabItem("Twitch API"))
        {
            if (twitchTab.Success)
            {
                DrawTwitchApiTab();
            }
        }

        using (var settingsTab = ImRaii.TabItem("Settings"))
        {
            if (settingsTab.Success)
            {
                DrawSettingsTab();
            }
        }
    }

    private void DrawDjListTab()
    {
        ImGui.Text("Add Twitch streamers to track:");
        ImGui.Spacing();

        // Add new DJ input
        ImGui.SetNextItemWidth(300);
        var enterPressed = ImGui.InputText("##NewDJ", ref newDjInput, 100, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();

        if ((ImGui.Button("Add DJ") || enterPressed) && !string.IsNullOrWhiteSpace(newDjInput))
        {
            var username = newDjInput.Trim().ToLower();

            // Remove @ if user added it
            if (username.StartsWith('@'))
            {
                username = username[1..];
            }

            if (!configuration.DjList.Contains(username))
            {
                configuration.DjList.Add(username);
                configuration.Save();
                _ = plugin.RefreshStreamsAsync();
            }

            newDjInput = string.Empty;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text($"Tracked DJs ({configuration.DjList.Count}):");
        ImGui.Spacing();

        using (var child = ImRaii.Child("DJListScroll", new Vector2(0, 300), true))
        {
            if (child.Success)
            {
                for (var i = 0; i < configuration.DjList.Count; i++)
                {
                    var dj = configuration.DjList[i];
                    using var id = ImRaii.PushId(i);

                    ImGui.Text(dj);
                    ImGui.SameLine(ImGui.GetWindowWidth() - 80);

                    if (ImGui.Button("Remove"))
                    {
                        djToRemove = i;
                    }
                }

                if (configuration.DjList.Count == 0)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No DJs added yet.");
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Enter a Twitch username above to get started!");
                }
            }
        }
    }

    private void DrawTwitchApiTab()
    {
        ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "Twitch API Configuration");
        ImGui.Spacing();

        // Client ID
        ImGui.Text("Client ID:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ClientId", ref clientIdInput, 100))
        {
            configuration.TwitchClientId = clientIdInput.Trim();
            configuration.Save();
            plugin.TwitchService.InvalidateToken();
        }

        ImGui.Spacing();

        // Client Secret
        ImGui.Text("Client Secret:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ClientSecret", ref clientSecretInput, 100, ImGuiInputTextFlags.Password))
        {
            configuration.TwitchClientSecret = clientSecretInput.Trim();
            configuration.Save();
            plugin.TwitchService.InvalidateToken();
        }

        ImGui.Spacing();

        // Test connection button
        if (ImGui.Button("Test Connection"))
        {
            _ = plugin.RefreshStreamsAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Setup guide
        if (ImGui.CollapsingHeader("Setup Guide", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSetupGuide();
        }
    }

    private void DrawSetupGuide()
    {
        ImGui.TextWrapped("To use this plugin, you need to create a Twitch application to get API credentials:");
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 1:");
        ImGui.SameLine();
        ImGui.Text("Go to the Twitch Developer Console");
        if (ImGui.Button("Open dev.twitch.tv"))
        {
            Plugin.OpenUrl("https://dev.twitch.tv/console");
        }

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 2:");
        ImGui.SameLine();
        ImGui.Text("Log in with your Twitch account");

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 3:");
        ImGui.SameLine();
        ImGui.Text("Click \"Register Your Application\"");

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 4:");
        ImGui.SameLine();
        ImGui.Text("Fill in the form:");

        using (ImRaii.PushIndent(20f))
        {
            ImGui.BulletText("Name: Nala FFXIV Plugin (or anything)");
            ImGui.BulletText("OAuth Redirect URL: http://localhost");
            ImGui.BulletText("Category: Application Integration");
        }

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 5:");
        ImGui.SameLine();
        ImGui.Text("Click \"Create\"");

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 6:");
        ImGui.SameLine();
        ImGui.Text("Copy the \"Client ID\" to the field above");

        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Step 7:");
        ImGui.SameLine();
        ImGui.Text("Click \"New Secret\" and copy it above");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Done! The plugin will now be able to check stream status.");
    }

    private void DrawSettingsTab()
    {
        ImGui.Text("General Settings");
        ImGui.Spacing();

        // Refresh interval
        ImGui.Text("Refresh Interval:");
        ImGui.SameLine();
        var refreshInterval = configuration.RefreshIntervalSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("##RefreshInterval", ref refreshInterval, 30, 300, "%d seconds"))
        {
            configuration.RefreshIntervalSeconds = refreshInterval;
            configuration.Save();
        }

        ImGui.Spacing();

        // Notifications toggle
        var notificationsEnabled = configuration.NotificationsEnabled;
        if (ImGui.Checkbox("Enable notifications when DJs go live", ref notificationsEnabled))
        {
            configuration.NotificationsEnabled = notificationsEnabled;
            configuration.Save();
        }

        ImGui.Spacing();

        // Movable window toggle
        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Allow moving config window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Commands:");
        using (ImRaii.PushIndent(20f))
        {
            ImGui.BulletText("/dj - Open DJ Tracker window");
            ImGui.BulletText("/djschedule - Open the Scheduler");
            ImGui.BulletText("/djconfig - Open this configuration window");
        }
    }
}
