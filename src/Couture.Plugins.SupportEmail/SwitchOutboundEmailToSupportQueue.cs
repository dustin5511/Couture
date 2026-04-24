using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.SupportEmail
{
    /// <summary>
    /// Replaces the From/Sender of outbound emails with the Support Intake Queue
    /// (support@tabperformance.com) so customer replies always appear to come from
    /// Support rather than the CSR who drafted them.
    ///
    /// Register against:
    ///   Entity : email
    ///   Message: Create
    ///   Stage  : PreOperation (20)
    ///   Mode   : Synchronous
    /// </summary>
    public sealed class SwitchOutboundEmailToSupportQueue : IPlugin
    {
        private const string SupportQueueName = "Support Intake Queue";
        private const string SupportEmailAddress = "support@tabperformance.com";

        private const int PreOperationStage = 20;

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, "email", StringComparison.OrdinalIgnoreCase)
                || context.Stage != PreOperationStage)
            {
                return;
            }

            if (!context.InputParameters.Contains("Target")
                || !(context.InputParameters["Target"] is Entity target))
            {
                return;
            }

            // directioncode: true = outgoing, false = incoming. Null defaults to outgoing in CRM,
            // so only skip when the caller has explicitly marked the email as incoming.
            var directionCode = target.GetAttributeValue<bool?>("directioncode");
            if (directionCode == false)
            {
                tracing.Trace("Email is incoming; leaving From/Sender untouched.");
                return;
            }

            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var queueId = ResolveSupportQueueId(service, tracing);
            if (queueId == Guid.Empty)
            {
                tracing.Trace(
                    "Support queue '{0}' / '{1}' not found; leaving From/Sender untouched.",
                    SupportQueueName,
                    SupportEmailAddress);
                return;
            }

            var fromParty = new Entity("activityparty");
            fromParty["partyid"] = new EntityReference("queue", queueId);

            target["from"] = new EntityCollection(new[] { fromParty });
            target["sender"] = SupportEmailAddress;

            tracing.Trace(
                "Outbound email From/Sender rewritten to Support Intake Queue ({0}).",
                SupportEmailAddress);
        }

        private static Guid ResolveSupportQueueId(IOrganizationService service, ITracingService tracing)
        {
            // Match on either the queue name or the support address so an admin rename of
            // one side doesn't silently break the redirection.
            var query = new QueryExpression("queue")
            {
                ColumnSet = new ColumnSet("queueid"),
                TopCount = 2,
                Criteria =
                {
                    FilterOperator = LogicalOperator.Or,
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, SupportQueueName),
                        new ConditionExpression("emailaddress", ConditionOperator.Equal, SupportEmailAddress),
                    },
                },
            };

            var matches = service.RetrieveMultiple(query).Entities;
            if (matches.Count == 0)
            {
                return Guid.Empty;
            }

            if (matches.Count > 1)
            {
                tracing.Trace(
                    "Multiple queues matched name/email lookup; using the first. Consider cleaning up duplicates.");
            }

            return matches.First().Id;
        }
    }
}
