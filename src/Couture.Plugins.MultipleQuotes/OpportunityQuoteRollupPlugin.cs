using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;

namespace Couture.Plugins.MultipleQuotes
{
    /// Keeps the opportunity's "total of active + won quotes" money field in
    /// sync. The rollup sums each active/won quote's eb_fobtotal (product
    /// prices × qty, no delivery, no tax) – Kraemer wants the revenue
    /// number to reflect the base product value regardless of delivery
    /// preference. Quotes flagged eb_delayed = true are excluded even when
    /// they're Active/Won. Delta-trigger only: we refresh when eb_fobtotal,
    /// state, eb_delayed or the parent opportunity change – not on every
    /// Update of an unrelated field.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///     Filter attributes: eb_fobtotal, statecode, statuscode,
    ///                        opportunityid, eb_delayed
    ///     Pre-image "preImage" with columns: opportunityid, statecode
    ///   Message=Delete, PrimaryEntity=quote, Stage=PostOperation (40)
    ///     Pre-image "preImage" with columns: opportunityid
    public sealed class OpportunityQuoteRollupPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            if (ctx.Execution.Depth > 3)
            {
                ctx.Tracing.Trace("Depth {0} exceeded – bailing out of rollup refresh.", ctx.Execution.Depth);
                return;
            }

            var helper = new QuoteOpportunityService(ctx.Service, ctx.Tracing);

            var opportunityIds = ResolveOpportunityIds(ctx);
            foreach (var id in opportunityIds)
            {
                if (id == Guid.Empty) continue;
                helper.RefreshOpportunityRollup(id);
            }
        }

        /// A single event may affect two opportunities when the quote is
        /// reparented (the old one loses the amount, the new one gains it),
        /// so we return an array.
        private static Guid[] ResolveOpportunityIds(PluginContext ctx)
        {
            var current = Guid.Empty;
            var previous = Guid.Empty;

            if (ctx.Execution.MessageName == SchemaConstants.Messages.Delete)
            {
                var preImage = ctx.Execution.PreEntityImages.Contains("preImage")
                    ? ctx.Execution.PreEntityImages["preImage"]
                    : null;

                var oppRef = preImage?.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
                if (oppRef != null) current = oppRef.Id;
                return new[] { current };
            }

            var target = ctx.Execution.InputParameters.Contains("Target")
                ? ctx.Execution.InputParameters["Target"] as Entity
                : null;

            if (target != null)
            {
                var oppRef = target.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
                if (oppRef != null) current = oppRef.Id;
            }

            if (ctx.Execution.PreEntityImages.Contains("preImage"))
            {
                var preImage = ctx.Execution.PreEntityImages["preImage"];
                var oppRef = preImage.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
                if (oppRef != null) previous = oppRef.Id;
            }

            // Fallback: if Update didn't include opportunityid in the target,
            // retrieve it from the platform so we still refresh the rollup.
            if (current == Guid.Empty && target != null && target.Id != Guid.Empty)
            {
                try
                {
                    var fresh = new QuoteOpportunityService(ctx.Service, ctx.Tracing)
                        .RetrieveQuote(target.Id, SchemaConstants.Quote.OpportunityId);
                    var oppRef = fresh.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
                    if (oppRef != null) current = oppRef.Id;
                }
                catch (Exception ex)
                {
                    ctx.Tracing.Trace("Could not hydrate quote for rollup: {0}", ex.Message);
                }
            }

            return current == previous || previous == Guid.Empty
                ? new[] { current }
                : new[] { current, previous };
        }
    }
}
