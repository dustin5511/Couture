using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Synchronises the quote's status reason with the eb_delayed flag
    /// AND cascades the delay onto the parent Project:
    ///
    ///   eb_delayed = true  → Quote: State=Active, Status=Delayed
    ///                        Project: Status=OnHold (2),
    ///                                 eb_pipelinestage=Delayed
    ///   eb_delayed = false → Quote: restored to InProgress (Draft 1
    ///                        or Active 2 depending on current state).
    ///                        Project: if still in Open/OnHold, restored
    ///                                 to InProgress (1) with
    ///                                 eb_pipelinestage=Open. Skipped if
    ///                                 project has been won, lost or
    ///                                 manually moved off OnHold.
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
            var helper = new QuoteOpportunityService(ctx.Service, ctx.Tracing);

            // We need the parent opportunity (both branches cascade) and
            // the current statecode (the un-delay branch uses it to pick
            // the right InProgress status). Read both in a single hop.
            var quoteSnapshot = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                target.Id,
                new ColumnSet(
                    SchemaConstants.Quote.OpportunityId,
                    SchemaConstants.Quote.StateCode));
            var oppRef = quoteSnapshot.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.OpportunityId);

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

                if (oppRef != null)
                {
                    helper.MarkProjectDelayed(oppRef.Id);
                }
            }
            else
            {
                var stateCode = quoteSnapshot.GetAttributeValue<OptionSetValue>(
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

                if (oppRef != null)
                {
                    helper.ClearProjectDelayed(oppRef.Id);
                }
            }
        }
    }
}
