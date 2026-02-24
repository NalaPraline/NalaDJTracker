using System;

namespace Nala.Models;

[Serializable]
public class ScheduledShout
{
    // Twitch username of the DJ
    public string TwitchUsername { get; set; } = string.Empty;

    // Display name for the UI
    public string DisplayName { get; set; } = string.Empty;

    // How long this DJ stays in the active rotation (in minutes)
    public int DurationMinutes { get; set; } = 60;

    // How often to yell about this DJ (in minutes)
    public int YellIntervalMinutes { get; set; } = 5;

    // When this DJ's slot started (runtime only, not serialized)
    [NonSerialized]
    public DateTime? SlotStartTime;

    // Last time we yelled about this DJ (runtime only)
    [NonSerialized]
    public DateTime? LastYellTime;
}
