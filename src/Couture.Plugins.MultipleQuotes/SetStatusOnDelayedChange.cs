using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Synchronises the quote's status reason with the eb_delayed flag:
    ///
    ///   eb_delayed = true  → State=Active (1), Status=Delayed (122050002)
    ///   eb_delayed = false → State=Active (1), Status=InProgressActive (2)
    ///                    or  State=Draft  (0), Status=InProgressDraft  (1)
    ///                        depending on the quote's current state.
    ///
    /// This means the Delayed status is always visible in views and
    /// dashboards, and the rollup can rely on statuscode alone rather
    /// than the boolean.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Filter attributes: eb_delayed
    public sealed class SetStatusOnDelayedChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace(
                "SetStatusOnDelayedChange started. Depth={0}",
                ctx.Execution.Depth);

            if (ctx.Execution.Depth > 2)
            {
                ctx.Tracing.Trace("Depth > 2 – exiting to prevent recursion.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            if (!target.Contains(SchemaConstants.Quote.Delayed))
            {
                ctx.Tracing.Trace("eb_delayed not in Target – exiting.");
                return;
            }

            var delayed = target.GetAttributeValue<bool>(SchemaConstants.Quote.Delayed);

            if (delayed)
            {
                ctx.Tracing.Trace("Delayed=true – setting Active/Delayed.");
                ctx.Service.Execute(new SetStateRequest
                {
                    EntityMoniker = new EntityReference(
                        SchemaConstants.Entities.Quote, target.Id),
                    State = new OptionSetValue(SchemaConstants.QuoteState.Active),
                    Status = new OptionSetValue(SchemaConstants.QuoteStatus.Delayed)
                });
            }
            else
            {
                var quote = ctx.Service.Retrieve(
                    SchemaConstants.Entities.Quote,
                    target.Id,
                    new ColumnSet(SchemaConstants.Quote.StateCode));

                var stateCode = quote.GetAttributeValue<OptionSetValue>(
                    SchemaConstants.Quote.StateCode);
                var stateVal = stateCode?.Value ?? SchemaConstants.QuoteState.Active;

                int newState;
                int newStatus;

                if (stateVal == SchemaConstants.QuoteState.Draft)
                {
                    newState = SchemaConstants.QuoteState.Draft;
                    newStatus = SchemaConstants.QuoteStatus.InProgressDraft;
                }
                else
                {
                    newState = SchemaConstants.QuoteState.Active;
                    newStatus = SchemaConstants.QuoteStatus.InProgressActive;
                }

                ctx.Tracing.Trace(
                    "Delayed=false – restoring state={0}, status={1}.",
                    newState, newStatus);

                ctx.Service.Execute(new SetStateRequest
                {
                    EntityMoniker = new EntityReference(
                        SchemaConstants.Entities.Quote, target.Id),
                    State = new OptionSetValue(newState),
                    Status = new OptionSetValue(newStatus)
                });
            }
        }
    }
}
