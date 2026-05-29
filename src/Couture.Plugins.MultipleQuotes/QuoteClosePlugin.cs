using System;
using System.Linq;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;

namespace Couture.Plugins.MultipleQuotes
{
    /// Handles the Close (as Lost/Cancelled) message on Quote.
    /// When the last open quote is closed we cascade the result to the
    /// opportunity:
    ///   * If any quote on the opportunity is Won → close opportunity Won.
    ///   * Otherwise every quote is Lost/Cancelled → close opportunity Lost.
    /// The rollup field is refreshed unconditionally.
    ///
    /// Register on: Message=Close, PrimaryEntity=quote, Stage=PostOperation (40).
    public sealed class QuoteClosePlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            var quoteClose = ctx.Execution.InputParameters.Contains("QuoteClose")
                ? ctx.Execution.InputParameters["QuoteClose"] as Entity
                : null;

            if (quoteClose == null)
            {
                ctx.Tracing.Trace("No QuoteClose in context; nothing to do.");
                return;
            }

            var quoteRef = quoteClose.GetAttributeValue<EntityReference>(SchemaConstants.Quote.Id);
            if (quoteRef == null)
            {
                return;
            }

            var helper = new QuoteOpportunityService(ctx.Service, ctx.Tracing);

            var quote = helper.RetrieveQuote(
                quoteRef.Id,
                SchemaConstants.Quote.OpportunityId,
                SchemaConstants.Quote.StateCode,
                SchemaConstants.Quote.StatusCode);

            var opportunityRef = quote.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
            if (opportunityRef == null)
            {
                ctx.Tracing.Trace("Closed quote has no opportunity – skipping cascade.");
                return;
            }

            // Refresh regardless of cascade outcome – a closed quote no longer
            // contributes to the active/won rollup.
            helper.RefreshOpportunityRollup(opportunityRef.Id);

            // If the user explicitly closed the opportunity already we leave
            // it alone. OpportunityState != Open means the user (or an
            // earlier cascade) has already finalised it.
            var opportunity = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                opportunityRef.Id,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(SchemaConstants.Opportunity.StateCode));

            var oppState = opportunity.GetAttributeValue<OptionSetValue>(SchemaConstants.Opportunity.StateCode);
            if (oppState == null || oppState.Value != SchemaConstants.OpportunityState.Open)
            {
                ctx.Tracing.Trace("Opportunity already closed (state {0}) – cascade skipped.",
                    oppState?.Value);
                return;
            }

            var siblings = helper.RetrieveSiblingQuotes(opportunityRef.Id, quoteRef.Id);
            // Include the current quote so "all closed" evaluates correctly.
            var everyQuote = siblings.Concat(new[] { quote }).ToList();

            var anyOpen = everyQuote.Any(helper.IsQuoteOpen);
            if (anyOpen)
            {
                ctx.Tracing.Trace("At least one quote still open – leaving opportunity untouched.");
                return;
            }

            // No open quotes remain → decide Won vs Lost for the opportunity.
            // A single Won quote is enough to close the opportunity as Won;
            // otherwise every remaining quote is Lost/Cancelled/Revised and
            // the opportunity closes as Lost. Stamp the pipeline stage
            // before the close request because the project becomes
            // read-only once closed.
            if (everyQuote.Any(helper.IsQuoteWon))
            {
                helper.SetProjectPipelineStage(
                    opportunityRef.Id,
                    SchemaConstants.ProjectPipelineStage.Won);
                helper.CloseOpportunityAsWon(opportunityRef.Id);
            }
            else
            {
                helper.SetProjectPipelineStage(
                    opportunityRef.Id,
                    SchemaConstants.ProjectPipelineStage.Lost);
                helper.CloseOpportunityAsLost(opportunityRef.Id);
            }
        }
    }
}
