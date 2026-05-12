using System.Linq;
using System.Text;
using Couture.Plugins.MultipleQuotes.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Couture.Plugins.MultipleQuotes.Helpers
{
    /// Resolves the sales-tax rate for an opportunity's delivery ZIP by
    /// querying the eb_taxrate table. The MN refresh flow stores rates
    /// keyed by full 9-digit ZIPs with no dashes, but users on the
    /// opportunity may enter the ZIP in any of:
    ///   "55337"        (5 digits)
    ///   "55337-1234"   (9 digits, ZIP+4 with dash)
    ///   "553371234"    (9 digits, no dash)
    /// We strip non-digits and look up with `BeginsWith` so a 5-digit
    /// input still resolves. If multiple +4 extensions match a 5-digit
    /// input we take the lowest eb_zip and warn in the trace log – the
    /// user should refine to 9 digits when the 5-digit ZIP crosses
    /// city/county/transit boundaries.
    internal sealed class TaxRateService
    {
        private readonly IOrganizationService _service;
        private readonly ITracingService _tracing;

        public TaxRateService(IOrganizationService service, ITracingService tracing)
        {
            _service = service;
            _tracing = tracing;
        }

        /// Returns the combined rate as a fraction (e.g. 0.06875 for
        /// 6.875%) for the given state + raw ZIP, read from eb_rate on
        /// the matching eb_taxrate row. Returns null when the ZIP is
        /// empty or no row matches; callers decide whether that is a
        /// hard error or a silent zero.
        public decimal? LookupCombinedRate(string state, string rawZip)
        {
            var cleaned = StripNonDigits(rawZip);
            if (cleaned.Length == 0)
            {
                _tracing.Trace("TaxRateService: empty ZIP after cleaning – no lookup.");
                return null;
            }

            var query = new QueryExpression(SchemaConstants.Entities.TaxRate)
            {
                ColumnSet = new ColumnSet(
                    SchemaConstants.TaxRate.Zip,
                    SchemaConstants.TaxRate.Rate),
                TopCount = 5,
                NoLock = true
            };
            query.Criteria.AddCondition(
                SchemaConstants.TaxRate.State, ConditionOperator.Equal, state);
            query.Criteria.AddCondition(
                SchemaConstants.TaxRate.Zip, ConditionOperator.BeginsWith, cleaned);
            query.AddOrder(SchemaConstants.TaxRate.Zip, OrderType.Ascending);

            var results = _service.RetrieveMultiple(query).Entities;
            if (results.Count == 0)
            {
                _tracing.Trace(
                    "TaxRateService: no eb_taxrate match for state='{0}', zip starts with '{1}'.",
                    state, cleaned);
                return null;
            }

            if (results.Count > 1)
            {
                _tracing.Trace(
                    "TaxRateService: {0} rows match state='{1}', zip starts with '{2}'. " +
                    "Using lowest ZIP '{3}'. Enter the full 9-digit ZIP on the opportunity " +
                    "to avoid ambiguity at city/transit-district boundaries.",
                    results.Count, state, cleaned,
                    results[0].GetAttributeValue<string>(SchemaConstants.TaxRate.Zip));
            }

            var rate = results[0].GetAttributeValue<decimal?>(SchemaConstants.TaxRate.Rate);
            _tracing.Trace(
                "TaxRateService: resolved rate {0} for state='{1}', zip starts with '{2}'.",
                rate, state, cleaned);
            return rate;
        }

        /// Cleans user-entered ZIPs to the digit-only form stored in
        /// eb_taxrate. Tolerant of spaces, dashes, and any other
        /// punctuation typed by accident.
        internal static string StripNonDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (ch >= '0' && ch <= '9') sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
