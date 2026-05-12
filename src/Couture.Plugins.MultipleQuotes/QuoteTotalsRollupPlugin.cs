using System;
using System.Linq;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Recomputes the Quote-level pricing totals whenever a quote-detail
    /// changes. Three totals plus the matching tax buckets:
    ///
    ///   eb_fobtotal               = Σ (priceperunit × quantity)         [no tax]
    ///   eb_taxtotaltrailer        = Σ eb_taxamounttrailer
    ///   eb_taxtotalstraight       = Σ eb_taxamountstraight
    ///   eb_totaldeliveredtrailer  = Σ (eb_deliveredpricetrailer × qty)
    ///                               + eb_taxtotaltrailer  (when pref allows)
    ///   eb_totaldeliveredstraight = Σ (eb_deliveredpricestraight × qty)
    ///                               + eb_taxtotalstraight (when pref allows)
    ///
    /// Tax is added to the delivered totals only when the parent
    /// Opportunity's eb_deliverypreference is Delivery (2) or
    /// FobAndDelivery (3). For FOB-only the per-line tax fields are
    /// already 0, so the math still works without a branch – but we keep
    /// the check explicit so the rollup is defensible if a tax row slips
    /// through from a different code path.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///   Message=Update, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///     Filter attributes:
    ///       priceperunit, quantity, eb_isincomingmaterial,
    ///       eb_deliveredpricetrailer, eb_deliveredpricestraight,
    ///       eb_taxamounttrailer, eb_taxamountstraight, quoteid
    ///   Message=Delete, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///     PreImage "PreImage" with column quoteid
    ///   PostImage "PostImage" on Create/Update with column quoteid
    ///
    /// Run this step AFTER CalculateDeliveryPricingPlugin and
    /// CalculateTaxPlugin so it observes their writes.
    public sealed class QuoteTotalsRollupPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("QuoteTotalsRollup started. Message={0}, Depth={1}",
                ctx.Execution.MessageName, ctx.Execution.Depth);

            // Our own write targets Quote (not quotedetail), so this plugin
            // cannot retrigger itself. The depth guard catches unrelated
            // upstream plugin chains.
            if (ctx.Execution.Depth > 3)
            {
                ctx.Tracing.Trace("Depth > 3 – exiting to prevent runaway chains.");
                return;
            }

            var quoteId = ResolveQuoteId(ctx);
            if (quoteId == Guid.Empty)
            {
                ctx.Tracing.Trace("Could not resolve parent QuoteId – exiting.");
                return;
            }

            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteId,
                new ColumnSet(SchemaConstants.Quote.OpportunityId));
            var oppRef = quote.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.OpportunityId);

            var includeTaxInDelivered = ShouldIncludeTaxInDeliveredTotal(ctx, oppRef);

            var query = new QueryExpression(SchemaConstants.Entities.QuoteDetail)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.QuoteDetail.PricePerUnit,
                    SchemaConstants.QuoteDetail.Quantity,
                    SchemaConstants.QuoteDetail.DeliveredPriceTrailer,
                    SchemaConstants.QuoteDetail.DeliveredPriceStraight,
                    SchemaConstants.QuoteDetail.TaxAmountTrailer,
                    SchemaConstants.QuoteDetail.TaxAmountStraight),
                NoLock = true
            };
            query.Criteria.AddCondition(
                SchemaConstants.QuoteDetail.QuoteId, ConditionOperator.Equal, quoteId);

            // On Delete the line is gone before PostOperation, so excluding
            // it would happen automatically; on Create/Update including it
            // is what we want.
            var details = ctx.Service.RetrieveMultiple(query).Entities;
            ctx.Tracing.Trace("Aggregating {0} quote details for quote {1}.",
                details.Count, quoteId);

            decimal fobTotal = 0m;
            decimal deliveredTrailerSubtotal = 0m;
            decimal deliveredStraightSubtotal = 0m;
            decimal taxTotalTrailer = 0m;
            decimal taxTotalStraight = 0m;

            foreach (var line in details)
            {
                var ppu = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.PricePerUnit);
                var qty = line.GetAttributeValue<decimal>(
                    SchemaConstants.QuoteDetail.Quantity);
                var dt = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.DeliveredPriceTrailer);
                var ds = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.DeliveredPriceStraight);
                var tt = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.TaxAmountTrailer);
                var ts = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.TaxAmountStraight);

                if (ppu != null) fobTotal += ppu.Value * qty;
                if (dt != null) deliveredTrailerSubtotal += dt.Value * qty;
                if (ds != null) deliveredStraightSubtotal += ds.Value * qty;
                if (tt != null) taxTotalTrailer += tt.Value;
                if (ts != null) taxTotalStraight += ts.Value;
            }

            var deliveredTotalTrailer = deliveredTrailerSubtotal
                + (includeTaxInDelivered ? taxTotalTrailer : 0m);
            var deliveredTotalStraight = deliveredStraightSubtotal
                + (includeTaxInDelivered ? taxTotalStraight : 0m);

            ctx.Tracing.Trace(
                "Totals: fob={0}, deliveredTrailer={1} (+{2} tax), " +
                "deliveredStraight={3} (+{4} tax). IncludeTax={5}",
                fobTotal, deliveredTrailerSubtotal, taxTotalTrailer,
                deliveredStraightSubtotal, taxTotalStraight, includeTaxInDelivered);

            var update = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.FobTotal] = new Money(Math.Round(fobTotal, 2)),
                [SchemaConstants.Quote.DeliveredTotalTrailer] =
                    new Money(Math.Round(deliveredTotalTrailer, 2)),
                [SchemaConstants.Quote.DeliveredTotalStraight] =
                    new Money(Math.Round(deliveredTotalStraight, 2)),
                [SchemaConstants.Quote.TaxTotalTrailer] =
                    new Money(Math.Round(taxTotalTrailer, 2)),
                [SchemaConstants.Quote.TaxTotalStraight] =
                    new Money(Math.Round(taxTotalStraight, 2))
            };
            ctx.Service.Update(update);
        }

        /// QuoteId lives on the Target for Create, on the PostImage for
        /// Update, and on the PreImage for Delete. The Target on Update
        /// only carries fields that actually changed.
        private static Guid ResolveQuoteId(PluginContext ctx)
        {
            if (ctx.Execution.MessageName == SchemaConstants.Messages.Delete)
            {
                if (ctx.Execution.PreEntityImages.Contains("PreImage"))
                {
                    var pre = ctx.Execution.PreEntityImages["PreImage"];
                    return pre.GetAttributeValue<EntityReference>(
                        SchemaConstants.QuoteDetail.QuoteId)?.Id ?? Guid.Empty;
                }
                return Guid.Empty;
            }

            if (ctx.Execution.InputParameters.Contains("Target")
                && ctx.Execution.InputParameters["Target"] is Entity target)
            {
                var fromTarget = target.GetAttributeValue<EntityReference>(
                    SchemaConstants.QuoteDetail.QuoteId);
                if (fromTarget != null) return fromTarget.Id;
            }

            if (ctx.Execution.PostEntityImages.Contains("PostImage"))
            {
                var post = ctx.Execution.PostEntityImages["PostImage"];
                return post.GetAttributeValue<EntityReference>(
                    SchemaConstants.QuoteDetail.QuoteId)?.Id ?? Guid.Empty;
            }

            return Guid.Empty;
        }

        private static bool ShouldIncludeTaxInDeliveredTotal(
            PluginContext ctx, EntityReference oppRef)
        {
            if (oppRef == null) return false;

            var opportunity = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppRef.Id,
                new ColumnSet(SchemaConstants.Opportunity.DeliveryPreference));
            var preference = opportunity.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Opportunity.DeliveryPreference);
            if (preference == null) return false;

            return preference.Value == SchemaConstants.DeliveryPreference.Delivery
                || preference.Value == SchemaConstants.DeliveryPreference.FobAndDelivery;
        }
    }
}
