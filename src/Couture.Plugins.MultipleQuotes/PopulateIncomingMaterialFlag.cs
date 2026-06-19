using System;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// When a Quote Product line is created the plugin looks up the
    /// associated Product and copies two fields onto the Quote Product:
    ///
    ///   • eb_incomingmaterial → eb_isincomingmaterial — flags rubble /
    ///     common-dirt products so delivery pricing is skipped without
    ///     the salesperson having to check a box.
    ///   • eb_location → eb_location — copies the plant/quarry lookup so
    ///     the printed quote can show the source location per line
    ///     without traversing the productid.
    ///
    /// Both copies are overridable: if the user has already set the
    /// corresponding field on the target (e.g. picked a different
    /// location for a particular job), we respect the explicit value
    /// and don't touch it.
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

            var productRef = target.GetAttributeValue<EntityReference>(
                SchemaConstants.QuoteDetail.ProductId);
            if (productRef == null)
            {
                // Write-in product (no Product lookup) — default the
                // incoming flag to false so delivery pricing applies
                // normally. Location is left null since there's no
                // source product to copy from.
                ctx.Tracing.Trace(
                    "No product lookup (write-in product) – defaulting Incoming Material to false.");
                if (!target.Contains(SchemaConstants.QuoteDetail.IsIncomingMaterial))
                {
                    target[SchemaConstants.QuoteDetail.IsIncomingMaterial] = false;
                }
                return;
            }

            ctx.Tracing.Trace("Product ID: {0}", productRef.Id);

            var product = ctx.Service.Retrieve(
                SchemaConstants.Entities.Product,
                productRef.Id,
                new ColumnSet(
                    SchemaConstants.Product.IncomingMaterial,
                    SchemaConstants.Product.Location));

            // ── Incoming Material flag ──────────────────────────────────
            if (target.Contains(SchemaConstants.QuoteDetail.IsIncomingMaterial))
            {
                ctx.Tracing.Trace(
                    "Incoming Material flag already set on target – respecting explicit value.");
            }
            else
            {
                var isIncoming = product.GetAttributeValue<bool>(
                    SchemaConstants.Product.IncomingMaterial);
                target[SchemaConstants.QuoteDetail.IsIncomingMaterial] = isIncoming;
                ctx.Tracing.Trace("Incoming Material set to {0}.", isIncoming);
            }

            // ── Location lookup ─────────────────────────────────────────
            if (target.Contains(SchemaConstants.QuoteDetail.Location))
            {
                ctx.Tracing.Trace(
                    "Location already set on target – respecting explicit value.");
            }
            else
            {
                var location = product.GetAttributeValue<EntityReference>(
                    SchemaConstants.Product.Location);
                if (location != null)
                {
                    target[SchemaConstants.QuoteDetail.Location] = location;
                    ctx.Tracing.Trace("Location set to {0}.", location.Id);
                }
                else
                {
                    ctx.Tracing.Trace("Product has no Location – leaving target unset.");
                }
            }
        }
    }
}
