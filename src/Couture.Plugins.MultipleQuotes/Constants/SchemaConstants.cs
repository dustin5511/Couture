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
            public const string SystemUser = "systemuser";
            public const string Account = "account";
        }

        internal static class Product
        {
            /// Boolean flag on the Product record that marks it as incoming
            /// material (rubble, common dirt, etc.). PopulateIncomingMaterialFlag
            /// copies it onto each new Quote Product line as
            /// QuoteDetail.IsIncomingMaterial.
            public const string IncomingMaterial = "eb_incomingmaterial";

            /// Lookup to the custom Locations entity (the plant/quarry the
            /// product comes from). Copied onto each new Quote Product
            /// line as QuoteDetail.Location so the printed quote shows
            /// the source location without traversing the productid.
            public const string Location = "eb_location";
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
            /// Polymorphic customer lookup (account / contact). Kraemer's
            /// pipeline only supports Account customers — SyncPriceList-
            /// OnCustomerChange throws if a Contact is set here.
            public const string CustomerId = "customerid";
            /// Standard Quote price list lookup. Kept in sync with the
            /// customer's defaultpricelevelid by
            /// SyncPriceListOnCustomerChange.
            public const string PriceLevelId = "pricelevelid";
            /// Two-option flag set by the user when a quote is on hold
            /// (waiting on customer decision, paused, etc.). When true,
            /// OpportunityQuoteRollupPlugin excludes the quote from the
            /// project's eb_activewonquotestotal sum.
            public const string Delayed = "eb_delayed";

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

            /// Per-quote copy of the project's eb_deliverypreference.
            /// Seeded from Opportunity at quote Create and then editable
            /// on the quote itself; all downstream tax / delivered-total
            /// calculations read from this field so each quote under a
            /// project can model a different scenario.
            public const string DeliveryPreference = "eb_deliverypreference";

            // Standard OOB Ship To address fields. Seeded at quote Create
            // from the parent Project's eb_jobsite* columns so the printed
            // quote shows the delivery address without any extra lookup.
            public const string ShipToLine1 = "shipto_line1";
            public const string ShipToLine2 = "shipto_line2";
            public const string ShipToCity = "shipto_city";
            public const string ShipToStateOrProvince = "shipto_stateorprovince";
            public const string ShipToPostalCode = "shipto_postalcode";
            public const string ShipToCountry = "shipto_country";

            // Denormalized owner contact fields. Word Template XML mapper
            // can't traverse the ownerid lookup into systemuser, so the
            // values get stamped onto the Quote at Create from the
            // owning user's record. Create-only — not refreshed on
            // Assign / reassignment.
            public const string OwnerFullname = "eb_ownerfullname";
            public const string OwnerEmail = "eb_owneremail";
            public const string OwnerDirect = "eb_ownerdirect";
            public const string OwnerMobile = "eb_ownermobile";
            public const string OwnerFax = "eb_ownerfax";
            public const string OwnerTitle = "eb_ownertitle";

            // Customer's internal job / project reference, copied from
            // the parent Opportunity at Create.
            public const string CustomerJobProjectNumber = "eb_customerjobprojectnumber";
        }

        internal static class SystemUser
        {
            public const string Id = "systemuserid";
            public const string FullName = "fullname";
            public const string InternalEmailAddress = "internalemailaddress";
            /// Main / direct desk phone on the systemuser record.
            public const string Telephone1 = "address1_telephone1";
            public const string MobilePhone = "mobilephone";
            public const string Fax = "address1_fax";
            public const string JobTitle = "jobtitle";
        }

        internal static class Account
        {
            public const string Id = "accountid";
            public const string Name = "name";
            /// OOB Account → Price List lookup. Drives the per-account
            /// price list applied to new quotes.
            public const string DefaultPriceLevelId = "defaultpricelevelid";

            /// Placeholder account every quote is initialised against
            /// at Create. The user picks the real customer afterward;
            /// SyncPriceListOnCustomerChange swaps the quote's
            /// pricelevelid when they do. The literal is hardcoded
            /// because Kraemer flagged the row itself with "do not
            /// remove" so a rename is unlikely.
            public const string DefaultPlaceholderName = "Default (do not remove)";
        }

        internal static class Opportunity
        {
            public const string Id = "opportunityid";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";

            /// Custom money field on Opportunity that stores the sum of
            /// eb_fobtotal for every quote currently Active or Won.
            /// FOB total = Σ priceperunit × quantity across the quote's
            /// line items — base product revenue, no delivery, no tax —
            /// which is what Kraemer wants to recognise regardless of
            /// the quote's delivery preference. Update the prefix/name
            /// to match the target environment.
            public const string ActiveWonQuotesTotal = "eb_activewonquotestotal";

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

            // Job-site address columns on the Project. Copied onto the
            // standard shipto_* address fields of every Quote created
            // under the Project (see ValidateFreightBeforeQuoteCreate).
            public const string JobsiteStreet1 = "eb_jobsitestreet1";
            public const string JobsiteStreet2 = "eb_jobsitestreet2";
            public const string JobsiteCity = "eb_jobsitecity";
            public const string JobsiteState = "eb_jobsitestate";
            public const string JobsiteCountry = "eb_jobsitecountry";

            /// Customer's internal job / project reference number.
            /// Copied to the new Quote at Create.
            public const string CustomerJobProjectNumber = "eb_customerjobprojectnumber";
        }

        internal static class QuoteDetail
        {
            public const string Id = "quotedetailid";
            public const string QuoteId = "quoteid";
            public const string ProductId = "productid";
            public const string PricePerUnit = "priceperunit";
            public const string Quantity = "quantity";
            public const string IsIncomingMaterial = "eb_isincomingmaterial";
            /// Lookup to Locations. Copied from Product.eb_location at
            /// Create by PopulateIncomingMaterialFlag.
            public const string Location = "eb_location";
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

            // Per-ton delivered unit prices INCLUDING tax. Customer-facing
            // numbers shown in the quote-line grid and on each line of the
            // printed Word template:
            //   eb_unitpricetrailerwithtax  = deliveredpricetrailer  * (1 + rate)
            //   eb_unitpricestraightwithtax = deliveredpricestraight * (1 + rate)
            // Cleared (0) for FOB-only quotes and incoming-material lines.
            public const string UnitPriceTrailerWithTax = "eb_unitpricetrailerwithtax";
            public const string UnitPriceStraightWithTax = "eb_unitpricestraightwithtax";
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

            /// State value stored on each eb_taxrate row for Minnesota.
            /// We use the 2-letter abbreviation rather than the full name
            /// because that's what the spreadsheet load uses; the literal
            /// must match exactly (case-sensitive).
            public const string MinnesotaStateName = "MN";
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
