using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes
{
    /// When the user changes customerid on a quote, this plugin reads the
    /// new account's defaultpricelevelid and writes it onto the quote's
    /// pricelevelid. No line-level recalc is fired – Kraemer's choice:
    /// the user manually adjusts line prices afterward if the new price
    /// list differs from the old one.
    ///
    /// Account-only by design. Contacts are rejected with an explicit
    /// error rather than silently doing nothing or attempting to read
    /// the contact's parent account's price list.
    ///
    /// Register on:
    ///   Message=Update, PrimaryEntity=quote, Stage=PostOperation (40)
    ///   Filter attributes: customerid
    public sealed class SyncPriceListOnCustomerChange : BasePlugin
    {
        protected override void ExecuteInternal(PluginContext ctx)
        {
            ctx.Tracing.Trace(
                "SyncPriceListOnCustomerChange started. Depth={0}",
                ctx.Execution.Depth);

            // We write back to the same quote (pricelevelid) but our
            // filter is customerid only, so the platform won't re-fire
            // this plugin on that write. Depth guard is belt-and-braces.
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

            var newCustomer = target.GetAttributeValue<EntityReference>(
                SchemaConstants.Quote.CustomerId);

            if (newCustomer == null)
            {
                // Customer cleared – per requirement leave pricelevelid
                // alone rather than blanking it. The user can pick a new
                // customer when ready.
                ctx.Tracing.Trace(
                    "Customer cleared on quote {0} – leaving pricelevelid untouched.",
                    target.Id);
                return;
            }

            if (newCustomer.LogicalName != SchemaConstants.Entities.Account)
            {
                throw new InvalidPluginExecutionException(
                    "Quote customer must be an Account – Contact-typed " +
                    "customers are not supported in this pipeline. " +
                    "Got: " + newCustomer.LogicalName);
            }

            // Skip Won / Closed quotes. The platform usually blocks header
            // edits on closed quotes anyway, but the guard keeps this
            // plugin from re-stamping a price list on a frozen record.
            var quote = ctx.Service.Retrieve(
                SchemaConstants.Entities.Quote,
                target.Id,
                new ColumnSet(SchemaConstants.Quote.StateCode));
            var stateCode = quote.GetAttributeValue<OptionSetValue>(
                SchemaConstants.Quote.StateCode);
            if (stateCode != null
                && (stateCode.Value == SchemaConstants.QuoteState.Won
                    || stateCode.Value == SchemaConstants.QuoteState.Closed))
            {
                ctx.Tracing.Trace(
                    "Quote state {0} is Won/Closed – skipping pricelist sync.",
                    stateCode.Value);
                return;
            }

            var account = ctx.Service.Retrieve(
                SchemaConstants.Entities.Account,
                newCustomer.Id,
                new ColumnSet(SchemaConstants.Account.DefaultPriceLevelId));

            var defaultPriceList = account.GetAttributeValue<EntityReference>(
                SchemaConstants.Account.DefaultPriceLevelId);
            if (defaultPriceList == null)
            {
                ctx.Tracing.Trace(
                    "Account {0} has no defaultpricelevelid – leaving " +
                    "the quote's pricelevelid untouched.",
                    newCustomer.Id);
                return;
            }

            var update = new Entity(SchemaConstants.Entities.Quote, target.Id)
            {
                [SchemaConstants.Quote.PriceLevelId] = defaultPriceList
            };
            ctx.Service.Update(update);

            ctx.Tracing.Trace(
                "Synced pricelevelid={0} from account {1} onto quote {2}.",
                defaultPriceList.Id, newCustomer.Id, target.Id);
        }
    }
}
