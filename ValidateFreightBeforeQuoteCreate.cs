// ============================================================================
// ValidateFreightBeforeQuoteCreate.cs
// Kraemer Mining & Minerals — Dynamics 365 Sales Plugin
//
// PURPOSE:
//   Prevents creation of a Quote if the parent Project (Opportunity) is
//   missing any of the required freight calculation fields. Fires before
//   the record is saved and displays a user-friendly error message in
//   the standard D365 error dialog.
//
// REGISTRATION:
//   Entity:    quote
//   Message:   Create
//   Stage:     Pre-Validation (10), Synchronous
//   Execution Order:  1
//   Images:    None required
//
// AUTHOR:  Dustin Anderson — Eide Bailly Technology Consulting
// DATE:    2026
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    public class ValidateFreightBeforeQuoteCreate : IPlugin
    {
        // ── Field Schema Names ───────────────────────────────────────────────
        private const string QUOTE_OPP_LOOKUP = "opportunityid";

        private const string OPP_SHIPPING_RATE = "eb_shippingrateperhour";
        private const string OPP_CYCLE_TIME = "eb_cycletime";
        private const string OPP_LOAD_TIME = "eb_loadtime";
        private const string OPP_UNLOAD_TIME = "eb_unloadtime";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace = (ITracingService)serviceProvider
                .GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider
                .GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider
                .GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            try
            {
                trace.Trace("ValidateFreightBeforeQuoteCreate: Execution started.");

                // ── Guard: Prevent recursion ─────────────────────────────────
                if (context.Depth > 2)
                {
                    trace.Trace("Depth > 2 — exiting.");
                    return;
                }

                // ── Guard: Validate target ───────────────────────────────────
                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity))
                {
                    trace.Trace("No valid Target — exiting.");
                    return;
                }

                Entity target = (Entity)context.InputParameters["Target"];

                // ── Get the parent Project (Opportunity) ─────────────────────
                EntityReference oppRef = target
                    .GetAttributeValue<EntityReference>(QUOTE_OPP_LOOKUP);

                if (oppRef == null)
                {
                    // Quote created without a parent Project — allow it.
                    // This handles edge cases like standalone quotes.
                    trace.Trace("No parent Project on Quote — skipping validation.");
                    return;
                }

                trace.Trace("Parent Project ID: {0}", oppRef.Id);

                // ── Retrieve freight fields from the Project ─────────────────
                Entity opp = service.Retrieve("opportunity", oppRef.Id,
                    new ColumnSet(
                        OPP_SHIPPING_RATE,
                        OPP_CYCLE_TIME,
                        OPP_LOAD_TIME,
                        OPP_UNLOAD_TIME
                    ));

                // ── Check each required field ────────────────────────────────
                List<string> missingFields = new List<string>();

                Money shippingRate = opp.GetAttributeValue<Money>(OPP_SHIPPING_RATE);
                if (shippingRate == null || shippingRate.Value <= 0)
                {
                    missingFields.Add("Shipping Rate ($/hr)");
                }

                int? cycleTime = opp.GetAttributeValue<int?>(OPP_CYCLE_TIME);
                if (cycleTime == null || cycleTime.Value <= 0)
                {
                    missingFields.Add("Cycle Time (minutes)");
                }

                int? loadTime = opp.GetAttributeValue<int?>(OPP_LOAD_TIME);
                if (loadTime == null || loadTime.Value <= 0)
                {
                    missingFields.Add("Load Time (minutes)");
                }

                int? unloadTime = opp.GetAttributeValue<int?>(OPP_UNLOAD_TIME);
                if (unloadTime == null || unloadTime.Value <= 0)
                {
                    missingFields.Add("Unload Time (minutes)");
                }

                // ── If anything is missing, block the create ─────────────────
                if (missingFields.Count > 0)
                {
                    StringBuilder message = new StringBuilder();
                    message.AppendLine("Cannot create a Quote — the following " +
                        "freight fields are missing on the Project:");
                    message.AppendLine();

                    foreach (string field in missingFields)
                    {
                        message.AppendLine("  •  " + field);
                    }

                    message.AppendLine();
                    message.AppendLine("Please go back to the Project record " +
                        "and fill in the Freight / Delivery Calculation " +
                        "section before creating a Quote.");

                    trace.Trace("Validation FAILED — missing fields: {0}",
                        string.Join(", ", missingFields));

                    throw new InvalidPluginExecutionException(message.ToString());
                }

                trace.Trace("Validation PASSED — all freight fields populated.");
            }
            catch (InvalidPluginExecutionException)
            {
                // Re-throw the validation message as-is
                throw;
            }
            catch (Exception ex)
            {
                trace.Trace("ValidateFreightBeforeQuoteCreate ERROR: {0}",
                    ex.ToString());
                throw new InvalidPluginExecutionException(
                    "An error occurred while validating freight fields. " +
                    "Please contact your administrator. " +
                    "Error: " + ex.Message, ex);
            }
        }
    }
}