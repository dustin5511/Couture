using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Blocks Quote creation until the parent Project (Opportunity) has the
    /// inputs the rest of the pricing pipeline depends on:
    ///   * Delivery ZIP code (eb_jobsitezip) – feeds tax-rate lookup.
    ///   * Delivery Preference (eb_deliverypreference) – drives whether tax
    ///     is calculated and which totals it applies to.
    /// Throws InvalidPluginExecutionException so the platform surfaces a
    /// readable message to the user instead of a generic plugin failure.
    ///
    /// On success the plugin also stamps the resolved tax rate (as a
    /// percentage) on the target Quote so the field is populated before
    /// any line items exist – the printed quote can then show the rate
    /// even on an empty quote.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quote, Stage=PreOperation (20)
    public sealed class QuoteCreateValidationPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("QuoteCreateValidation started.");

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target entity – exiting.");
                return;
            }

            var oppRef = target.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.OpportunityId);
            if (oppRef == null)
            {
                // Quotes without a parent opportunity can't be validated and
                // are unusual in this org; let the platform surface its own
                // error rather than blocking here.
                ctx.Tracing.Trace("Quote has no parent opportunity – nothing to validate.");
                return;
            }

            var opportunity = ctx.Service.Retrieve(
                SchemaConstants.Entities.Opportunity,
                oppRef.Id,
                new ColumnSet(
                    SchemaConstants.Opportunity.JobsiteZip,
                    SchemaConstants.Opportunity.DeliveryPreference));

            var zip = opportunity.GetAttributeValue<string>(
                SchemaConstants.Opportunity.JobsiteZip);
            if (string.IsNullOrWhiteSpace(zip))
            {
                throw new InvalidPluginExecutionException(
                    "Please set the Delivery ZIP Code on the Project before creating a Quote.");
            }

            var preference = opportunity.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Opportunity.DeliveryPreference);
            if (preference == null)
            {
                throw new InvalidPluginExecutionException(
                    "Please set the Delivery Preference on the Project before creating a Quote.");
            }

            ctx.Tracing.Trace(
                "QuoteCreateValidation passed. JobsiteZip='{0}', DeliveryPreference={1}.",
                zip, preference.Value);

            // PreOperation mutation: stamping fields on the target writes
            // them to the row the platform is about to insert, so no extra
            // Update is needed. A missing rate (zero) is non-fatal here –
            // CalculateTaxPlugin will fail loudly when the first quote
            // detail tries to compute tax.
            var taxService = new TaxRateService(ctx.Service, ctx.Tracing);
            var rate = taxService.LookupCombinedRate(
                SchemaConstants.TaxRate.MinnesotaStateName, zip);
            if (rate.HasValue && rate.Value > 0)
            {
                target[SchemaConstants.Quote.AppliedTaxRatePercent] =
                    Math.Round(rate.Value * 100m, 4);
                ctx.Tracing.Trace("Stamped applied tax rate {0}% on new quote.",
                    target[SchemaConstants.Quote.AppliedTaxRatePercent]);
            }
        }
    }
}
