using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Re-fires the per-line tax + delivered-total calculations whenever
    /// the user changes eb_deliverypreference OR the ship-to ZIP on a
    /// Quote. The preference is seeded from the parent Project on Create
    /// (see ValidateFreightBeforeQuoteCreate), but each quote owns the
    /// value from that point on so the user can model a different
    /// scenario (FOB vs Delivery vs FOB and Delivery) on a per-quote
    /// basis. The ship-to ZIP drives the tax-rate lookup, so changing
    /// the job site on a Delivery quote re-prices the tax too.
    ///
    /// We don't recompute anything in-process; instead we touch each
    /// child QuoteDetail by writing its current Quantity back to itself.
    /// That re-fires CalculateDeliveryPricingPlugin and CalculateTaxPlugin
    /// (both filter on `quantity`), which read the updated preference and
    /// ZIP off the parent Quote and produce the right line-level tax
    /// amounts. CalculateDeliveryPricingPlugin's rollup then writes the
    /// new quote totals.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Filter attributes: eb_deliverypreference, shipto_postalcode
    public sealed class RecalcOnQuoteDeliveryPreferenceChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace(
                "RecalcOnQuoteDeliveryPreferenceChange started. Depth={0}",
                ctx.Execution.Depth);

            // Touching child quotedetails re-fires CalculateTaxPlugin /
            // CalculateDeliveryPricingPlugin, each of which writes back to
            // the same line. Those plugins have their own depth guards but
            // this one shouldn't loop on itself.
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

            var quoteId = target.Id;

            var query = new QueryExpression(SchemaConstants.Entities.QuoteDetail)
            {
                ColumnSet = new ColumnSet(SchemaConstants.QuoteDetail.Quantity),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SchemaConstants.QuoteDetail.QuoteId,
                            ConditionOperator.Equal, quoteId)
                    }
                }
            };

            var lines = ctx.Service.RetrieveMultiple(query);
            ctx.Tracing.Trace("Found {0} quote product(s) to re-trigger.",
                lines.Entities.Count);

            foreach (var line in lines.Entities)
            {
                var qty = line.GetAttributeValue<decimal>(
                    SchemaConstants.QuoteDetail.Quantity);

                // Writing the same quantity is a no-op for data but trips
                // the filter attribute on the downstream pricing plugins,
                // which is the cheapest way to re-fire them without
                // duplicating their math here.
                var nudge = new Entity(
                    SchemaConstants.Entities.QuoteDetail, line.Id)
                {
                    [SchemaConstants.QuoteDetail.Quantity] = qty
                };
                ctx.Service.Update(nudge);
            }

            ctx.Tracing.Trace(
                "Re-triggered {0} line(s) for Quote {1}.",
                lines.Entities.Count, quoteId);
        }
    }
}
