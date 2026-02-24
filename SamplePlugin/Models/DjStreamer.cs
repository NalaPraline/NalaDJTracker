using System;

namespace Nala.Models;

public class DjStreamer
{
    public string TwitchUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsLive { get; set; }
    public string StreamTitle { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
}
