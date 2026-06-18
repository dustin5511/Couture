using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Calculates delivered price per ton for both Trailer (25-ton) and
    /// Straight Truck (20-ton) on Quote Product line items. Pulls freight
    /// inputs from the parent Project (Opportunity) and applies them to
    /// each quote product's unit price.
    ///
    /// Line-level calculation:
    ///   Delivered Price/Ton (Trailer)        = Price Per Unit + Trailer Rate/Ton
    ///   Delivered Price/Ton (Straight Truck) = Price Per Unit + Straight Rate/Ton
    ///   Extended Delivered (Trailer)         = Quantity * Delivered Price/Ton (Trailer)
    ///   Extended Delivered (Straight Truck)  = Quantity * Delivered Price/Ton (Straight)
    ///
    /// Quote-level rollup (RollUpQuoteTotals):
    ///   eb_fobtotal                = Σ (PricePerUnit × Quantity)              (always)
    ///   eb_totaldeliveredtrailer   = Σ ExtendedDeliveredTrailer
    ///                                + eb_taxtotaltrailer (when pref ∈ {2,3})
    ///   eb_totaldeliveredstraight  = Σ ExtendedDeliveredStraight
    ///                                + eb_taxtotalstraight (when pref ∈ {2,3})
    ///   eb_taxtotaltrailer         = Σ eb_taxamounttrailer                    (always)
    ///   eb_taxtotalstraight        = Σ eb_taxamountstraight                   (always)
    ///
    /// Where rates come from the parent Opportunity:
    ///   Total Trip Minutes = Cycle Time + Load Time + Unload Time
    ///   Trailer Rate/Ton   = (Shipping Rate/hr / 60) * Total Trip Minutes / 25
    ///   Straight Rate/Ton  = (Shipping Rate/hr / 60) * Total Trip Minutes / 20
    /// Pre-calculated rates on the Opportunity are preferred over recomputing
    /// from raw inputs; if both are populated we use them directly.
    ///
    /// Incoming-material quote products (rubble, common dirt, etc.) have their
    /// delivery prices cleared instead of calculated. The rollup still runs
    /// afterward so the quote totals reflect the change. Per-line tax fields
    /// are written by CalculateTaxPlugin (which runs at a later rank) and
    /// included in the rollup on subsequent passes.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///   Message=Update, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///     Filter attributes: priceperunit, eb_isincomingmaterial, quantity
    ///   PostImage "PostImage" with columns:
    ///     priceperunit, eb_isincomingmaterial, quoteid, productid, quantity
    public sealed class CalculateDeliveryPricingPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("CalculateDeliveryPricing started. Message={0}, Depth={1}",
                ctx.Execution.MessageName, ctx.Execution.Depth);

            if (ctx.Execution.Depth > 2)
            {
                ctx.Tracing.Trace("Depth > 2 – exiting to prevent recursion.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target entity – exiting.");
                return;
            }

            var record = ResolveRecord(ctx, target);

            // We need the parent quote reference for the rollup regardless
            // of the incoming-material or price outcome, so grab it early.
            var quoteRef = record.GetAttributeValue<EntityReference>(
                SchemaConstants.QuoteDetail.QuoteId);
            if (quoteRef == null)
            {
                ctx.Tracing.Trace("No parent Quote on QuoteDetail – exiting.");
                return;
            }

            var isIncoming = record.GetAttributeValue<bool>(
                SchemaConstants.QuoteDetail.IsIncomingMaterial);
            ctx.Tracing.Trace("Incoming-material flag: {0}", isIncoming);
            if (isIncoming)
            {
                ClearDeliveryPrices(ctx, target.Id);
                RollUpQuoteTotals(ctx, quoteRef.Id);
                return;
            }

            var ppu = record.GetAttributeValue<Money>(
                SchemaConstants.QuoteDetail.PricePerUnit);
            if (ppu == null || ppu.Value <= 0)
            {
                ctx.Tracing.Trace("Price Per Unit is null or zero – clearing delivery prices.");
                ClearDeliveryPrices(ctx, target.Id);
                RollUpQuoteTotals(ctx, quoteRef.Id);
                return;
            }

            var quantity = record.GetAttributeValue<decimal?>(
                SchemaConstants.QuoteDetail.Quantity) ?? 0m;
            ctx.Tracing.Trace("Price Per Unit={0}, Quantity={1}", ppu.Value, quantity);

            // Freight inputs and pre-calculated rates live on the Quote
            // itself now – each quote owns its own values so the user can
            // adjust freight per-quote without disturbing siblings or
            // the project.
            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteRef.Id,
                new ColumnSet(
                    SchemaConstants.Quote.ShippingRatePerHour,
                    SchemaConstants.Quote.CycleTime,
                    SchemaConstants.Quote.LoadTime,
                    SchemaConstants.Quote.UnloadTime,
                    SchemaConstants.Quote.TrailerRatePerTon,
                    SchemaConstants.Quote.StraightTruckRatePerTon));

            if (!TryResolveFreightRates(ctx, quote, out var trailerRate, out var straightRate))
            {
                return;
            }

            // Line-level: per-ton delivered prices
            var deliveredTrailer = Math.Round(ppu.Value + trailerRate, 2);
            var deliveredStraight = Math.Round(ppu.Value + straightRate, 2);

            // Line-level: extended amounts (quantity * delivered price/ton)
            var extTrailer = Math.Round(quantity * deliveredTrailer, 2);
            var extStraight = Math.Round(quantity * deliveredStraight, 2);

            ctx.Tracing.Trace(
                "Delivered Trailer={0}/ton, Straight={1}/ton, " +
                "Extended Trailer={2}, Extended Straight={3}",
                deliveredTrailer, deliveredStraight, extTrailer, extStraight);

            var update = new Entity(SchemaConstants.Entities.QuoteDetail, target.Id)
            {
                [SchemaConstants.QuoteDetail.DeliveredPriceTrailer] = new Money(deliveredTrailer),
                [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = new Money(deliveredStraight),
                [SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer] = new Money(extTrailer),
                [SchemaConstants.QuoteDetail.ExtendedDeliveredStraight] = new Money(extStraight)
            };
            ctx.Service.Update(update);

            ctx.Tracing.Trace("Quote Product updated. Rolling up to Quote...");
            RollUpQuoteTotals(ctx, quoteRef.Id);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// On Update only the changed fields are on Target – use the
        /// post-image so we can read priceperunit/quoteid/etc. Falls back to
        /// a Retrieve if the image isn't registered (typical on Create).
        private static Entity ResolveRecord(PluginContext ctx, Entity target)
        {
            if (ctx.Execution.PostEntityImages.Contains("PostImage"))
            {
                ctx.Tracing.Trace("Using PostImage for field reads.");
                return ctx.Execution.PostEntityImages["PostImage"];
            }

            ctx.Tracing.Trace("PostImage not registered – retrieving record directly.");
            return ctx.Service.Retrieve(
                SchemaConstants.Entities.QuoteDetail,
                target.Id,
                new ColumnSet(
                    SchemaConstants.QuoteDetail.PricePerUnit,
                    SchemaConstants.QuoteDetail.Quantity,
                    SchemaConstants.QuoteDetail.IsIncomingMaterial,
                    SchemaConstants.QuoteDetail.QuoteId,
                    SchemaConstants.QuoteDetail.ProductId));
        }

        /// Prefer the rates that already live on the Quote
        /// (RecalcQuoteOnFreightChange populates them from raw freight
        /// inputs); otherwise derive them from the raw shipping inputs.
        /// Returns false when neither path can produce usable numbers,
        /// so the caller can bail out cleanly.
        private static bool TryResolveFreightRates(
            PluginContext ctx,
            Entity quote,
            out decimal trailerRate,
            out decimal straightRate)
        {
            var trailerMoney = quote.GetAttributeValue<Money>(
                SchemaConstants.Quote.TrailerRatePerTon);
            var straightMoney = quote.GetAttributeValue<Money>(
                SchemaConstants.Quote.StraightTruckRatePerTon);

            if (trailerMoney != null && trailerMoney.Value > 0
                && straightMoney != null && straightMoney.Value > 0)
            {
                trailerRate = trailerMoney.Value;
                straightRate = straightMoney.Value;
                ctx.Tracing.Trace("Using pre-calculated rates – Trailer={0}, Straight={1}",
                    trailerRate, straightRate);
                return true;
            }

            var shipping = quote.GetAttributeValue<Money>(
                SchemaConstants.Quote.ShippingRatePerHour);
            var cycle = quote.GetAttributeValue<int?>(
                SchemaConstants.Quote.CycleTime);

            if (shipping == null || shipping.Value <= 0)
            {
                ctx.Tracing.Trace("Shipping rate missing/zero – cannot calculate freight.");
                trailerRate = straightRate = 0m;
                return false;
            }

            if (cycle == null || cycle.Value <= 0)
            {
                ctx.Tracing.Trace("Cycle time missing/zero – cannot calculate freight.");
                trailerRate = straightRate = 0m;
                return false;
            }

            // Load and unload are hard-coded to 10 minutes each.
            var totalMinutes =
                cycle.Value
                + SchemaConstants.Freight.DefaultLoadTimeMinutes
                + SchemaConstants.Freight.DefaultUnloadTimeMinutes;

            var costPerTrip = (shipping.Value / SchemaConstants.Freight.MinutesPerHour) * totalMinutes;
            trailerRate = Math.Round(costPerTrip / SchemaConstants.Freight.TrailerTonsPerLoad, 2);
            straightRate = Math.Round(costPerTrip / SchemaConstants.Freight.StraightTruckTonsPerLoad, 2);

            ctx.Tracing.Trace("Calculated rates – Trailer={0}/ton, Straight={1}/ton, " +
                "TotalMinutes={2}, ShippingRate={3}",
                trailerRate, straightRate, totalMinutes, shipping.Value);
            return true;
        }

        private static void ClearDeliveryPrices(PluginContext ctx, Guid quoteDetailId)
        {
            var update = new Entity(SchemaConstants.Entities.QuoteDetail, quoteDetailId)
            {
                [SchemaConstants.QuoteDetail.DeliveredPriceTrailer] = null,
                [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = null,
                [SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer] = null,
                [SchemaConstants.QuoteDetail.ExtendedDeliveredStraight] = null
            };
            ctx.Service.Update(update);
            ctx.Tracing.Trace("Delivery prices cleared on QuoteDetail {0}.", quoteDetailId);
        }

        /// Queries every QuoteDetail under the given Quote and writes the
        /// five header totals to the parent Quote in one Update:
        ///   eb_fobtotal               = Σ priceperunit × quantity
        ///   eb_taxtotaltrailer        = Σ eb_taxamounttrailer
        ///   eb_taxtotalstraight       = Σ eb_taxamountstraight
        ///   eb_totaldeliveredtrailer  = Σ extendeddeliveredtrailer  (+ tax if pref allows)
        ///   eb_totaldeliveredstraight = Σ extendeddeliveredstraight (+ tax if pref allows)
        ///
        /// The opportunity's delivery preference decides whether tax
        /// totals fold into the delivered totals. For FOB-only quotes the
        /// per-line tax fields are 0 so the math collapses cleanly even
        /// without a branch, but we keep the check explicit so a tax
        /// value from a stale state doesn't sneak in.
        private static void RollUpQuoteTotals(PluginContext ctx, Guid quoteId)
        {
            ctx.Tracing.Trace("RollUpQuoteTotals: starting for Quote {0}.", quoteId);

            var includeTaxInDelivered = ShouldIncludeTaxInDeliveredTotal(ctx, quoteId);

            var query = new QueryExpression(SchemaConstants.Entities.QuoteDetail)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.QuoteDetail.PricePerUnit,
                    SchemaConstants.QuoteDetail.Quantity,
                    SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer,
                    SchemaConstants.QuoteDetail.ExtendedDeliveredStraight,
                    SchemaConstants.QuoteDetail.TaxAmountTrailer,
                    SchemaConstants.QuoteDetail.TaxAmountStraight),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SchemaConstants.QuoteDetail.QuoteId,
                            ConditionOperator.Equal,
                            quoteId)
                    }
                }
            };

            var lines = ctx.Service.RetrieveMultiple(query);
            ctx.Tracing.Trace("Found {0} quote product(s).", lines.Entities.Count);

            var fobTotal = 0m;
            var totalTrailer = 0m;
            var totalStraight = 0m;
            var taxTotalTrailer = 0m;
            var taxTotalStraight = 0m;

            foreach (var line in lines.Entities)
            {
                var ppu = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.PricePerUnit);
                var qty = line.GetAttributeValue<decimal>(
                    SchemaConstants.QuoteDetail.Quantity);
                var extT = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer);
                var extS = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.ExtendedDeliveredStraight);
                var taxT = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.TaxAmountTrailer);
                var taxS = line.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.TaxAmountStraight);

                if (ppu != null) fobTotal += ppu.Value * qty;
                if (extT != null) totalTrailer += extT.Value;
                if (extS != null) totalStraight += extS.Value;
                if (taxT != null) taxTotalTrailer += taxT.Value;
                if (taxS != null) taxTotalStraight += taxS.Value;
            }

            if (includeTaxInDelivered)
            {
                totalTrailer += taxTotalTrailer;
                totalStraight += taxTotalStraight;
            }

            fobTotal = Math.Round(fobTotal, 2);
            totalTrailer = Math.Round(totalTrailer, 2);
            totalStraight = Math.Round(totalStraight, 2);
            taxTotalTrailer = Math.Round(taxTotalTrailer, 2);
            taxTotalStraight = Math.Round(taxTotalStraight, 2);

            ctx.Tracing.Trace(
                "Totals – FOB={0}, DeliveredTrailer={1}, DeliveredStraight={2}, " +
                "TaxTrailer={3}, TaxStraight={4}, IncludeTax={5}",
                fobTotal, totalTrailer, totalStraight,
                taxTotalTrailer, taxTotalStraight, includeTaxInDelivered);

            var updateQuote = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.FobTotal] = new Money(fobTotal),
                [SchemaConstants.Quote.TotalDeliveredTrailer] = new Money(totalTrailer),
                [SchemaConstants.Quote.TotalDeliveredStraight] = new Money(totalStraight),
                [SchemaConstants.Quote.TaxTotalTrailer] = new Money(taxTotalTrailer),
                [SchemaConstants.Quote.TaxTotalStraight] = new Money(taxTotalStraight)
            };
            ctx.Service.Update(updateQuote);

            ctx.Tracing.Trace("Quote totals updated.");
        }

        private static bool ShouldIncludeTaxInDeliveredTotal(PluginContext ctx, Guid quoteId)
        {
            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteId,
                new ColumnSet(SchemaConstants.Quote.DeliveryPreference));
            var preference = quote.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Quote.DeliveryPreference);
            if (preference == null) return false;

            return preference.Value == SchemaConstants.DeliveryPreference.Delivery
                || preference.Value == SchemaConstants.DeliveryPreference.FobAndDelivery;
        }
    }
}
