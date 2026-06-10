using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Drives the per-quote freight cascade. Two scenarios it handles:
    ///
    ///   • User changes a raw input (shipping rate, cycle time, load
    ///     time, unload time) → recalculate the cached trailer /
    ///     straight-truck per-ton rates + total trip minutes on the
    ///     Quote, then re-fire every child Quote Product so delivered
    ///     prices and tax pick up the new values.
    ///
    ///   • User manually overrides a calculated rate (eb_trailerrateton
    ///     or eb_straighttruckrateton) → skip the recalc so the manual
    ///     value is preserved, then re-fire every child line so
    ///     delivered prices reflect the manual rate.
    ///
    /// Replaces the old RecalcDeliveryOnProjectChange that cascaded
    /// project freight changes across all sibling quotes – freight is
    /// now per-quote, so the cascade lives at the Quote level.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Filter attributes: eb_shippingrateperhour, eb_cycletime,
    ///                      eb_loadtime, eb_unloadtime,
    ///                      eb_trailerrateton, eb_straighttruckrateton
    ///   PostImage "PostImage" with columns:
    ///     eb_shippingrateperhour, eb_cycletime, eb_loadtime, eb_unloadtime
    public sealed class RecalcQuoteOnFreightChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("RecalcQuoteOnFreightChange started. Depth={0}",
                ctx.Execution.Depth);

            // Self-recursion guard. When this plugin runs at depth 1 and
            // recalculates rates, it writes them back to the Quote – that
            // write re-fires this plugin at depth 2 with the rate fields
            // in Target. We've already done the cascade in the depth-1
            // pass, so bail to avoid duplicate work and an infinite loop.
            if (ctx.Execution.Depth > 1)
            {
                ctx.Tracing.Trace("Depth > 1 – assuming self-triggered re-fire and exiting.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            var quoteId = target.Id;

            // Decide which branch to take based on what's in Target.
            // Raw inputs win – if any are present, we recalculate rates
            // (the user could legitimately change a raw input AND a rate
            // in the same save; raw inputs override).
            var rawInputChanged =
                target.Contains(SchemaConstants.Quote.ShippingRatePerHour)
                || target.Contains(SchemaConstants.Quote.CycleTime)
                || target.Contains(SchemaConstants.Quote.LoadTime)
                || target.Contains(SchemaConstants.Quote.UnloadTime);

            if (rawInputChanged)
            {
                RecalculateRatesAndCascade(ctx, quoteId);
            }
            else
            {
                ctx.Tracing.Trace(
                    "Only rate fields in Target – preserving manual override, " +
                    "cascading to line items.");
                CascadeToLines(ctx, quoteId);
            }
        }

        private static void RecalculateRatesAndCascade(PluginContext ctx, Guid quoteId)
        {
            var quote = ResolveQuote(ctx, quoteId);

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
                CascadeToLines(ctx, quoteId);
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

            var updateQuote = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.TrailerRatePerTon] = new Money(trailerRate),
                [SchemaConstants.Quote.StraightTruckRatePerTon] = new Money(straightRate),
                [SchemaConstants.Quote.TotalTripMinutes] = totalMinutes
            };
            ctx.Service.Update(updateQuote);

            CascadeToLines(ctx, quoteId);
        }

        /// Touches each child Quote Product so CalculateDeliveryPricingPlugin
        /// re-fires with the latest freight rates on the parent Quote.
        /// Writing the line's current Quantity back to itself is the
        /// cheapest way to trip the downstream filter without
        /// duplicating its math here.
        private static void CascadeToLines(PluginContext ctx, Guid quoteId)
        {
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
