using System;
using System.Collections.Generic;
using System.Linq;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// When freight calculation fields are updated on a Project (Opportunity)
    /// — shipping rate, cycle time, load time, or unload time — this plugin
    /// recalculates the delivery rates on the Project and then cascades the
    /// new rates to ALL Quote Products across ALL active Quotes under that
    /// Project.
    ///
    /// For each Quote Product it recalculates:
    ///   Delivered Price/Ton (Trailer and Straight Truck)
    ///   Extended Delivered amounts (Quantity * Delivered Price/Ton)
    ///
    /// After cascading, it rolls up the extended amounts to each parent
    /// Quote so the quote-level totals stay in sync.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=opportunity, Stage=PostOperation (40)
    ///   Filter attributes: eb_shippingrateperhour, eb_cycletime,
    ///                      eb_loadtime, eb_unloadtime
    ///   PostImage "PostImage" with columns:
    ///     eb_shippingrateperhour, eb_cycletime, eb_loadtime, eb_unloadtime
    public sealed class RecalcDeliveryOnProjectChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("RecalcDeliveryOnProjectChange started. Depth={0}",
                ctx.Execution.Depth);

            // This plugin writes back to the Opportunity (calculated rates)
            // which will re-trigger itself. The filtering attributes should
            // prevent that (we write trailer/straight rate, not the input
            // fields), but the depth guard is here as a safety net.
            if (ctx.Execution.Depth > 3)
            {
                ctx.Tracing.Trace("Depth > 3 – exiting to prevent recursion.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            var opportunityId = target.Id;
            var opp = ResolveOpportunity(ctx, opportunityId);

            // ── Read freight inputs ──────────────────────────────────────
            var shipping = opp.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.ShippingRatePerHour);
            var cycle = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.CycleTime);
            var load = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.LoadTime);
            var unload = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.UnloadTime);

            if (shipping == null || shipping.Value <= 0
                || cycle == null || cycle.Value <= 0)
            {
                ctx.Tracing.Trace("Shipping Rate or Cycle Time missing – " +
                    "clearing rates on Project.");

                var clearOpp = new Entity(
                    SchemaConstants.Entities.Opportunity, opportunityId)
                {
                    [SchemaConstants.Opportunity.TrailerRatePerTon] = null,
                    [SchemaConstants.Opportunity.StraightTruckRatePerTon] = null,
                    [SchemaConstants.Opportunity.TotalTripMinutes] = null
                };
                ctx.Service.Update(clearOpp);
                return;
            }

            var totalMinutes =
                cycle.Value
                + (load ?? SchemaConstants.Freight.DefaultLoadTimeMinutes)
                + (unload ?? SchemaConstants.Freight.DefaultUnloadTimeMinutes);

            var costPerTrip =
                (shipping.Value / SchemaConstants.Freight.MinutesPerHour)
                * totalMinutes;
            var trailerRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.TrailerTonsPerLoad, 2);
            var straightRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.StraightTruckTonsPerLoad, 2);

            ctx.Tracing.Trace(
                "Calculated – TotalMinutes={0}, Trailer={1}/ton, Straight={2}/ton",
                totalMinutes, trailerRate, straightRate);

            // ── Update the Project with calculated rates ─────────────────
            var updateOpp = new Entity(
                SchemaConstants.Entities.Opportunity, opportunityId)
            {
                [SchemaConstants.Opportunity.TrailerRatePerTon] = new Money(trailerRate),
                [SchemaConstants.Opportunity.StraightTruckRatePerTon] = new Money(straightRate),
                [SchemaConstants.Opportunity.TotalTripMinutes] = totalMinutes
            };
            ctx.Service.Update(updateOpp);

            ctx.Tracing.Trace("Project rates updated. Cascading to quote products...");

            // ── Find all active Quotes under this Project ────────────────
            var quoteQuery = new QueryExpression(SchemaConstants.Entities.Quote)
            {
                ColumnSet = new ColumnSet(SchemaConstants.Quote.QuoteId),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SchemaConstants.Quote.OpportunityId,
                            ConditionOperator.Equal, opportunityId),
                        // statecode 0 = Draft/Active
                        new ConditionExpression(
                            SchemaConstants.Quote.StateCode,
                            ConditionOperator.Equal, 0)
                    }
                }
            };

            var quotes = ctx.Service.RetrieveMultiple(quoteQuery);
            ctx.Tracing.Trace("Found {0} active quote(s).", quotes.Entities.Count);

            if (quotes.Entities.Count == 0)
            {
                ctx.Tracing.Trace("No active quotes – nothing to cascade.");
                return;
            }

            var quoteIds = quotes.Entities.Select(q => q.Id).ToList();

            // ── Find all Quote Products across those Quotes ──────────────
            var qdQuery = new QueryExpression(SchemaConstants.Entities.QuoteDetail)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.QuoteDetail.PricePerUnit,
                    SchemaConstants.QuoteDetail.Quantity,
                    SchemaConstants.QuoteDetail.IsIncomingMaterial,
                    SchemaConstants.QuoteDetail.QuoteId),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SchemaConstants.QuoteDetail.QuoteId,
                            ConditionOperator.In, quoteIds.ToArray())
                    }
                }
            };

            var quoteDetails = ctx.Service.RetrieveMultiple(qdQuery);
            ctx.Tracing.Trace("Found {0} quote product(s) to recalculate.",
                quoteDetails.Entities.Count);

            // ── Recalculate each Quote Product ───────────────────────────
            var updated = 0;
            var skipped = 0;

            foreach (var qd in quoteDetails.Entities)
            {
                var isIncoming = qd.GetAttributeValue<bool>(
                    SchemaConstants.QuoteDetail.IsIncomingMaterial);

                if (isIncoming)
                {
                    var clearQd = new Entity(
                        SchemaConstants.Entities.QuoteDetail, qd.Id)
                    {
                        [SchemaConstants.QuoteDetail.DeliveredPriceTrailer] = null,
                        [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = null,
                        [SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer] = null,
                        [SchemaConstants.QuoteDetail.ExtendedDeliveredStraight] = null
                    };
                    ctx.Service.Update(clearQd);
                    skipped++;
                    continue;
                }

                var ppu = qd.GetAttributeValue<Money>(
                    SchemaConstants.QuoteDetail.PricePerUnit);
                if (ppu == null || ppu.Value <= 0)
                {
                    skipped++;
                    continue;
                }

                var qty = qd.GetAttributeValue<decimal?>(
                    SchemaConstants.QuoteDetail.Quantity) ?? 0m;

                var delTrailer = Math.Round(ppu.Value + trailerRate, 2);
                var delStraight = Math.Round(ppu.Value + straightRate, 2);
                var extTrailer = Math.Round(qty * delTrailer, 2);
                var extStraight = Math.Round(qty * delStraight, 2);

                var updateQd = new Entity(
                    SchemaConstants.Entities.QuoteDetail, qd.Id)
                {
                    [SchemaConstants.QuoteDetail.DeliveredPriceTrailer] = new Money(delTrailer),
                    [SchemaConstants.QuoteDetail.DeliveredPriceStraight] = new Money(delStraight),
                    [SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer] = new Money(extTrailer),
                    [SchemaConstants.QuoteDetail.ExtendedDeliveredStraight] = new Money(extStraight)
                };
                ctx.Service.Update(updateQd);
                updated++;
            }

            ctx.Tracing.Trace("Cascade complete – {0} updated, {1} skipped.",
                updated, skipped);

            // ── Roll up totals to each Quote ─────────────────────────────
            ctx.Tracing.Trace("Rolling up totals to {0} quote(s)...",
                quotes.Entities.Count);

            foreach (var q in quotes.Entities)
            {
                RollUpQuoteTotals(ctx, q.Id);
            }

            ctx.Tracing.Trace("RecalcDeliveryOnProjectChange completed.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// Uses the PostImage if registered; otherwise retrieves the
        /// Opportunity directly so we have the full set of freight inputs.
        private static Entity ResolveOpportunity(
            PluginContext ctx, Guid opportunityId)
        {
            if (ctx.Execution.PostEntityImages.Contains("PostImage"))
            {
                ctx.Tracing.Trace("Using PostImage for Opportunity fields.");
                return ctx.Execution.PostEntityImages["PostImage"];
            }

            ctx.Tracing.Trace("PostImage not registered – retrieving Opportunity.");
            return ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                opportunityId,
                new ColumnSet(
                    SchemaConstants.Opportunity.ShippingRatePerHour,
                    SchemaConstants.Opportunity.CycleTime,
                    SchemaConstants.Opportunity.LoadTime,
                    SchemaConstants.Opportunity.UnloadTime));
        }

        /// Full 5-field rollup: FOB total, delivered totals (trailer/
        /// straight), and tax totals (trailer/straight). Mirrors the
        /// rollup in CalculateDeliveryPricingPlugin so the two code
        /// paths converge on identical Quote-level numbers regardless of
        /// whether the trigger was a quotedetail edit or an opportunity
        /// freight change.
        private static void RollUpQuoteTotals(PluginContext ctx, Guid quoteId)
        {
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
                            ConditionOperator.Equal, quoteId)
                    }
                }
            };

            var lines = ctx.Service.RetrieveMultiple(query);

            var fobTotal = 0m;
            var totalTrailer = 0m;
            var totalStraight = 0m;
            var taxTotalTrailer = 0m;
            var taxTotalStraight = 0m;

            foreach (var line in lines.Entities)
            {
                var ppu = line.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.PricePerUnit);
                var qty = line.GetAttributeValue<decimal>(SchemaConstants.QuoteDetail.Quantity);
                var extT = line.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.ExtendedDeliveredTrailer);
                var extS = line.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.ExtendedDeliveredStraight);
                var taxT = line.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.TaxAmountTrailer);
                var taxS = line.GetAttributeValue<Money>(SchemaConstants.QuoteDetail.TaxAmountStraight);

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

            var updateQuote = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.FobTotal] = new Money(fobTotal),
                [SchemaConstants.Quote.TotalDeliveredTrailer] = new Money(totalTrailer),
                [SchemaConstants.Quote.TotalDeliveredStraight] = new Money(totalStraight),
                [SchemaConstants.Quote.TaxTotalTrailer] = new Money(taxTotalTrailer),
                [SchemaConstants.Quote.TaxTotalStraight] = new Money(taxTotalStraight)
            };
            ctx.Service.Update(updateQuote);

            ctx.Tracing.Trace(
                "Quote {0} totals – FOB={1}, DelTrailer={2}, DelStraight={3}, TaxT={4}, TaxS={5}",
                quoteId, fobTotal, totalTrailer, totalStraight,
                taxTotalTrailer, taxTotalStraight);
        }

        private static bool ShouldIncludeTaxInDeliveredTotal(PluginContext ctx, Guid quoteId)
        {
            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteId,
                new ColumnSet(SchemaConstants.Quote.OpportunityId));
            var oppRef = quote.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.OpportunityId);
            if (oppRef == null) return false;

            var opp = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppRef.Id,
                new ColumnSet(SchemaConstants.Opportunity.DeliveryPreference));
            var preference = opp.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Opportunity.DeliveryPreference);
            if (preference == null) return false;

            return preference.Value == SchemaConstants.DeliveryPreference.Delivery
                || preference.Value == SchemaConstants.DeliveryPreference.FobAndDelivery;
        }
    }
}