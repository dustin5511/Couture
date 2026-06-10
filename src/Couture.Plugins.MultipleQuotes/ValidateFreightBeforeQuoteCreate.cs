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
    /// depends on. Surfaced as a single readable error message listing
    /// every missing field.
    ///
    /// Delivery preference is **always** required. The other checks
    /// only run for Delivery / FOB+Delivery quotes:
    ///   Freight calculation – shipping rate, cycle time, load time,
    ///                          unload time (drives delivered pricing).
    ///   Tax inputs          – jobsite ZIP (drives tax-rate lookup).
    /// FOB quotes pick up at the plant so they have no freight, no tax
    /// and no required job site – the salesperson can still fill in a
    /// job site address for lat/long heat-mapping, just not mandatory.
    ///
    /// On success the plugin also:
    ///   • copies eb_deliverypreference from the parent Project onto the
    ///     new Quote (seed only — the user can change it on the quote
    ///     afterward and downstream pricing reads from the quote field),
    ///   • copies the Project's job-site address (eb_jobsitestreet1/2,
    ///     city, state, postal, country) onto the Quote's standard
    ///     shipto_* fields so the printed quote shows the delivery
    ///     address without any extra lookup,
    ///   • copies eb_customerjobprojectnumber from the parent Project
    ///     onto the Quote,
    ///   • stamps eb_owner* (fullname, email, direct, mobile, fax,
    ///     title) on the Quote from the owning user's systemuser record
    ///     so the printed quote can show the salesperson's contact
    ///     details without traversing the ownerid lookup,
    ///   • overrides customerid + pricelevelid to the "Default (do not
    ///     remove)" placeholder account and its default price list so
    ///     every new quote starts on a known footing. The user then
    ///     changes the customer on the quote, which fires
    ///     SyncPriceListOnCustomerChange to swap the price list, and
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
                    SchemaConstants.Opportunity.TrailerRatePerTon,
                    SchemaConstants.Opportunity.StraightTruckRatePerTon,
                    SchemaConstants.Opportunity.TotalTripMinutes,
                    SchemaConstants.Opportunity.JobsiteZip,
                    SchemaConstants.Opportunity.DeliveryPreference,
                    SchemaConstants.Opportunity.JobsiteStreet1,
                    SchemaConstants.Opportunity.JobsiteStreet2,
                    SchemaConstants.Opportunity.JobsiteCity,
                    SchemaConstants.Opportunity.JobsiteState,
                    SchemaConstants.Opportunity.JobsiteCountry,
                    SchemaConstants.Opportunity.CustomerJobProjectNumber));

            var missing = new List<string>();

            // ── Delivery preference (always required) ───────────────────
            var deliveryPreference = opp.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Opportunity.DeliveryPreference);
            if (deliveryPreference == null)
                missing.Add("Delivery Preference");

            var jobsiteZip = opp.GetAttributeValue<string>(
                SchemaConstants.Opportunity.JobsiteZip);

            // Freight + tax fields only apply to Delivery and FOB+Delivery.
            // FOB-only quotes have no freight, no tax and no required
            // ZIP – the job site address stays optional.
            var requiresFreight = deliveryPreference != null
                && deliveryPreference.Value != SchemaConstants.DeliveryPreference.FOB;

            if (requiresFreight)
            {
                // ── Freight inputs ──────────────────────────────────────
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

                // ── Tax inputs ──────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(jobsiteZip))
                    missing.Add("Delivery ZIP Code");
            }

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

            // ── Seed Ship To address on the new quote ───────────────────
            // Copy the Project's job-site address onto the Quote's
            // standard shipto_* columns. Same Target-mutation trick as
            // the preference above; the user can override any field on
            // the quote afterward. ZIP is also written here so the
            // shipto block is internally consistent even though the tax
            // lookup still reads eb_jobsitezip off the Project.
            SeedStringField(target, SchemaConstants.Quote.ShipToLine1,
                opp.GetAttributeValue<string>(SchemaConstants.Opportunity.JobsiteStreet1));
            SeedStringField(target, SchemaConstants.Quote.ShipToLine2,
                opp.GetAttributeValue<string>(SchemaConstants.Opportunity.JobsiteStreet2));
            SeedStringField(target, SchemaConstants.Quote.ShipToCity,
                opp.GetAttributeValue<string>(SchemaConstants.Opportunity.JobsiteCity));
            SeedStringField(target, SchemaConstants.Quote.ShipToStateOrProvince,
                opp.GetAttributeValue<string>(SchemaConstants.Opportunity.JobsiteState));
            SeedStringField(target, SchemaConstants.Quote.ShipToPostalCode, jobsiteZip);
            SeedStringField(target, SchemaConstants.Quote.ShipToCountry,
                opp.GetAttributeValue<string>(SchemaConstants.Opportunity.JobsiteCountry));
            ctx.Tracing.Trace("Seeded shipto_* fields from Project job-site address.");

            // ── Seed customer's job / project number ────────────────────
            SeedStringField(target, SchemaConstants.Quote.CustomerJobProjectNumber,
                opp.GetAttributeValue<string>(
                    SchemaConstants.Opportunity.CustomerJobProjectNumber));

            // ── Seed freight inputs onto the new quote ─────────────────
            // Once on the quote, the user can change any of these and
            // CalculateDeliveryPricingPlugin (via RecalcQuoteOnFreightChange)
            // re-prices the line items. The project's values are NOT
            // cascaded after Create – each quote owns its own set.
            SeedMoneyField(target, SchemaConstants.Quote.ShippingRatePerHour,
                opp.GetAttributeValue<Money>(SchemaConstants.Opportunity.ShippingRatePerHour));
            SeedIntField(target, SchemaConstants.Quote.CycleTime,
                opp.GetAttributeValue<int?>(SchemaConstants.Opportunity.CycleTime));
            SeedIntField(target, SchemaConstants.Quote.LoadTime,
                opp.GetAttributeValue<int?>(SchemaConstants.Opportunity.LoadTime));
            SeedIntField(target, SchemaConstants.Quote.UnloadTime,
                opp.GetAttributeValue<int?>(SchemaConstants.Opportunity.UnloadTime));
            SeedMoneyField(target, SchemaConstants.Quote.TrailerRatePerTon,
                opp.GetAttributeValue<Money>(SchemaConstants.Opportunity.TrailerRatePerTon));
            SeedMoneyField(target, SchemaConstants.Quote.StraightTruckRatePerTon,
                opp.GetAttributeValue<Money>(SchemaConstants.Opportunity.StraightTruckRatePerTon));
            SeedIntField(target, SchemaConstants.Quote.TotalTripMinutes,
                opp.GetAttributeValue<int?>(SchemaConstants.Opportunity.TotalTripMinutes));

            // ── Stamp owner contact details ─────────────────────────────
            // Word Template XML mapper can't traverse ownerid → systemuser,
            // so denormalize the owning user's contact info onto the
            // Quote. Owner defaults to the calling user on Create unless
            // the form explicitly set ownerid; either way we resolve to
            // the systemuser record and stamp. Team-owned quotes skip
            // the stamp – nothing to map a team onto these fields.
            StampOwnerFields(ctx, target);

            // ── Override customer + price list to the placeholder account ─
            // Kraemer wants every new quote to start on the "Default (do
            // not remove)" account regardless of whatever customerid the
            // OOB quote-from-opportunity flow copied across. The user
            // picks the real customer on the quote, which then triggers
            // SyncPriceListOnCustomerChange to swap pricelevelid.
            SeedPlaceholderCustomerAndPriceList(ctx, target);

            // ── Stamp tax rate on the new quote ─────────────────────────
            // Only meaningful for Delivery / FOB+Delivery quotes that
            // have a ZIP. FOB-only quotes never owe tax so we skip the
            // lookup entirely. A missing rate when ZIP is provided is
            // non-fatal here; CalculateTaxPlugin will fail loudly when
            // the first quote detail tries to compute tax.
            if (!string.IsNullOrWhiteSpace(jobsiteZip))
            {
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
            else
            {
                ctx.Tracing.Trace("No ZIP on Project – skipping tax-rate stamp (FOB-only quote).");
            }
        }

        /// Writes `value` into `target[fieldName]` unless the user has
        /// already supplied something for that field on the inbound
        /// Quote – this keeps any manual override the user typed into
        /// the form before save. Empty / whitespace source values are
        /// skipped so we don't blank out a legitimate manual entry
        /// with a missing source column.
        private static void SeedStringField(Entity target, string fieldName, string value)
        {
            if (target.Contains(fieldName)) return;
            if (string.IsNullOrWhiteSpace(value)) return;
            target[fieldName] = value;
        }

        private static void SeedMoneyField(Entity target, string fieldName, Money value)
        {
            if (target.Contains(fieldName)) return;
            if (value == null) return;
            target[fieldName] = value;
        }

        private static void SeedIntField(Entity target, string fieldName, int? value)
        {
            if (target.Contains(fieldName)) return;
            if (!value.HasValue) return;
            target[fieldName] = value.Value;
        }

        /// Resolves the user who will own the new Quote and copies their
        /// contact info into the eb_owner* columns on Target. The owner
        /// is either explicitly set on the form (target["ownerid"]) or
        /// defaulted by the platform to the calling user; we read
        /// InitiatingUserId in the fallback case because UserId can be
        /// SYSTEM in some impersonation contexts. Team-owned quotes
        /// have no contact info to denormalize so we just trace and
        /// move on.
        private static void StampOwnerFields(PluginContext ctx, Entity target)
        {
            Guid userId;

            var explicitOwner = target.GetAttributeValue<EntityReference>("ownerid");
            if (explicitOwner != null)
            {
                if (explicitOwner.LogicalName != SchemaConstants.Entities.SystemUser)
                {
                    ctx.Tracing.Trace(
                        "ownerid is a {0}, not a user – skipping owner stamp.",
                        explicitOwner.LogicalName);
                    return;
                }
                userId = explicitOwner.Id;
            }
            else
            {
                userId = ctx.Execution.InitiatingUserId;
            }

            if (userId == Guid.Empty)
            {
                ctx.Tracing.Trace("No resolvable owner – skipping owner stamp.");
                return;
            }

            var user = ctx.Service.Retrieve(
                SchemaConstants.Entities.SystemUser,
                userId,
                new ColumnSet(
                    SchemaConstants.SystemUser.FullName,
                    SchemaConstants.SystemUser.InternalEmailAddress,
                    SchemaConstants.SystemUser.Telephone1,
                    SchemaConstants.SystemUser.MobilePhone,
                    SchemaConstants.SystemUser.Fax,
                    SchemaConstants.SystemUser.JobTitle));

            SeedStringField(target, SchemaConstants.Quote.OwnerFullname,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.FullName));
            SeedStringField(target, SchemaConstants.Quote.OwnerEmail,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.InternalEmailAddress));
            SeedStringField(target, SchemaConstants.Quote.OwnerDirect,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.Telephone1));
            SeedStringField(target, SchemaConstants.Quote.OwnerMobile,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.MobilePhone));
            SeedStringField(target, SchemaConstants.Quote.OwnerFax,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.Fax));
            SeedStringField(target, SchemaConstants.Quote.OwnerTitle,
                user.GetAttributeValue<string>(SchemaConstants.SystemUser.JobTitle));

            ctx.Tracing.Trace("Stamped eb_owner* from systemuser {0}.", userId);
        }

        /// Looks up the placeholder Account by exact name and stamps it
        /// onto target["customerid"]. The account's defaultpricelevelid
        /// rides along to seed target["pricelevelid"]. Unlike the
        /// shipto / owner seeds above, this one **overrides** any value
        /// the OOB quote-from-opportunity flow copied in – Kraemer
        /// wants every new quote to start from the same baseline so the
        /// salesperson explicitly picks the real customer afterward.
        private static void SeedPlaceholderCustomerAndPriceList(
            PluginContext ctx, Entity target)
        {
            var query = new QueryExpression(SchemaConstants.Entities.Account)
            {
                TopCount = 1,
                ColumnSet = new ColumnSet(
                    SchemaConstants.Account.DefaultPriceLevelId),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            SchemaConstants.Account.Name,
                            ConditionOperator.Equal,
                            SchemaConstants.Account.DefaultPlaceholderName)
                    }
                }
            };

            var match = ctx.Service.RetrieveMultiple(query);
            if (match.Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    "The placeholder account '" +
                    SchemaConstants.Account.DefaultPlaceholderName +
                    "' was not found. Please create that account before " +
                    "creating quotes — every new quote initialises against it.");
            }

            var defaultAcct = match.Entities[0];
            target[SchemaConstants.Quote.CustomerId] = new EntityReference(
                SchemaConstants.Entities.Account, defaultAcct.Id);

            var defaultPriceList = defaultAcct.GetAttributeValue<EntityReference>(
                SchemaConstants.Account.DefaultPriceLevelId);
            if (defaultPriceList != null)
            {
                target[SchemaConstants.Quote.PriceLevelId] = defaultPriceList;
                ctx.Tracing.Trace(
                    "Seeded customerid={0} and pricelevelid={1} from placeholder.",
                    defaultAcct.Id, defaultPriceList.Id);
            }
            else
            {
                ctx.Tracing.Trace(
                    "Seeded customerid={0} from placeholder; account has no " +
                    "defaultpricelevelid so pricelevelid is left unset.",
                    defaultAcct.Id);
            }
        }
    }
}
