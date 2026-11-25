using TimeCheck.Domain.Common;

namespace TimeCheck.Domain.Entities;

public sealed class Church : BaseEntity
{
    private Church() { }
    public string Name { get; private set; } = "";
    public string Timezone { get; private set; } = "Asia/Manila";
    /// <summary>Raw jsonb settings (devotion windows, AI thresholds, retention...).</summary>
    public string Settings { get; private set; } = "{}";

    public void ReplaceSettings(string json) => Settings = json;

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(Timezone);
}
