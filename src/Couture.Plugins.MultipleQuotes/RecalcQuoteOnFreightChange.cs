using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// When freight inputs change on a Quote (shipping rate, cycle time,
    /// load time, unload time), this plugin recalculates the cached
    /// trailer / straight-truck per-ton rates + total trip minutes on
    /// the same Quote, then re-fires every child Quote Product so the
    /// delivered prices and tax pick up the new values.
    ///
    /// Replaces the old RecalcDeliveryOnProjectChange that cascaded
    /// project freight changes across all sibling quotes – freight is
    /// now per-quote, so the cascade lives at the Quote level.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Filter attributes: eb_shippingrateperhour, eb_cycletime,
    ///                      eb_loadtime, eb_unloadtime
    ///   PostImage "PostImage" with columns:
    ///     eb_shippingrateperhour, eb_cycletime, eb_loadtime, eb_unloadtime
    public sealed class RecalcQuoteOnFreightChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("RecalcQuoteOnFreightChange started. Depth={0}",
                ctx.Execution.Depth);

            // We write back to the same Quote (trailerrateton etc.) which
            // re-fires this plugin. The filter attributes prevent that
            // (we write rate fields, not input fields), but depth guard
            // is the safety net.
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

            var quoteId = target.Id;
            var quote = ResolveQuote(ctx, quoteId);

            // ── Read freight inputs ──────────────────────────────────────
            var shipping = quote.GetAttributeValue<Money>(
                SchemaConstants.Quote.ShippingRatePerHour);
            var cycle = quote.GetAttributeValue<int?>(
                SchemaConstants.Quote.CycleTime);
            var load = quote.GetAttributeValue<int?>(
                SchemaConstants.Quote.LoadTime);
            var unload = quote.GetAttributeValue<int?>(
                SchemaConstants.Quote.UnloadTime);

            if (shipping == null || shipping.Value <= 0
                || cycle == null || cycle.Value <= 0)
            {
                ctx.Tracing.Trace("Shipping rate or cycle time missing – " +
                    "clearing cached rates on the Quote.");

                var clear = new Entity(SchemaConstants.Entities.Quote, quoteId)
                {
                    [SchemaConstants.Quote.TrailerRatePerTon] = null,
                    [SchemaConstants.Quote.StraightTruckRatePerTon] = null,
                    [SchemaConstants.Quote.TotalTripMinutes] = null
                };
                ctx.Service.Update(clear);
                return;
            }

            var totalMinutes =
                cycle.Value
                + (load ?? SchemaConstants.Freight.DefaultLoadTimeMinutes)
                + (unload ?? SchemaConstants.Freight.DefaultUnloadTimeMinutes);

            var costPerTrip =
                (shipping.Value / SchemaConstants.Freight.MinutesPerHour) * totalMinutes;
            var trailerRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.TrailerTonsPerLoad, 2);
            var straightRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.StraightTruckTonsPerLoad, 2);

            ctx.Tracing.Trace(
                "Calculated – TotalMinutes={0}, Trailer={1}/ton, Straight={2}/ton",
                totalMinutes, trailerRate, straightRate);

            // ── Update the Quote with calculated rates ───────────────────
            var updateQuote = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.TrailerRatePerTon] = new Money(trailerRate),
                [SchemaConstants.Quote.StraightTruckRatePerTon] = new Money(straightRate),
                [SchemaConstants.Quote.TotalTripMinutes] = totalMinutes
            };
            ctx.Service.Update(updateQuote);

            // ── Touch each child line to re-fire delivered-price calc ────
            // We don't recompute prices inline – we write quantity back to
            // its current value so CalculateDeliveryPricingPlugin's
            // filter trips and re-runs with the new freight rates. That
            // cascades into tax + rollup automatically.
            var lines = ctx.Service.RetrieveMultiple(
                new QueryExpression(SchemaConstants.Entities.QuoteDetail)
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
                });

            ctx.Tracing.Trace("Re-firing {0} quote product line(s).",
                lines.Entities.Count);

            foreach (var line in lines.Entities)
            {
                var qty = line.GetAttributeValue<decimal>(
                    SchemaConstants.QuoteDetail.Quantity);
                ctx.Service.Update(new Entity(
                    SchemaConstants.Entities.QuoteDetail, line.Id)
                {
                    [SchemaConstants.QuoteDetail.Quantity] = qty
                });
            }
        }

        /// Uses the PostImage if registered; otherwise retrieves the
        /// Quote directly so we have the full set of freight inputs.
        private static Entity ResolveQuote(PluginContext ctx, Guid quoteId)
        {
            if (ctx.Execution.PostEntityImages.Contains("PostImage"))
            {
                ctx.Tracing.Trace("Using PostImage for Quote freight fields.");
                return ctx.Execution.PostEntityImages["PostImage"];
            }

            ctx.Tracing.Trace("PostImage not registered – retrieving Quote.");
            return ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteId,
                new ColumnSet(
                    SchemaConstants.Quote.ShippingRatePerHour,
                    SchemaConstants.Quote.CycleTime,
                    SchemaConstants.Quote.LoadTime,
                    SchemaConstants.Quote.UnloadTime));
        }
    }
}
