namespace Couture.Plugins.MultipleQuotes.Constants
{
    /// Centralizes entity, attribute and option-set values so a single edit
    /// propagates across every plugin when the customer's publisher prefix
    /// or rollup field name differs.
    internal static class SchemaConstants
    {
        internal static class Entities
        {
            public const string Quote = "quote";
            public const string QuoteClose = "quoteclose";
            public const string QuoteDetail = "quotedetail";
            public const string Opportunity = "opportunity";
            public const string OpportunityClose = "opportunityclose";
            public const string Product = "product";
            public const string TaxRate = "eb_taxrate";
        }

        internal static class Product
        {
            /// Boolean flag on the Product record that marks it as incoming
            /// material (rubble, common dirt, etc.). PopulateIncomingMaterialFlag
            /// copies it onto each new Quote Product line as
            /// QuoteDetail.IsIncomingMaterial.
            public const string IncomingMaterial = "eb_incomingmaterial";
        }

        internal static class Quote
        {
            public const string Id = "quoteid";
            /// Alias for Id used by code paths that name the primary key
            /// after the entity (e.g. RecalcDeliveryOnProjectChange).
            public const string QuoteId = Id;
            public const string OpportunityId = "opportunityid";
            public const string TotalAmount = "totalamount";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Name = "name";

            // Custom money totals populated by QuoteTotalsRollupPlugin from
            // the quote's line items. FOB total never includes tax; delivered
            // totals include their matching tax bucket when delivery
            // preference allows.
            public const string FobTotal = "eb_fobtotal";
            public const string TotalDeliveredTrailer = "eb_totaldeliveredtrailer";
            public const string TotalDeliveredStraight = "eb_totaldeliveredstraight";
            public const string TaxTotalTrailer = "eb_taxtotaltrailer";
            public const string TaxTotalStraight = "eb_taxtotalstraight";

            /// Tax rate applied to this quote, stored as a **percentage**
            /// (e.g. 8.125 for 8.125%). Source rate from eb_taxrate.eb_rate
            /// is a fraction (0.081250); the plugin multiplies by 100 before
            /// writing here so the Word template can show "{value}%"
            /// without any formula in the content control.
            public const string AppliedTaxRatePercent = "eb_taxratepercent";
        }

        internal static class Opportunity
        {
            public const string Id = "opportunityid";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";

            /// Custom money field on Opportunity that stores the sum of
            /// totalamount for every quote that is currently Active or Won.
            /// Update the prefix/name to match the target environment.
            public const string ActiveWonQuotesTotal = "new_activewonquotestotal";

            // Freight inputs and pre-calculated rates used by
            // CalculateDeliveryPricingPlugin.
            public const string ShippingRatePerHour = "eb_shippingrateperhour";
            public const string CycleTime = "eb_cycletime";
            public const string LoadTime = "eb_loadtime";
            public const string UnloadTime = "eb_unloadtime";
            public const string TrailerRatePerTon = "eb_trailerrateton";
            public const string StraightTruckRatePerTon = "eb_straighttruckrateton";

            /// Cycle + Load + Unload time on the Opportunity – cached so
            /// RecalcDeliveryOnProjectChange doesn't have to recompute it
            /// every time a Quote Product is touched.
            public const string TotalTripMinutes = "eb_totaltripminutes";

            // Inputs gating quote creation and feeding the tax lookup.
            public const string JobsiteZip = "eb_jobsitezip";
            public const string DeliveryPreference = "eb_deliverypreference";
        }

        internal static class QuoteDetail
        {
            public const string Id = "quotedetailid";
            public const string QuoteId = "quoteid";
            public const string ProductId = "productid";
            public const string PricePerUnit = "priceperunit";
            public const string Quantity = "quantity";
            public const string IsIncomingMaterial = "eb_isincomingmaterial";
            public const string DeliveredPriceTrailer = "eb_deliveredpricetrailer";
            public const string DeliveredPriceStraight = "eb_deliveredpricestraight";

            // Per-line extended delivered amounts (quantity × delivered
            // price/ton). Used by CalculateDeliveryPricingPlugin and rolled
            // up to the parent Quote.
            public const string ExtendedDeliveredTrailer = "eb_extendeddeliveredtrailer";
            public const string ExtendedDeliveredStraight = "eb_extendeddeliveredstraight";

            // Per-line tax in each scenario. Different because tax applies
            // to the full delivered price (which includes freight) and the
            // trailer and straight-truck freight components differ.
            //   eb_taxamounttrailer  = deliveredpricetrailer  * qty * rate
            //   eb_taxamountstraight = deliveredpricestraight * qty * rate
            public const string TaxAmountTrailer = "eb_taxamounttrailer";
            public const string TaxAmountStraight = "eb_taxamountstraight";
        }

        internal static class TaxRate
        {
            public const string Id = "eb_taxrateid";
            public const string Name = "eb_name";
            public const string State = "eb_state";
            public const string Zip = "eb_zip";

            /// Combined sales tax rate for the (state, ZIP), expressed as a
            /// fraction (e.g. 0.06875 for 6.875%). The MN refresh flow
            /// writes this column; the CalculateTaxPlugin reads it.
            public const string Rate = "eb_rate";
            public const string EffectiveDate = "eb_effectivedate";

            /// State name stored on each eb_taxrate row. The MN refresh flow
            /// writes the full state name (not the 2-letter code) so the
            /// lookup uses the same literal.
            public const string MinnesotaStateName = "Minnesota";
        }

        /// Option-set values for Opportunity.eb_deliverypreference.
        /// FOB = customer picks up; Delivery = we deliver; FobAndDelivery =
        /// the printed quote shows both pricing scenarios.
        internal static class DeliveryPreference
        {
            public const int FOB = 1;
            public const int Delivery = 2;
            public const int FobAndDelivery = 3;
        }

        internal static class Freight
        {
            public const decimal TrailerTonsPerLoad = 25m;
            public const decimal StraightTruckTonsPerLoad = 20m;
            public const int MinutesPerHour = 60;
            public const int DefaultLoadTimeMinutes = 10;
            public const int DefaultUnloadTimeMinutes = 10;
        }

        internal static class QuoteState
        {
            public const int Draft = 0;
            public const int Active = 1;
            public const int Won = 2;
            public const int Closed = 3;
        }

        internal static class QuoteStatus
        {
            public const int InProgressDraft = 1;
            public const int InProgressActive = 2;
            public const int OpenActive = 3;
            public const int Won = 4;
            public const int Lost = 5;
            public const int Canceled = 6;
            public const int Revised = 7;
        }

        internal static class OpportunityState
        {
            public const int Open = 0;
            public const int Won = 1;
            public const int Lost = 2;
        }

        internal static class OpportunityStatus
        {
            public const int InProgress = 1;
            public const int OnHold = 2;
            public const int Won = 3;
            public const int Canceled = 4;
            public const int OutSold = 5;
        }

        internal static class Messages
        {
            public const string Win = "Win";
            public const string Close = "Close";
            public const string Create = "Create";
            public const string Update = "Update";
            public const string Delete = "Delete";
            public const string SetState = "SetState";
        }

        internal static class SharedVariableKeys
        {
            /// Comma-separated list of quote ids that were Active on the
            /// opportunity at the moment the Win message started.
            public const string SiblingActiveQuoteIds = "Couture.SiblingActiveQuoteIds";

            /// Id of the opportunity whose state needs restoring in
            /// post-operation once the platform has force-closed it.
            public const string OpportunityToReopen = "Couture.OpportunityToReopen";
        }
    }
}
