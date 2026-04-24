using System;
using System.Collections.Generic;
using System.Linq;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;

namespace Couture.Plugins.MultipleQuotes
{
    /// Intercepts the Win message on Quote so that:
    ///   * Winning one quote no longer forces the opportunity closed while
    ///     other quotes remain open.
    ///   * Sibling quotes the platform auto-revises are reinstated.
    ///   * Only when every sibling is already in a closed state does the
    ///     opportunity get closed as Won.
    ///
    /// Register two steps, both pointing at this class:
    ///   1. Message=Win, PrimaryEntity=quote, Stage=PreOperation (20)
    ///   2. Message=Win, PrimaryEntity=quote, Stage=PostOperation (40)
    public sealed class QuoteWinPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            var quoteClose = ctx.Execution.InputParameters.Contains("QuoteClose")
                ? ctx.Execution.InputParameters["QuoteClose"] as Entity
                : null;

            if (quoteClose == null)
            {
                ctx.Tracing.Trace("No QuoteClose on Target – nothing to do.");
                return;
            }

            var quoteRef = quoteClose.GetAttributeValue<EntityReference>(SchemaConstants.Quote.Id);
            if (quoteRef == null)
            {
                ctx.Tracing.Trace("QuoteClose is missing quoteid – skipping.");
                return;
            }

            var helper = new QuoteOpportunityService(ctx.Service, ctx.Tracing);

            if (ctx.Execution.Stage == 20) // PreOperation
            {
                HandlePreOperation(ctx, helper, quoteRef.Id);
            }
            else if (ctx.Execution.Stage == 40) // PostOperation
            {
                HandlePostOperation(ctx, helper, quoteRef.Id);
            }
        }

        private static void HandlePreOperation(PluginContext ctx, QuoteOpportunityService helper, Guid quoteId)
        {
            var quote = helper.RetrieveQuote(
                quoteId,
                SchemaConstants.Quote.OpportunityId,
                SchemaConstants.Quote.StateCode);

            var opportunityRef = quote.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
            if (opportunityRef == null)
            {
                ctx.Tracing.Trace("Quote has no opportunity – default Win semantics apply.");
                return;
            }

            var siblings = helper.RetrieveSiblingQuotes(opportunityRef.Id, quoteId);
            var stillOpen = siblings
                .Where(helper.IsQuoteOpen)
                .Select(q => q.Id)
                .ToList();

            if (stillOpen.Count == 0)
            {
                ctx.Tracing.Trace("No other open quotes – platform's default Win cascade is correct.");
                return;
            }

            // Stash the ids so the post-operation step knows which quotes to
            // put back into Active once the platform has finished its cascade.
            ctx.Execution.SharedVariables[SchemaConstants.SharedVariableKeys.SiblingActiveQuoteIds] =
                string.Join(",", stillOpen.Select(id => id.ToString("D")));
            ctx.Execution.SharedVariables[SchemaConstants.SharedVariableKeys.OpportunityToReopen] =
                opportunityRef.Id.ToString("D");
        }

        private static void HandlePostOperation(PluginContext ctx, QuoteOpportunityService helper, Guid quoteId)
        {
            var siblings = ReadSiblingIds(ctx);
            var opportunityId = ReadOpportunityId(ctx);

            if (opportunityId == Guid.Empty || siblings.Count == 0)
            {
                // Default path: every other quote was already closed, so the
                // platform correctly won the opportunity. Nothing to undo,
                // just refresh the rollup.
                var quote = helper.RetrieveQuote(quoteId, SchemaConstants.Quote.OpportunityId);
                var oppRef = quote.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
                if (oppRef != null)
                {
                    helper.RefreshOpportunityRollup(oppRef.Id);
                }
                return;
            }

            // The platform has just won the quote and closed the opportunity
            // as Won. Reverse the opportunity closure and put every sibling
            // quote back into Active so the sales rep can keep working them.
            helper.ReopenOpportunity(opportunityId);

            foreach (var siblingId in siblings)
            {
                try
                {
                    helper.ReactivateQuote(siblingId);
                }
                catch (Exception ex)
                {
                    // Don't fail the whole transaction if one sibling can't
                    // be reactivated – surface it in trace and move on.
                    ctx.Tracing.Trace("Could not reactivate quote {0}: {1}", siblingId, ex.Message);
                }
            }

            helper.RefreshOpportunityRollup(opportunityId);
        }

        private static List<Guid> ReadSiblingIds(PluginContext ctx)
        {
            if (!ctx.Execution.SharedVariables.Contains(SchemaConstants.SharedVariableKeys.SiblingActiveQuoteIds))
            {
                return new List<Guid>();
            }

            var raw = ctx.Execution.SharedVariables[SchemaConstants.SharedVariableKeys.SiblingActiveQuoteIds] as string;
            if (string.IsNullOrWhiteSpace(raw)) return new List<Guid>();

            return raw.Split(',')
                .Select(p => p.Trim())
                .Where(p => Guid.TryParse(p, out _))
                .Select(Guid.Parse)
                .ToList();
        }

        private static Guid ReadOpportunityId(PluginContext ctx)
        {
            if (!ctx.Execution.SharedVariables.Contains(SchemaConstants.SharedVariableKeys.OpportunityToReopen))
            {
                return Guid.Empty;
            }

            var raw = ctx.Execution.SharedVariables[SchemaConstants.SharedVariableKeys.OpportunityToReopen] as string;
            return Guid.TryParse(raw, out var parsed) ? parsed : Guid.Empty;
        }
    }
}
