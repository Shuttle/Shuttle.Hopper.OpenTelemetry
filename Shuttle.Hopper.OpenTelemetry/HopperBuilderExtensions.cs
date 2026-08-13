using Microsoft.Extensions.DependencyInjection;
using Shuttle.Contract;
using Shuttle.Hopper;
using Shuttle.Pipelines;

namespace Shuttle.Hopper.OpenTelemetry;

public static class HopperBuilderExtensions
{
    extension(HopperBuilder hopperBuilder)
    {
        public HopperBuilder AddOpenTelemetry()
        {
            var services = Guard.AgainstNull(hopperBuilder).Services;

            services.AddOptions<HopperOptions>().Configure(options => options.AddOpenTelemetryMetrics());
            services.AddOptions<PipelineOptions>().Configure(options => options.AddOpenTelemetryTracing());

            return hopperBuilder;
        }
    }
}
