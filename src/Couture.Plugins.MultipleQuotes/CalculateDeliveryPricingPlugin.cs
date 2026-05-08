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
    /// Delivered Price/Ton (Trailer)        = Price Per Unit + Trailer Rate/Ton
    /// Delivered Price/Ton (Straight Truck) = Price Per Unit + Straight Rate/Ton
    /// Where:
    ///   Total Trip Minutes = Cycle Time + Load Time + Unload Time
    ///   Trailer Rate/Ton   = (Shipping Rate/hr / 60) * Total Trip Minutes / 25
    ///   Straight Rate/Ton  = (Shipping Rate/hr / 60) * Total Trip Minutes / 20
    /// Pre-calculated rates on the Opportunity are preferred over recomputing
    /// from raw inputs; if both are populated we use them directly.
    ///
    /// Incoming-material quote products (rubble, common dirt, etc.) have their
    /// delivery prices cleared instead of calculated.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///   Message=Update, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///     Filter attributes: priceperunit, eb_isincomingmaterial
    ///   PostImage "PostImage" with columns:
    ///     priceperunit, eb_isincomingmaterial, quoteid, productid
    public sealed class CalculateDeliveryPricingPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("CalculateDeliveryPricing started. Message={0}, Depth={1}",
                ctx.Execution.MessageName, ctx.Execution.Depth);

            // Belt-and-braces: our own write only touches delivered price
            // fields (not in the Update filter), so this guard is just for
            // unrelated chains of plugins triggering us.
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

            var isIncoming = record.GetAttributeValue<bool>(SchemaConstants.QuoteDetail.IsIncomingMaterial);
            ctx.Tracing.Trace("Incoming-material flag: {0}", isIncoming);
            if (isIncoming)
            {
                ClearDeliveryPrices(ctx, target.Id);
                return;
            }

            var ppu = record.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.PricePerUnit);
            if (ppu == null || ppu.Value <= 0)
            {
                ctx.Tracing.Trace("Price Per Unit is null or zero – clearing delivery prices.");
                ClearDeliveryPrices(ctx, target.Id);
                return;
            }

            var quoteRef = record.GetAttributeValue<EntityReference>(SchemaConstants.QuoteDetail.QuoteId);
            if (quoteRef == null)
            {
                ctx.Tracing.Trace("No parent Quote on QuoteDetail – exiting.");
                return;
            }

            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteRef.Id,
                new ColumnSet(SchemaConstants.Quote.OpportunityId));

            var oppRef = quote.GetAttributeValue<EntityReference>(SchemaConstants.Quote.OpportunityId);
            if (oppRef == null)
            {
                ctx.Tracing.Trace("No parent Opportunity on Quote – exiting.");
                return;
            }

            var opportunity = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppRef.Id,
                new ColumnSet(
                    SchemaConstants.Opportunity.ShippingRatePerHour,
                    SchemaConstants.Opportunity.CycleTime,
                    SchemaConstants.Opportunity.LoadTime,
                    SchemaConstants.Opportunity.UnloadTime,
                    SchemaConstants.Opportunity.TrailerRatePerTon,
                    SchemaConstants.Opportunity.StraightTruckRatePerTon));

            if (!TryResolveFreightRates(ctx, opportunity, out var trailerRate, out var straightRate))
            {
                return;
            }

            var deliveredTrailer = Math.Round(ppu.Value + trailerRate, 2);
            var deliveredStraight = Math.Round(ppu.Value + straightRate, 2);

            ctx.Tracing.Trace("Delivered Trailer={0}, Delivered Straight={1}",
                deliveredTrailer, deliveredStraight);

            var update = new Entity(SchemaConstants.Entities.QuoteDetail, target.Id)
            {
                [SchemaConstants.QuoteDetail.DeliveredPriceTrailer] = new Money(deliveredTrailer),
                [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = new Money(deliveredStraight)
            };
            ctx.Service.Update(update);
        }

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
                    SchemaConstants.QuoteDetail.IsIncomingMaterial,
                    SchemaConstants.QuoteDetail.QuoteId,
                    SchemaConstants.QuoteDetail.ProductId));
        }

        /// Prefer the rates that already live on the Opportunity (a business
        /// rule or rollup may have populated them); otherwise derive them
        /// from the raw shipping inputs. Returns false when neither path can
        /// produce usable numbers, so the caller can bail out cleanly.
        private static bool TryResolveFreightRates(
            PluginContext ctx,
            Entity opportunity,
            out decimal trailerRate,
            out decimal straightRate)
        {
            var trailerMoney = opportunity.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.TrailerRatePerTon);
            var straightMoney = opportunity.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.StraightTruckRatePerTon);

            if (trailerMoney != null && trailerMoney.Value > 0
                && straightMoney != null && straightMoney.Value > 0)
            {
                trailerRate = trailerMoney.Value;
                straightRate = straightMoney.Value;
                ctx.Tracing.Trace("Using pre-calculated rates – Trailer={0}, Straight={1}",
                    trailerRate, straightRate);
                return true;
            }

            var shipping = opportunity.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.ShippingRatePerHour);
            var cycle = opportunity.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.CycleTime);
            var load = opportunity.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.LoadTime);
            var unload = opportunity.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.UnloadTime);

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

            var totalMinutes =
                cycle.Value
                + (load ?? SchemaConstants.Freight.DefaultLoadTimeMinutes)
                + (unload ?? SchemaConstants.Freight.DefaultUnloadTimeMinutes);

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
                [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = null
            };
            ctx.Service.Update(update);
            ctx.Tracing.Trace("Delivery prices cleared on QuoteDetail {0}.", quoteDetailId);
        }
    }
}
