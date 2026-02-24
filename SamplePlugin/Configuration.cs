using Dalamud.Configuration;
using Nala.Models;
using System;
using System.Collections.Generic;

namespace Nala;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    // Twitch API Settings
    public string TwitchClientId { get; set; } = string.Empty;
    public string TwitchClientSecret { get; set; } = string.Empty;

    // DJ List - Twitch usernames to track
    public List<string> DjList { get; set; } = new();

    // Polling interval in seconds (default: 60)
    public int RefreshIntervalSeconds { get; set; } = 60;

    // Enable/disable notifications when a DJ goes live
    public bool NotificationsEnabled { get; set; } = true;

    // Track which streamers we've already notified about (to avoid duplicates)
    // This is not serialized - resets on plugin load
    [NonSerialized]
    public HashSet<string> NotifiedStreamers = new();

    // === Scheduling Settings ===

    // The queue of scheduled shouts
    public List<ScheduledShout> ShoutSchedule { get; set; } = new();

    // Is the scheduler currently running?
    public bool SchedulerEnabled { get; set; }

    // Default duration for new scheduled shouts (in minutes)
    public int DefaultShoutDurationMinutes { get; set; } = 60;

    // Default yell interval for new scheduled shouts (in minutes)
    public int DefaultYellIntervalMinutes { get; set; } = 5;

    // Only yell about DJs who are currently live
    public bool OnlyShoutLiveDjs { get; set; } = true;

    // Delay in seconds between DJs (transition period)
    public int TransitionDelaySeconds { get; set; } = 30;

    // Custom shout messages - use {name} for DJ name and {url} for Twitch URL
    public string ShoutMessageLive { get; set; } = "Come check out {name} who is LIVE right now! {url}";
    public string ShoutMessageOffline { get; set; } = "Go follow {name} on Twitch! {url}";

    // Runtime: tracks when we started the transition (not serialized)
    [NonSerialized]
    public DateTime? TransitionStartTime;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
