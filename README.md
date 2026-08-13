# OpenTelemetry

Adds OpenTelemetry-compatible metrics and distributed tracing to Shuttle.Hopper by observing the events already exposed on `HopperOptions` and `PipelineOptions` — no changes are required to your handlers or `ITransport` implementations.

## Installation

```bash
dotnet add package Shuttle.Hopper.OpenTelemetry
```

## Registration

```csharp
services.AddHopper()
    .AddOpenTelemetry();
```

This wires two things onto the shared `HopperOptions` / `PipelineOptions`:

- **Metrics** – every `ITransport`-facing event on `HopperOptions` (message sent / received / acknowledged / released, not-handled, handler and deserialization exceptions, transport created / disposed, transport operations, deferred-message events) is recorded against a `Meter` named `Shuttle.Hopper`.
- **Tracing** – every pipeline execution (inbox, outbox, send, dispatch, deferred, startup, shutdown) is wrapped in an `Activity` from an `ActivitySource` named `Shuttle.Hopper`, and each inbound message gets its own child span covering its handler invocation.

The instrumentation is built on `System.Diagnostics.ActivitySource` / `System.Diagnostics.Metrics.Meter` directly, so this package has no dependency on the `OpenTelemetry` SDK — it only becomes "live" once something subscribes to those names, for example:

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder.AddSource("Shuttle.Hopper"))
    .WithMetrics(builder => builder.AddMeter("Shuttle.Hopper"));
```

## Metrics

| Name | Instrument | Unit | Description |
| --- | --- | --- | --- |
| `hopper.messages.sent` | Counter | `{message}` | Number of messages sent to a transport. |
| `hopper.messages.received` | Counter | `{message}` | Number of messages received from a transport. |
| `hopper.messages.acknowledged` | Counter | `{message}` | Number of messages acknowledged on a transport. |
| `hopper.messages.released` | Counter | `{message}` | Number of messages released back to a transport. |
| `hopper.messages.not_handled` | Counter | `{message}` | Number of messages for which no handler was registered. |
| `hopper.handler.exceptions` | Counter | `{exception}` | Number of exceptions raised by message handlers. |
| `hopper.deserialization.exceptions` | Counter | `{exception}` | Number of message or transport-message deserialization exceptions (tagged `hopper.stage`). |
| `hopper.transports.active` | UpDownCounter | `{transport}` | Number of transports currently created and not yet disposed. |
| `hopper.transport.operation.duration` | Histogram | `ms` | Duration of a create / delete / purge / has-pending transport operation. |
| `hopper.deferred_messages.returned` | Counter | `{message}` | Number of deferred messages returned to the inbox work queue. |
| `hopper.deferred_message_processing.adjusted` | Counter | `{event}` | Number of times deferred message processing was rescheduled. |
| `hopper.deferred_message_processing.halted` | Counter | `{event}` | Number of times deferred message processing was halted. |
| `hopper.transport_messages.deferred` | Counter | `{message}` | Number of transport messages deferred. |

Transport-scoped counters carry `hopper.transport.scheme` and `hopper.transport.name` tags.

## Tracing

- Every pipeline execution (`InboxMessagePipeline`, `OutboxPipeline`, `TransportMessagePipeline`, `DispatchTransportMessagePipeline`, `DeferredMessagePipeline`, `StartupPipeline`, `ShutdownPipeline`) produces an `Activity` named after the pipeline type, parented to whatever `Activity.Current` is ambient at the time — a caller's own span, an incoming ASP.NET Core request span, or none.
- **Send** – once the outgoing `TransportMessage` has been assembled, the current trace context (`traceparent` / `tracestate`) and any `Activity.Current` baggage are written to `TransportMessage.Headers`, using the same header names the W3C Trace Context and Baggage specifications use for HTTP. The same context therefore propagates correctly whether the next hop is HTTP or a queue.
- **Receive** – once the message has been deserialized, those headers are extracted and used as the parent for a new child `Activity` named after the message type, covering exactly the handler invocation. It is tagged with `hopper.message.id`, `hopper.message.type` and, where present, `hopper.message.correlation_id`.
- A failed pipeline marks whichever `Activity` is open (the message-level one if handling had started, otherwise the pipeline-level one) with `ActivityStatusCode.Error` and records the exception via `Activity.AddException`.

## Extension points

- `HopperTelemetry.ActivitySource` / `HopperTelemetry.Meter` – the shared instances used throughout; exposed so you can add your own spans or measurements under the same names.
- `TraceContext.Inject` / `TraceContext.ExtractContext` / `TraceContext.ExtractBaggage` – the W3C header propagation helpers, exposed for use outside the standard pipeline hooks (for example, in a custom `ITransport` or an HTTP integration layer).
