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
            public const string Opportunity = "opportunity";
            public const string OpportunityClose = "opportunityclose";
        }

        internal static class Quote
        {
            public const string Id = "quoteid";
            public const string OpportunityId = "opportunityid";
            public const string TotalAmount = "totalamount";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Name = "name";
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
