using System;
using System.Collections.Generic;
using System.Linq;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes.Helpers
{
    /// All retrieval and mutation logic shared by the plugins lives here so
    /// each plugin stays focused on "when does this run" rather than "how".
    internal sealed class QuoteOpportunityService
    {
        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;

        public QuoteOpportunityService(IOrganizationService service, ITracingService tracing)
        {
            _service = service;
            _tracing = tracing;
        }

        public Entity RetrieveQuote(Guid quoteId, params string[] columns)
        {
            return _service.Retrieve(SchemaConstants.Entities.Quote, quoteId, new ColumnSet(columns));
        }

        /// Returns every quote belonging to the opportunity except the one we
        /// are currently processing. Always hydrates state/status/FOB total/
        /// delayed flag so callers don't need to re-retrieve.
        public List<Entity> RetrieveSiblingQuotes(Guid opportunityId, Guid excludeQuoteId)
        {
            var query = new QueryExpression(SchemaConstants.Entities.Quote)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.Quote.Id,
                    SchemaConstants.Quote.StateCode,
                    SchemaConstants.Quote.StatusCode,
                    SchemaConstants.Quote.FobTotal,
                    SchemaConstants.Quote.Delayed,
                    SchemaConstants.Quote.Name,
                    SchemaConstants.Quote.OpportunityId),
                NoLock = true
            };
            query.Criteria.AddCondition(SchemaConstants.Quote.OpportunityId, ConditionOperator.Equal, opportunityId);
            if (excludeQuoteId != Guid.Empty)
            {
                query.Criteria.AddCondition(SchemaConstants.Quote.Id, ConditionOperator.NotEqual, excludeQuoteId);
            }

            return _service.RetrieveMultiple(query).Entities.ToList();
        }

        public bool IsQuoteOpen(Entity quote)
        {
            var state = quote.GetAttributeValue<OptionSetValue>(SchemaConstants.Quote.StateCode);
            if (state == null) return false;
            return state.Value == SchemaConstants.QuoteState.Draft
                || state.Value == SchemaConstants.QuoteState.Active;
        }

        public bool IsQuoteWon(Entity quote)
        {
            var state = quote.GetAttributeValue<OptionSetValue>(SchemaConstants.Quote.StateCode);
            return state != null && state.Value == SchemaConstants.QuoteState.Won;
        }

        public bool IsQuoteLost(Entity quote)
        {
            var status = quote.GetAttributeValue<OptionSetValue>(SchemaConstants.Quote.StatusCode);
            return status != null
                && (status.Value == SchemaConstants.QuoteStatus.Lost
                    || status.Value == SchemaConstants.QuoteStatus.Canceled);
        }

        /// Re-activates a quote the platform auto-revised (status 7) when it
        /// tried to close the opportunity on a competing Win. We push it back
        /// to Active/InProgress so sales users can still work it.
        public void ReactivateQuote(Guid quoteId)
        {
            _tracing.Trace("Re-activating quote {0}", quoteId);
            var request = new SetStateRequest
            {
                EntityMoniker = new EntityReference(SchemaConstants.Entities.Quote, quoteId),
                State = new OptionSetValue(SchemaConstants.QuoteState.Active),
                Status = new OptionSetValue(SchemaConstants.QuoteStatus.InProgressActive)
            };
            _service.Execute(request);
        }

        /// Re-opens an opportunity the platform force-closed as Won. Only safe
        /// when at least one sibling quote is still workable.
        public void ReopenOpportunity(Guid opportunityId)
        {
            _tracing.Trace("Re-opening opportunity {0}", opportunityId);
            var request = new SetStateRequest
            {
                EntityMoniker = new EntityReference(SchemaConstants.Entities.Opportunity, opportunityId),
                State = new OptionSetValue(SchemaConstants.OpportunityState.Open),
                Status = new OptionSetValue(SchemaConstants.OpportunityStatus.InProgress)
            };
            _service.Execute(request);
        }

        public void CloseOpportunityAsWon(Guid opportunityId)
        {
            _tracing.Trace("Closing opportunity {0} as Won", opportunityId);
            var close = new Entity(SchemaConstants.Entities.OpportunityClose)
            {
                ["opportunityid"] = new EntityReference(SchemaConstants.Entities.Opportunity, opportunityId),
                ["subject"] = "Opportunity Won – all quotes finalised"
            };
            _service.Execute(new WinOpportunityRequest
            {
                OpportunityClose = close,
                Status = new OptionSetValue(SchemaConstants.OpportunityStatus.Won)
            });
        }

        public void CloseOpportunityAsLost(Guid opportunityId)
        {
            _tracing.Trace("Closing opportunity {0} as Lost", opportunityId);
            var close = new Entity(SchemaConstants.Entities.OpportunityClose)
            {
                ["opportunityid"] = new EntityReference(SchemaConstants.Entities.Opportunity, opportunityId),
                ["subject"] = "Opportunity Lost – every quote closed"
            };
            _service.Execute(new LoseOpportunityRequest
            {
                OpportunityClose = close,
                Status = new OptionSetValue(SchemaConstants.OpportunityStatus.Canceled)
            });
        }

        /// Recalculates the custom rollup money field. Runs as a single
        /// Update so it co-operates with plugin/workflow auditing rather
        /// than triggering nested state changes.
        public void RefreshOpportunityRollup(Guid opportunityId)
        {
            // Include the quote we may have just changed – retrieving the
            // whole set keeps the sum in sync regardless of which message
            // triggered the refresh.
            var quotes = RetrieveSiblingQuotes(opportunityId, Guid.Empty);

            decimal total = 0m;

            foreach (var quote in quotes)
            {
                var state = quote.GetAttributeValue<OptionSetValue>(SchemaConstants.Quote.StateCode);
                if (state == null) continue;

                var stateValue = state.Value;
                if (stateValue != SchemaConstants.QuoteState.Active
                    && stateValue != SchemaConstants.QuoteState.Won)
                {
                    continue;
                }

                // Delayed quotes are still Active/Won but the user has
                // flagged them as paused, so they don't count toward the
                // project's expected revenue figure.
                if (quote.GetAttributeValue<bool>(SchemaConstants.Quote.Delayed))
                {
                    continue;
                }

                var amount = quote.GetAttributeValue<Money>(SchemaConstants.Quote.FobTotal);
                if (amount != null)
                {
                    total += amount.Value;
                }
            }

            var update = new Entity(SchemaConstants.Entities.Opportunity, opportunityId)
            {
                [SchemaConstants.Opportunity.ActiveWonQuotesTotal] = new Money(total)
            };

            // The write targets the opportunity, but our rollup plugin only
            // fires on quote Create/Update/Delete – so this update can't
            // recurse back into us. A plain Update is enough.
            _service.Update(update);

            _tracing.Trace("Opportunity {0} rollup refreshed to {1}",
                opportunityId, total);
        }
    }
}
