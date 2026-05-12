using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// When a Quote Product line is created the plugin looks up the
    /// associated Product and copies its Incoming Material flag onto the
    /// Quote Product. This means rubble / common-dirt products are
    /// automatically excluded from delivery pricing without the
    /// salesperson having to check a box.
    ///
    /// The flag is overridable: if the user has already set
    /// eb_isincomingmaterial on the target (e.g. a normally outgoing
    /// product being received on a particular job) we respect their
    /// explicit value and don't touch it.
    ///
    /// Register on:
    ///   Message=Create, PrimaryEntity=quotedetail, Stage=PreOperation (20)
    ///   Execution Order = 1 (must run BEFORE CalculateDeliveryPricingPlugin
    ///   on Create so that plugin's incoming-flag read sees this write).
    public sealed class PopulateIncomingMaterialFlag : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace("PopulateIncomingMaterialFlag started.");

            if (ctx.Execution.MessageName != SchemaConstants.Messages.Create)
            {
                ctx.Tracing.Trace("Not a Create message – exiting.");
                return;
            }

            if (ctx.Execution.Depth > 2)
            {
                ctx.Tracing.Trace("Depth > 2 – exiting to prevent recursion.");
                return;
            }

            if (!ctx.Execution.InputParameters.Contains("Target")
                || !(ctx.Execution.InputParameters["Target"] is Entity target))
            {
                ctx.Tracing.Trace("No valid Target – exiting.");
                return;
            }

            // Respect an explicit user value: if they ticked the box on the
            // line themselves, leave it alone.
            if (target.Contains(SchemaConstants.QuoteDetail.IsIncomingMaterial))
            {
                ctx.Tracing.Trace(
                    "Incoming Material flag already set on target – respecting explicit value.");
                return;
            }

            var productRef = target.GetAttributeValue<EntityReference>(
                SchemaConstants.QuoteDetail.ProductId);
            if (productRef == null)
            {
                // Write-in product (no Product lookup) — default to false so
                // delivery pricing applies normally.
                ctx.Tracing.Trace(
                    "No product lookup (write-in product) – defaulting Incoming Material to false.");
                target[SchemaConstants.QuoteDetail.IsIncomingMaterial] = false;
                return;
            }

            ctx.Tracing.Trace("Product ID: {0}", productRef.Id);

            var product = ctx.Service.Retrieve(
                SchemaConstants.Entities.Product,
                productRef.Id,
                new ColumnSet(SchemaConstants.Product.IncomingMaterial));

            var isIncoming = product.GetAttributeValue<bool>(
                SchemaConstants.Product.IncomingMaterial);
            ctx.Tracing.Trace("Product Incoming Material flag: {0}", isIncoming);

            // Pre-Operation mutation of the Target lands in the initial
            // insert; no extra Update call needed.
            target[SchemaConstants.QuoteDetail.IsIncomingMaterial] = isIncoming;
            ctx.Tracing.Trace("Incoming Material set to {0}.", isIncoming);
        }
    }
}
