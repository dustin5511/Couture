using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Couture.Plugins.MultipleQuotes.Helpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// Populates the per-line tax fields and the with-tax unit prices on
    /// a quote product based on the parent Quote's Delivery Preference
    /// and the Project's Jobsite ZIP:
    ///
    ///   FOB only (1)             → tax = 0, unit-with-tax = 0 on both
    ///                              trailer and straight fields.
    ///   Delivery (2)             → tax = deliveredprice * qty * rate
    ///                              unit-with-tax = deliveredprice * (1+rate)
    ///                              (all zero if it is incoming material).
    ///   FOB and Delivery (3)     → same as Delivery; only the
    ///                              QuoteTotalsRollupPlugin decides which
    ///                              header total the tax lands in.
    ///
    /// Four fields per line are stored: two tax dollar amounts (qty-
    /// dependent, drive the header tax totals) and two with-tax unit
    /// prices per ton (qty-independent, customer-facing numbers shown
    /// in the quote-line grid and on the printed Word template).
    /// Trailer and straight-truck are separate columns because their
    /// delivered prices differ (different freight components) and MN
    /// taxes the full delivered price including freight.
    ///
    /// Tax rate is resolved by stripping non-digits from the quote's
    /// shipto_postalcode (falling back to the project's eb_jobsitezip if
    /// the quote's is blank) and querying eb_taxrate for the row whose
    /// eb_zip begins with that prefix in state "MN". See TaxRateService.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///   Message=Update, PrimaryEntity=quotedetail, Stage=PostOperation (40)
    ///     Filter attributes: priceperunit, quantity, eb_isincomingmaterial,
    ///                        eb_deliveredpricetrailer, eb_deliveredpricestraight
    ///   PostImage "PostImage" with columns:
    ///     priceperunit, quantity, eb_isincomingmaterial, quoteid,
    ///     eb_deliveredpricetrailer, eb_deliveredpricestraight
    ///
    /// Rank this step AFTER CalculateDeliveryPricingPlugin so the delivered
    /// prices are already on the record by the time we compute tax.
    public sealed class CalculateTaxPlugin : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("CalculateTax started. Message={0}, Depth={1}",
                ctx.Execution.MessageName, ctx.Execution.Depth);

            // Our own write to the tax fields will re-trigger this plugin
            // because we can't list those fields in the filter (they're
            // outputs, not inputs). A depth guard is cheap insurance.
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

            var quoteRef = record.GetAttributeValue<EntityReference>(
                SchemaConstants.QuoteDetail.QuoteId);
            if (quoteRef == null)
            {
                ctx.Tracing.Trace("No parent Quote on QuoteDetail – exiting.");
                return;
            }

            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                quoteRef.Id,
                new ColumnSet(
                    SchemaConstants.Quote.OpportunityId,
                    SchemaConstants.Quote.DeliveryPreference,
                    SchemaConstants.Quote.ShipToPostalCode));

            // Delivery preference lives on the quote (seeded from the
            // project at Create, then editable per-quote). The ZIP comes
            // from the quote's ship-to as well – the project may be FOB
            // with no ZIP while this particular quote is Delivery with a
            // job site entered directly on the quote.
            var preference = quote.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Quote.DeliveryPreference);
            var preferenceValue = preference?.Value ?? -1;

            var isIncoming = record.GetAttributeValue<bool>(
                SchemaConstants.QuoteDetail.IsIncomingMaterial);

            // FOB-only and incoming-material lines never owe tax in any
            // scenario; short-circuit before touching the rate table.
            if (preferenceValue == SchemaConstants.DeliveryPreference.FOB || isIncoming)
            {
                ctx.Tracing.Trace(
                    "FOB-only or incoming material – clearing tax + unit-with-tax. " +
                    "Preference={0}, IsIncoming={1}",
                    preferenceValue, isIncoming);
                WriteTaxAndUnitPrices(ctx, target.Id, 0m, 0m, 0m, 0m);
                return;
            }

            var deliveredTrailer = record.GetAttributeValue<Money>(
                SchemaConstants.QuoteDetail.DeliveredPriceTrailer);
            var deliveredStraight = record.GetAttributeValue<Money>(
                SchemaConstants.QuoteDetail.DeliveredPriceStraight);

            // Prefer the quote's own ship-to ZIP; fall back to the
            // project's job-site ZIP for quotes created before the
            // ship-to seed existed (or where the user cleared it).
            var rawZip = quote.GetAttributeValue<string>(
                SchemaConstants.Quote.ShipToPostalCode);
            if (string.IsNullOrWhiteSpace(rawZip))
            {
                var oppRef = quote.GetAttributeValue<EntityReference>(
                    SchemaConstants.Quote.OpportunityId);
                if (oppRef != null)
                {
                    var opportunity = ctx.Service.Retrieve(
                        SchemaConstants.Entities.Opportunity,
                        oppRef.Id,
                        new ColumnSet(SchemaConstants.Opportunity.JobsiteZip));
                    rawZip = opportunity.GetAttributeValue<string>(
                        SchemaConstants.Opportunity.JobsiteZip);
                    ctx.Tracing.Trace(
                        "Quote ship-to ZIP blank – fell back to Project ZIP '{0}'.",
                        rawZip);
                }
            }

            var taxService = new TaxRateService(ctx.Service, ctx.Tracing);
            var rate = taxService.LookupCombinedRate(
                SchemaConstants.TaxRate.MinnesotaStateName, rawZip);

            if (rate == null || rate.Value <= 0)
            {
                // No matching rate row. Throw so the calculation gap is
                // visible rather than silently storing $0 tax on what could
                // be a $100k order.
                throw new InvalidPluginExecutionException(
                    $"No tax rate found for ZIP '{rawZip}'. Delivery and " +
                    "FOB & Delivery quotes need a Ship To ZIP/Postal Code in " +
                    "the Job Site section of the quote (or a Jobsite ZIP on " +
                    "the Project). Also check the eb_taxrate table has a row " +
                    "for that ZIP in state 'MN'.");
            }

            // Unit prices with tax don't depend on quantity, so compute
            // them before the qty bail-out – the customer-facing
            // /ton price is meaningful even when qty is still 0 on a
            // freshly added line.
            var unitTrailerWithTax = ComputeUnitWithTax(deliveredTrailer, rate.Value);
            var unitStraightWithTax = ComputeUnitWithTax(deliveredStraight, rate.Value);

            var quantity = record.GetAttributeValue<decimal>(
                SchemaConstants.QuoteDetail.Quantity);

            // eb_rate is a fraction (e.g. 0.081250 = 8.125%); multiply
            // straight against the dollar amount. ComputeLineTax returns
            // 0 for zero qty so we don't need a separate branch.
            var taxTrailer = ComputeLineTax(deliveredTrailer, quantity, rate.Value);
            var taxStraight = ComputeLineTax(deliveredStraight, quantity, rate.Value);

            ctx.Tracing.Trace(
                "Tax line totals: trailer={0}, straight={1}; unit-with-tax: " +
                "trailer={2}, straight={3}; rate={4}, qty={5}",
                taxTrailer, taxStraight, unitTrailerWithTax, unitStraightWithTax,
                rate, quantity);

            WriteTaxAndUnitPrices(ctx, target.Id,
                taxTrailer, taxStraight,
                unitTrailerWithTax, unitStraightWithTax);

            // Keep the rate stamped on the parent Quote in sync. We re-write
            // every time rather than guarding because the opportunity's ZIP
            // can change after the quote was created, or the rate table can
            // refresh under us. Stored as a percentage (× 100) so the
            // printed quote can show "{value}%" with no formatting work.
            WriteAppliedRateToQuote(ctx, quoteRef.Id, rate.Value);
        }

        private static void WriteAppliedRateToQuote(PluginContext ctx, Guid quoteId, decimal rateFraction)
        {
            var update = new Entity(SchemaConstants.Entities.Quote, quoteId)
            {
                [SchemaConstants.Quote.AppliedTaxRatePercent] =
                    Math.Round(rateFraction * 100m, 4)
            };
            ctx.Service.Update(update);
        }

        private static decimal ComputeLineTax(Money deliveredPrice, decimal quantity, decimal rate)
        {
            if (deliveredPrice == null || deliveredPrice.Value <= 0) return 0m;
            return Math.Round(deliveredPrice.Value * quantity * rate, 2);
        }

        private static decimal ComputeUnitWithTax(Money deliveredPrice, decimal rate)
        {
            if (deliveredPrice == null || deliveredPrice.Value <= 0) return 0m;
            return Math.Round(deliveredPrice.Value * (1m + rate), 2);
        }

        private static void WriteTaxAndUnitPrices(PluginContext ctx, Guid quoteDetailId,
            decimal taxTrailer, decimal taxStraight,
            decimal unitTrailerWithTax, decimal unitStraightWithTax)
        {
            var update = new Entity(SchemaConstants.Entities.QuoteDetail, quoteDetailId)
            {
                [SchemaConstants.QuoteDetail.TaxAmountTrailer] = new Money(taxTrailer),
                [SchemaConstants.QuoteDetail.TaxAmountStraight] = new Money(taxStraight),
                [SchemaConstants.QuoteDetail.UnitPriceTrailerWithTax] = new Money(unitTrailerWithTax),
                [SchemaConstants.QuoteDetail.UnitPriceStraightWithTax] = new Money(unitStraightWithTax)
            };
            ctx.Service.Update(update);
        }

        /// On Update only the changed fields are on Target – pull the rest
        /// from the post-image. Falls back to a Retrieve when the image
        /// isn't registered (typical on Create or registration drift).
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
                    SchemaConstants.QuoteDetail.DeliveredPriceTrailer,
                    SchemaConstants.QuoteDetail.DeliveredPriceStraight));
        }
    }
}
