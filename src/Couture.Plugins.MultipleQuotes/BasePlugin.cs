using System;
using Microsoft.Xrm.Sdk;

namespace Couture.Plugins.MultipleQuotes
{
    /// Thin wrapper over IPlugin so the concrete plugins can focus on the
    /// business logic while still getting consistent tracing and a guarded
    /// entry point that wraps unexpected errors in InvalidPluginExecutionException
    /// (the only exception type the platform surfaces cleanly to the user).
    public abstract class BasePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            // Run as the calling user rather than SYSTEM so security roles
            // still apply when we cascade changes to opportunities/quotes.
            var service = factory.CreateOrganizationService(context.UserId);

            try
            {
                ExecuteInternal(new PluginContext(context, service, tracing));
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace("Unhandled exception: {0}", ex);
                throw new InvalidPluginExecutionException(
                    $"{GetType().Name} failed: {ex.Message}", ex);
            }
        }

        protected abstract void ExecuteInternal(PluginContext context);
    }

    public sealed class PluginContext
    {
        public PluginContext(IPluginExecutionContext execution, IOrganizationService service, ITracingService tracing)
        {
            Execution = execution;
            Service = service;
            Tracing = tracing;
        }

        public IPluginExecutionContext Execution { get; }
        public IOrganizationService Service { get; }
        public ITracingService Tracing { get; }
    }
}
