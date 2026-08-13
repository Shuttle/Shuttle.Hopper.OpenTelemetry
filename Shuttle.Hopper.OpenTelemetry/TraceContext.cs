using System.Diagnostics;
using Shuttle.Contract;
using Shuttle.Hopper;

namespace Shuttle.Hopper.OpenTelemetry;

/// <summary>
/// Propagates W3C Trace Context (https://www.w3.org/TR/trace-context/) and Baggage
/// (https://www.w3.org/TR/baggage/) across a `TransportMessage` using the same header
/// names as the HTTP spec, so the same context works whether a hop is HTTP or a queue.
/// </summary>
public static class TraceContext
{
    public const string BaggageHeaderKey = "baggage";
    public const string TraceParentHeaderKey = "traceparent";
    public const string TraceStateHeaderKey = "tracestate";

    public static void Inject(List<TransportHeader> headers, Activity? activity)
    {
        Guard.AgainstNull(headers);

        if (activity?.Id == null)
        {
            return;
        }

        headers.SetHeaderValue(TraceParentHeaderKey, activity.Id);

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers.SetHeaderValue(TraceStateHeaderKey, activity.TraceStateString);
        }

        var baggage = activity.Baggage.ToList();

        if (baggage.Count > 0)
        {
            headers.SetHeaderValue(BaggageHeaderKey, string.Join(',', baggage.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}")));
        }
    }

    public static ActivityContext ExtractContext(List<TransportHeader> headers)
    {
        Guard.AgainstNull(headers);

        if (!headers.Contains(TraceParentHeaderKey))
        {
            return default;
        }

        var traceParent = headers.GetHeaderValue(TraceParentHeaderKey);
        var traceState = headers.Contains(TraceStateHeaderKey) ? headers.GetHeaderValue(TraceStateHeaderKey) : null;

        return ActivityContext.TryParse(traceParent, traceState, out var context) ? context : default;
    }

    public static void ExtractBaggage(List<TransportHeader> headers, Activity activity)
    {
        Guard.AgainstNull(headers);
        Guard.AgainstNull(activity);

        if (!headers.Contains(BaggageHeaderKey))
        {
            return;
        }

        foreach (var pair in headers.GetHeaderValue(BaggageHeaderKey).Split(','))
        {
            var index = pair.IndexOf('=');

            if (index <= 0)
            {
                continue;
            }

            activity.SetBaggage(Uri.UnescapeDataString(pair[..index]), Uri.UnescapeDataString(pair[(index + 1)..]));
        }
    }
}
