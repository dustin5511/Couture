using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Recalculates the cached trailer / straight-truck per-ton rates +
    /// total trip minutes on a Project (Opportunity) when its freight
    /// inputs change. This is a preview only — the rates are NOT
    /// cascaded to existing quotes. They feed new quotes via
    /// ValidateFreightBeforeQuoteCreate's seed at quote Create.
    ///
    /// Load and unload are hard-coded to 10 minutes each (matches the
    /// JS-enforced form values and the Quote-side recalc).
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=opportunity, Stage=PostOperation (40)
    ///   Filter attributes: eb_shippingrateperhour, eb_cycletime
    ///   PostImage "PostImage" with columns:
    ///     eb_shippingrateperhour, eb_cycletime
    public sealed class RecalcProjectFreightChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("RecalcProjectFreightChange started. Depth={0}",
                ctx.Execution.Depth);

            // Our own write of the rate fields would re-fire this plugin
            // at depth 2 — the filter attrs don't include those fields,
            // so this is belt-and-braces.
            if (ctx.Execution.Depth > 1)
            {
                ctx.Tracing.Trace("Depth > 1 – exiting to prevent recursion.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            var oppId = target.Id;
            var opp = ResolveOpportunity(ctx, oppId);

            var shipping = opp.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.ShippingRatePerHour);
            var cycle = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.CycleTime);

            if (shipping == null || shipping.Value <= 0
                || cycle == null || cycle.Value <= 0)
            {
                ctx.Tracing.Trace("Shipping rate or cycle time missing – clearing cached rates.");
                var clear = new Entity(SchemaConstants.Entities.Opportunity, oppId)
                {
                    [SchemaConstants.Opportunity.TrailerRatePerTon] = null,
                    [SchemaConstants.Opportunity.StraightTruckRatePerTon] = null,
                    [SchemaConstants.Opportunity.TotalTripMinutes] = null
                };
                ctx.Service.Update(clear);
                return;
            }

            // Load and unload are hard-coded to 10 minutes each (Kraemer's
            // requirement). The plugin always uses the constants rather
            // than reading from the entity, so back-channel writes to
            // those columns can't break the math.
            var totalMinutes =
                cycle.Value
                + SchemaConstants.Freight.DefaultLoadTimeMinutes
                + SchemaConstants.Freight.DefaultUnloadTimeMinutes;

            var costPerTrip =
                (shipping.Value / SchemaConstants.Freight.MinutesPerHour) * totalMinutes;
            var trailerRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.TrailerTonsPerLoad, 2);
            var straightRate = Math.Round(
                costPerTrip / SchemaConstants.Freight.StraightTruckTonsPerLoad, 2);

            ctx.Tracing.Trace(
                "Calculated – TotalMinutes={0}, Trailer={1}/ton, Straight={2}/ton",
                totalMinutes, trailerRate, straightRate);

            var update = new Entity(SchemaConstants.Entities.Opportunity, oppId)
            {
                [SchemaConstants.Opportunity.TrailerRatePerTon] = new Money(trailerRate),
                [SchemaConstants.Opportunity.StraightTruckRatePerTon] = new Money(straightRate),
                [SchemaConstants.Opportunity.TotalTripMinutes] = totalMinutes
            };
            ctx.Service.Update(update);
        }

        private static Entity ResolveOpportunity(PluginContext ctx, Guid oppId)
        {
            if (ctx.Execution.PostEntityImages.Contains("PostImage"))
            {
                ctx.Tracing.Trace("Using PostImage for Opportunity freight fields.");
                return ctx.Execution.PostEntityImages["PostImage"];
            }

            ctx.Tracing.Trace("PostImage not registered – retrieving Opportunity.");
            return ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppId,
                new ColumnSet(
                    SchemaConstants.Opportunity.ShippingRatePerHour,
                    SchemaConstants.Opportunity.CycleTime));
        }
    }
}
