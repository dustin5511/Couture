using System;
using System.Collections.Generic;
using System.Text;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Prevents creation of a Quote when the parent Project (Opportunity)
    /// is missing any of the inputs the rest of the pricing pipeline
    /// depends on. Three buckets of checks, each surfaced as a single
    /// readable error message:
    ///
    ///   Freight calculation – shipping rate, cycle time, load time,
    ///                          unload time (drives delivered pricing).
    ///   Tax inputs          – jobsite ZIP, delivery preference
    ///                          (drives tax-rate lookup and which totals
    ///                          tax applies to).
    ///
    /// On success the plugin also:
    ///   • copies eb_deliverypreference from the parent Project onto the
    ///     new Quote (seed only — the user can change it on the quote
    ///     afterward and downstream pricing reads from the quote field), and
    ///   • stamps eb_taxratepercent on the target row by looking up the
    ///     rate from eb_taxrate, so the rate is visible on the printed
    ///     quote even before any line items exist.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quote, Stage=PreValidation (10),
    ///   Synchronous, Execution Order = 1.
    public sealed class ValidateFreightBeforeQuoteCreate : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("ValidateFreightBeforeQuoteCreate started.");

            if (ctx.Execution.Depth > 2)
            {
                ctx.Tracing.Trace("Depth > 2 – exiting.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            var oppRef = target.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.OpportunityId);
            if (oppRef == null)
            {
                // Quote created without a parent Project — allow it. Plugins
                // downstream that need the opportunity (delivery pricing,
                // tax) bail out cleanly when there isn't one.
                ctx.Tracing.Trace("No parent Project on Quote – skipping validation.");
                return;
            }

            ctx.Tracing.Trace("Parent Project ID: {0}", oppRef.Id);

            var opp = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppRef.Id,
                new ColumnSet(
                    SchemaConstants.Opportunity.ShippingRatePerHour,
                    SchemaConstants.Opportunity.CycleTime,
                    SchemaConstants.Opportunity.LoadTime,
                    SchemaConstants.Opportunity.UnloadTime,
                    SchemaConstants.Opportunity.JobsiteZip,
                    SchemaConstants.Opportunity.DeliveryPreference));

            var missing = new List<string>();

            // ── Freight inputs ──────────────────────────────────────────
            var shippingRate = opp.GetAttributeValue<Money>(
                SchemaConstants.Opportunity.ShippingRatePerHour);
            if (shippingRate == null || shippingRate.Value <= 0)
                missing.Add("Shipping Rate ($/hr)");

            var cycleTime = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.CycleTime);
            if (cycleTime == null || cycleTime.Value <= 0)
                missing.Add("Cycle Time (minutes)");

            var loadTime = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.LoadTime);
            if (loadTime == null || loadTime.Value <= 0)
                missing.Add("Load Time (minutes)");

            var unloadTime = opp.GetAttributeValue<int?>(
                SchemaConstants.Opportunity.UnloadTime);
            if (unloadTime == null || unloadTime.Value <= 0)
                missing.Add("Unload Time (minutes)");

            // ── Tax inputs ─────────────────────────────────────────────
            var jobsiteZip = opp.GetAttributeValue<string>(
                SchemaConstants.Opportunity.JobsiteZip);
            if (string.IsNullOrWhiteSpace(jobsiteZip))
                missing.Add("Delivery ZIP Code");

            var deliveryPreference = opp.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Opportunity.DeliveryPreference);
            if (deliveryPreference == null)
                missing.Add("Delivery Preference");

            if (missing.Count > 0)
            {
                var message = new StringBuilder();
                message.AppendLine(
                    "Cannot create a Quote — the following fields are missing on the Project:");
                message.AppendLine();
                foreach (var field in missing) message.AppendLine("  •  " + field);
                message.AppendLine();
                message.AppendLine(
                    "Please go back to the Project record and fill in the " +
                    "Freight / Delivery Calculation section before creating a Quote.");

                ctx.Tracing.Trace("Validation FAILED – missing: {0}",
                    string.Join(", ", missing));
                throw new InvalidPluginExecutionException(message.ToString());
            }

            ctx.Tracing.Trace("Validation PASSED – all required fields populated.");

            // ── Seed delivery preference on the new quote ───────────────
            // Pre-Validation runs before the platform writes the row, so
            // mutating the Target persists in the initial insert. The
            // user is free to change this on the quote afterward; every
            // downstream calc reads from the quote, not the project.
            if (!target.Contains(SchemaConstants.Quote.DeliveryPreference))
            {
                target[SchemaConstants.Quote.DeliveryPreference] =
                    new OptionSetValue(deliveryPreference.Value);
                ctx.Tracing.Trace("Seeded Quote.eb_deliverypreference = {0} from Project.",
                    deliveryPreference.Value);
            }

            // ── Stamp tax rate on the new quote ─────────────────────────
            // Pre-Validation runs before the platform writes the row, so
            // mutating the Target stores the value in the initial insert
            // with no extra Update call. A missing rate (zero match) is
            // non-fatal here; CalculateTaxPlugin will fail loudly when the
            // first quote detail tries to compute tax.
            var taxService = new TaxRateService(ctx.Service, ctx.Tracing);
            var rate = taxService.LookupCombinedRate(
                SchemaConstants.TaxRate.MinnesotaStateName, jobsiteZip);
            if (rate.HasValue && rate.Value > 0)
            {
                target[SchemaConstants.Quote.AppliedTaxRatePercent] =
                    Math.Round(rate.Value * 100m, 4);
                ctx.Tracing.Trace("Stamped tax rate {0}% on new quote.",
                    target[SchemaConstants.Quote.AppliedTaxRatePercent]);
            }
            else
            {
                ctx.Tracing.Trace(
                    "No matching eb_taxrate row for ZIP '{0}' – leaving rate unset on the new quote.",
                    jobsiteZip);
            }
        }
    }
}
