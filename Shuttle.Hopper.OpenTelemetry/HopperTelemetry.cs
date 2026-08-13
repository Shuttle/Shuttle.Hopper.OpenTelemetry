using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shuttle.Hopper.OpenTelemetry;

public static class HopperTelemetry
{
    public const string ActivitySourceName = "Shuttle.Hopper";
    public const string MeterName = "Shuttle.Hopper";

    private static readonly string? Version = typeof(HopperTelemetry).Assembly.GetName().Version?.ToString();

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);
    public static readonly Meter Meter = new(MeterName, Version);
}
