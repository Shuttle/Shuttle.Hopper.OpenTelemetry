using System.Diagnostics;
using Shuttle.Contract;
using Shuttle.Hopper;
using Shuttle.Pipelines;

namespace Shuttle.Hopper.OpenTelemetry;

/// <summary>
/// Added to every pipeline instance as it starts (see `PipelineOptionsTelemetryExtensions.AddOpenTelemetryTracing()`).
/// Reacts only to the event checkpoints it knows about, so it is harmless on pipelines
/// (startup, shutdown, deferred-message, ...) that never raise them.
/// </summary>
public class HopperPipelineTelemetryObserver :
    IPipelineObserver<MessageAssembled>,
    IPipelineObserver<MessageDeserialized>,
    IPipelineObserver<MessageHandled>,
    IPipelineObserver<PipelineFailed>
{
    internal const string MessageActivityStateKey = "Shuttle.Hopper.OpenTelemetry.MessageActivity";

    // Send side: the outgoing TransportMessage now exists (with its Headers list), so this is the
    // last point before it's handed to SerializeTransportMessage/DispatchTransportMessage where we
    // can still stamp it with the trace context that is current *right now* - whether that's an
    // ambient Activity from an ASP.NET Core request, a caller's own span, or nothing at all.
    public Task ExecuteAsync(IPipelineContext<MessageAssembled> pipelineContext, CancellationToken cancellationToken = default)
    {
        var transportMessage = Guard.AgainstNull(pipelineContext).Pipeline.State.GetTransportMessage();

        if (transportMessage != null)
        {
            TraceContext.Inject(transportMessage.Headers, Activity.Current);
        }

        return Task.CompletedTask;
    }

    // Receive side: the TransportMessage and the deserialized message are both available. Extract
    // whatever trace context was stamped on the way in and start a child span for handling this message.
    public Task ExecuteAsync(IPipelineContext<MessageDeserialized> pipelineContext, CancellationToken cancellationToken = default)
    {
        var state = Guard.AgainstNull(pipelineContext).Pipeline.State;
        var transportMessage = state.GetTransportMessage();

        if (transportMessage == null)
        {
            return Task.CompletedTask;
        }

        var parentContext = TraceContext.ExtractContext(transportMessage.Headers);
        var activityName = string.IsNullOrEmpty(transportMessage.MessageType) ? "HandleMessage" : transportMessage.MessageType;
        var activity = HopperTelemetry.ActivitySource.StartActivity(activityName, ActivityKind.Consumer, parentContext);

        if (activity == null)
        {
            return Task.CompletedTask;
        }

        TraceContext.ExtractBaggage(transportMessage.Headers, activity);

        if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
        {
            activity.SetTag("hopper.message.correlation_id", transportMessage.CorrelationId);
        }

        activity.SetTag("hopper.message.id", transportMessage.MessageId.ToString());
        activity.SetTag("hopper.message.type", transportMessage.MessageType);

        state.Replace(MessageActivityStateKey, activity);

        return Task.CompletedTask;
    }

    public Task ExecuteAsync(IPipelineContext<MessageHandled> pipelineContext, CancellationToken cancellationToken = default)
    {
        EndMessageActivity(Guard.AgainstNull(pipelineContext).Pipeline);

        return Task.CompletedTask;
    }

    public Task ExecuteAsync(IPipelineContext<PipelineFailed> pipelineContext, CancellationToken cancellationToken = default)
    {
        var pipeline = Guard.AgainstNull(pipelineContext).Pipeline;

        EndMessageActivity(pipeline, pipeline.Exception);

        return Task.CompletedTask;
    }

    private static void EndMessageActivity(IPipeline pipeline, Exception? exception = null)
    {
        var state = pipeline.State;
        var activity = state.Get<Activity>(MessageActivityStateKey);

        if (activity == null)
        {
            return;
        }

        state.Remove(MessageActivityStateKey);

        if (exception != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }

        activity.Dispose();
    }
}
