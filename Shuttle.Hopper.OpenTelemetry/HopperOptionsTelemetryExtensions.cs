using System.Collections.Concurrent;
using System.Diagnostics;
using Shuttle.Contract;
using Shuttle.Hopper;

namespace Shuttle.Hopper.OpenTelemetry;

public static class HopperOptionsTelemetryExtensions
{
    extension(HopperOptions hopperOptions)
    {
        /// <summary>
        /// Subscribes to every `ITransport`-facing event on `HopperOptions` and records it against
        /// the `Shuttle.Hopper` meter. Call once per `HopperOptions` instance (see `HopperBuilder.AddOpenTelemetry()`).
        /// </summary>
        public void AddOpenTelemetryMetrics()
        {
            Guard.AgainstNull(hopperOptions);

            var meter = HopperTelemetry.Meter;

            var messagesSent = meter.CreateCounter<long>("hopper.messages.sent", "{message}", "Number of messages sent to a transport.");
            var messagesReceived = meter.CreateCounter<long>("hopper.messages.received", "{message}", "Number of messages received from a transport.");
            var messagesAcknowledged = meter.CreateCounter<long>("hopper.messages.acknowledged", "{message}", "Number of messages acknowledged on a transport.");
            var messagesReleased = meter.CreateCounter<long>("hopper.messages.released", "{message}", "Number of messages released back to a transport.");
            var messagesNotHandled = meter.CreateCounter<long>("hopper.messages.not_handled", "{message}", "Number of messages for which no handler was registered.");
            var handlerExceptions = meter.CreateCounter<long>("hopper.handler.exceptions", "{exception}", "Number of exceptions raised by message handlers.");
            var deserializationExceptions = meter.CreateCounter<long>("hopper.deserialization.exceptions", "{exception}", "Number of message or transport-message deserialization exceptions.");
            var transportsActive = meter.CreateUpDownCounter<long>("hopper.transports.active", "{transport}", "Number of transports currently created and not yet disposed.");
            var transportOperationDuration = meter.CreateHistogram<double>("hopper.transport.operation.duration", "ms", "Duration of a create/delete/purge/has-pending transport operation.");
            var deferredMessagesReturned = meter.CreateCounter<long>("hopper.deferred_messages.returned", "{message}", "Number of deferred messages returned to the inbox work queue.");
            var deferredProcessingAdjusted = meter.CreateCounter<long>("hopper.deferred_message_processing.adjusted", "{event}", "Number of times deferred message processing was rescheduled.");
            var deferredProcessingHalted = meter.CreateCounter<long>("hopper.deferred_message_processing.halted", "{event}", "Number of times deferred message processing was halted.");
            var transportMessagesDeferred = meter.CreateCounter<long>("hopper.transport_messages.deferred", "{message}", "Number of transport messages deferred.");

            var operationStartTimestamps = new ConcurrentDictionary<(ITransport Transport, string Operation), long>();

            hopperOptions.MessageSent += (eventArgs, _) =>
            {
                messagesSent.Add(1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.MessageReceived += (eventArgs, _) =>
            {
                messagesReceived.Add(1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.MessageAcknowledged += (eventArgs, _) =>
            {
                messagesAcknowledged.Add(1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.MessageReleased += (eventArgs, _) =>
            {
                messagesReleased.Add(1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.MessageNotHandled += (eventArgs, _) =>
            {
                messagesNotHandled.Add(1, new KeyValuePair<string, object?>("hopper.message.type", eventArgs.TransportMessage.MessageType));

                return Task.CompletedTask;
            };

            hopperOptions.HandlerException += (eventArgs, _) =>
            {
                handlerExceptions.Add(1,
                    new KeyValuePair<string, object?>("hopper.message.type", eventArgs.TransportMessage.MessageType),
                    new KeyValuePair<string, object?>("error.type", eventArgs.Exception.GetType().FullName));

                return Task.CompletedTask;
            };

            hopperOptions.MessageDeserializationException += (_, _) =>
            {
                deserializationExceptions.Add(1, new KeyValuePair<string, object?>("hopper.stage", "message"));

                return Task.CompletedTask;
            };

            hopperOptions.TransportMessageDeserializationException += (_, _) =>
            {
                deserializationExceptions.Add(1, new KeyValuePair<string, object?>("hopper.stage", "transport-message"));

                return Task.CompletedTask;
            };

            hopperOptions.TransportCreated += (eventArgs, _) =>
            {
                transportsActive.Add(1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.TransportDisposed += (eventArgs, _) =>
            {
                transportsActive.Add(-1, TransportTags(eventArgs.Transport));

                return Task.CompletedTask;
            };

            hopperOptions.TransportOperation += (eventArgs, _) =>
            {
                var (operation, phase) = SplitOperation(eventArgs.Operation);
                var key = (eventArgs.Transport, operation);

                if (phase == "starting")
                {
                    operationStartTimestamps[key] = Stopwatch.GetTimestamp();

                    return Task.CompletedTask;
                }

                if (operationStartTimestamps.TryRemove(key, out var start))
                {
                    transportOperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        new KeyValuePair<string, object?>("hopper.transport.scheme", eventArgs.Transport.Uri.Uri.Scheme),
                        new KeyValuePair<string, object?>("hopper.transport.name", eventArgs.Transport.Uri.TransportName),
                        new KeyValuePair<string, object?>("hopper.transport.operation", operation));
                }

                return Task.CompletedTask;
            };

            hopperOptions.DeferredMessageReturned += (_, _) =>
            {
                deferredMessagesReturned.Add(1);

                return Task.CompletedTask;
            };

            hopperOptions.DeferredMessageProcessingAdjusted += (_, _) =>
            {
                deferredProcessingAdjusted.Add(1);

                return Task.CompletedTask;
            };

            hopperOptions.DeferredMessageProcessingHalted += (_, _) =>
            {
                deferredProcessingHalted.Add(1);

                return Task.CompletedTask;
            };

            hopperOptions.TransportMessageDeferred += (_, _) =>
            {
                transportMessagesDeferred.Add(1);

                return Task.CompletedTask;
            };
        }
    }

    // Transport operations are reported as e.g. "[create/starting]" / "[create/completed]", or,
    // for operations without an explicit completion suffix, "[has-pending/starting]" / "[has-pending]".
    // The base operation name is always the first segment, so pairing on it is stable either way.
    private static (string Operation, string Phase) SplitOperation(string operation)
    {
        var parts = operation.Trim('[', ']').Split('/');

        return parts.Length > 1 ? (parts[0], parts[1]) : (parts[0], "completed");
    }

    private static KeyValuePair<string, object?>[] TransportTags(ITransport transport)
    {
        return
        [
            new("hopper.transport.scheme", transport.Uri.Uri.Scheme),
            new("hopper.transport.name", transport.Uri.TransportName)
        ];
    }
}
