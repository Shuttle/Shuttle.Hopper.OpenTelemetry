using System.Diagnostics;
using Shuttle.Contract;
using Shuttle.Pipelines;

namespace Shuttle.Hopper.OpenTelemetry;

public static class PipelineOptionsTelemetryExtensions
{
    private const string PipelineActivityStateKey = "Shuttle.Hopper.OpenTelemetry.PipelineActivity";

    // Pipelines are DI-scoped once and then `ExecuteAsync()`-ed repeatedly for the lifetime of their
    // processor (see InboxProcessor), so `PipelineStarting` fires once per message, not once per
    // pipeline instance. The observer is stateless, so a single shared instance is added to every
    // pipeline the first time it starts - `HasOpenTelemetryObserverStateKey` makes that idempotent.
    private const string ObserverAddedStateKey = "Shuttle.Hopper.OpenTelemetry.ObserverAdded";
    private static readonly HopperPipelineTelemetryObserver Observer = new();

    extension(PipelineOptions pipelineOptions)
    {
        /// <summary>
        /// Wraps every pipeline execution (inbox, outbox, send, dispatch, deferred, startup, shutdown, ...)
        /// in an Activity named after the pipeline type, and attaches the message-level tracing observer
        /// so it can start/inject the finer-grained per-message span. Call once (see `HopperBuilder.AddOpenTelemetry()`).
        /// </summary>
        public void AddOpenTelemetryTracing()
        {
            Guard.AgainstNull(pipelineOptions);

            pipelineOptions.PipelineStarting += (eventArgs, _) =>
            {
                var pipeline = eventArgs.Pipeline;

                if (!pipeline.State.Contains(ObserverAddedStateKey))
                {
                    pipeline.AddObserver(Observer);
                    pipeline.State.Add(ObserverAddedStateKey, true);
                }

                var activity = HopperTelemetry.ActivitySource.StartActivity(pipeline.GetType().Name);

                if (activity != null)
                {
                    pipeline.State.Replace(PipelineActivityStateKey, activity);
                }

                return Task.CompletedTask;
            };

            pipelineOptions.PipelineCompleted += (eventArgs, _) =>
            {
                EndPipelineActivity(eventArgs.Pipeline);

                return Task.CompletedTask;
            };

            pipelineOptions.PipelineAborted += (eventArgs, _) =>
            {
                EndPipelineActivity(eventArgs.Pipeline);

                return Task.CompletedTask;
            };

            pipelineOptions.PipelineFailed += (eventArgs, _) =>
            {
                EndPipelineActivity(eventArgs.Pipeline, eventArgs.Pipeline.Exception);

                return Task.CompletedTask;
            };
        }
    }

    private static void EndPipelineActivity(IPipeline pipeline, Exception? exception = null)
    {
        var state = pipeline.State;
        var activity = state.Get<Activity>(PipelineActivityStateKey);

        if (activity == null)
        {
            return;
        }

        state.Remove(PipelineActivityStateKey);

        if (exception != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }

        activity.Dispose();
    }
}
