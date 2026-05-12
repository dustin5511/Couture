// ============================================================================
// PopulateIncomingMaterialFlag.cs
// Kraemer Mining & Minerals — Dynamics 365 Sales Plugin
//
// PURPOSE:
//   When a Quote Product line item is created, this plugin looks up the
//   associated Product record and copies its Incoming Material flag onto
//   the Quote Product. This ensures that rubble/common dirt products are
//   automatically excluded from delivery pricing without the salesperson
//   needing to manually check a box.
//
//   The flag is set as overridable — if a salesperson needs to change it
//   on a specific quote (e.g., a product that's normally outgoing but is
//   being received on a particular job), they can toggle it manually,
//   which will trigger the CalculateDeliveryPricing plugin to recalculate.
//
// REGISTRATION:
//   Entity:    quotedetail (Quote Product)
//   Message:   Create
//   Stage:     Pre-Operation (Stage 20), Synchronous
//   Images:    None required (Pre-Operation modifies the Target directly)
//
// EXECUTION ORDER:
//   This plugin MUST execute BEFORE CalculateDeliveryPricing on Create.
//   Set Execution Order = 1 on this step, and Execution Order = 2 on
//   the CalculateDeliveryPricing Create step. This ensures the incoming
//   flag is set before the delivery calculation reads it.
//
// AUTHOR:  Dustin Anderson — Eide Bailly Technology Consulting
// DATE:    2026
// ============================================================================

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    public class PopulateIncomingMaterialFlag : IPlugin
    {
        // ── Field Schema Names ───────────────────────────────────────────────
        private const string QD_PRODUCT_LOOKUP = "productid";
        private const string QD_INCOMING_MATERIAL = "eb_isincomingmaterial";
        private const string PRODUCT_INCOMING = "eb_incomingmaterial";

        public void Execute(IServiceProvider serviceProvider)
        {
            // ── Service Resolution ───────────────────────────────────────────
            ITracingService trace = (ITracingService)serviceProvider
                .GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider
                .GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider
                .GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            try
            {
                trace.Trace("PopulateIncomingMaterialFlag: Execution started.");

                // ── Guard: Only run on Create ────────────────────────────────
                if (context.MessageName != "Create")
                {
                    trace.Trace("Not a Create message — exiting.");
                    return;
                }

                // ── Guard: Prevent recursion ─────────────────────────────────
                if (context.Depth > 2)
                {
                    trace.Trace("Depth > 2 — exiting to prevent recursion.");
                    return;
                }

                // ── Guard: Validate target ───────────────────────────────────
                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity))
                {
                    trace.Trace("No valid Target entity — exiting.");
                    return;
                }

                Entity target = (Entity)context.InputParameters["Target"];

                // ── Guard: If incoming flag is already explicitly set, ────────
                //    respect the user's choice and don't overwrite it.
                if (target.Contains(QD_INCOMING_MATERIAL))
                {
                    trace.Trace("Incoming Material flag already set on target — " +
                        "respecting explicit value. Exiting.");
                    return;
                }

                // ── Get the Product lookup ───────────────────────────────────
                EntityReference productRef = target
                    .GetAttributeValue<EntityReference>(QD_PRODUCT_LOOKUP);

                if (productRef == null)
                {
                    // Write-in product (no product lookup) — default to false
                    trace.Trace("No product lookup (write-in product) — " +
                        "defaulting Incoming Material to false.");
                    target[QD_INCOMING_MATERIAL] = false;
                    return;
                }

                trace.Trace("Product ID: {0}", productRef.Id);

                // ── Retrieve the Product's Incoming Material flag ─────────────
                Entity product = service.Retrieve("product", productRef.Id,
                    new ColumnSet(PRODUCT_INCOMING));

                bool isIncoming = product.GetAttributeValue<bool>(PRODUCT_INCOMING);
                trace.Trace("Product Incoming Material flag: {0}", isIncoming);

                // ── Stamp the flag onto the Quote Product Target ──────────────
                // Because this is Pre-Operation, we modify the Target directly.
                // The platform will include this value when it creates the record.
                // No separate Update call needed.
                target[QD_INCOMING_MATERIAL] = isIncoming;

                trace.Trace("PopulateIncomingMaterialFlag: Set to {0}. " +
                    "Execution completed.", isIncoming);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                trace.Trace("PopulateIncomingMaterialFlag ERROR: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "An error occurred while setting the incoming material flag. " +
                    "Please contact your administrator. " +
                    "Error: " + ex.Message, ex);
            }
        }
    }
}