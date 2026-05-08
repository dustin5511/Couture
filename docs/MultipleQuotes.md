# Multiple Active Quotes plugin

Out-of-the-box Dynamics 365 only allows a single active quote per opportunity
and will close the opportunity as Won the moment any quote is won. The
`Couture.Plugins.MultipleQuotes` assembly changes that behaviour:

* Multiple quotes can be Active on the same opportunity at the same time.
* Winning a quote only closes the opportunity as Won when **every other**
  quote on that opportunity is already in a closed state (Won/Lost/Cancelled).
* Closing (or cancelling) the final open quote on an opportunity cascades to
  the opportunity: if any sibling was Won → opportunity is closed Won, if all
  are Lost/Cancelled → opportunity is closed Lost.
* A money field on the opportunity always reflects the sum of `totalamount`
  for every quote currently in Active or Won state.
* The user can still close the opportunity manually – the plugin never
  overrides a state that was already set to Won/Lost before we run.

## 1. Customisations required on the environment

Create the following custom field on the **Opportunity** table before
deploying the assembly:

| Schema name                        | Type  | Notes                                        |
| ---------------------------------- | ----- | -------------------------------------------- |
| `new_activewonquotestotal`         | Money | Populated by `OpportunityQuoteRollupPlugin`. |

If your publisher prefix is not `new_`, edit
`Constants/SchemaConstants.Opportunity.ActiveWonQuotesTotal` before building.

## 2. Build

```powershell
dotnet build src/Couture.Plugins.MultipleQuotes/Couture.Plugins.MultipleQuotes.csproj -c Release
```

The output `bin/Release/net462/Couture.Plugins.MultipleQuotes.dll` is the
assembly to register with the Plugin Registration Tool.

> The assembly must be strong-name signed. Generate a key once with
> `sn -k src/Couture.Plugins.MultipleQuotes/Couture.Plugins.MultipleQuotes.snk`.

## 3. Steps to register

All steps are **synchronous** and run as the calling user.

| # | Plugin class                       | Message | Primary entity | Stage            | Filtering attrs                                    | Images                                                |
| - | ---------------------------------- | ------- | -------------- | ---------------- | -------------------------------------------------- | ----------------------------------------------------- |
| 1 | `QuoteWinPlugin`                   | Win     | quote          | PreOperation 20  | –                                                  | –                                                     |
| 2 | `QuoteWinPlugin`                   | Win     | quote          | PostOperation 40 | –                                                  | –                                                     |
| 3 | `QuoteClosePlugin`                 | Close   | quote          | PostOperation 40 | –                                                  | –                                                     |
| 4 | `OpportunityQuoteRollupPlugin`     | Create  | quote          | PostOperation 40 | –                                                  | –                                                     |
| 5 | `OpportunityQuoteRollupPlugin`     | Update  | quote          | PostOperation 40 | totalamount, statecode, statuscode, opportunityid  | PreImage `preImage`: opportunityid, statecode         |
| 6 | `OpportunityQuoteRollupPlugin`     | Delete  | quote          | PostOperation 40 | –                                                  | PreImage `preImage`: opportunityid                    |
| 7 | `CalculateDeliveryPricingPlugin`   | Create  | quotedetail    | PostOperation 40 | –                                                  | PostImage `PostImage`: priceperunit, eb_isincomingmaterial, quoteid, productid |
| 8 | `CalculateDeliveryPricingPlugin`   | Update  | quotedetail    | PostOperation 40 | priceperunit, eb_isincomingmaterial                | PostImage `PostImage`: priceperunit, eb_isincomingmaterial, quoteid, productid |

## 4. Behaviour summary

### Winning a quote

1. Pre-operation collects any sibling quotes currently in Draft/Active.
2. Platform performs its default Win: quote → Won, siblings → Revised,
   opportunity → Won.
3. Post-operation inspects the pre-op list. If it was non-empty the plugin:
   * Re-opens the opportunity (state = Open, status = In Progress).
   * Re-activates each sibling that the platform auto-revised.
   * Refreshes the rollup field.
4. If it was empty the default cascade stands and we only refresh the rollup.

### Closing (lost/cancelled) a quote

1. Refresh rollup so the closed quote's amount drops out immediately.
2. If the opportunity is already closed – stop (the user finalised it
   themselves).
3. Otherwise evaluate every quote on the opportunity:
   * Any still open → no cascade.
   * Any Won → close opportunity as Won.
   * All Lost/Cancelled → close opportunity as Lost.

### Rollup refresh

Runs on quote Create / Update / Delete. The filter list means a plain field
edit (e.g. a description change) is free; the refresh only fires when
something that influences the sum actually changes. Re-parenting a quote to a
new opportunity updates **both** opportunities.

## 5. Recursion / transaction safety

* Post-op plugins call `SetStateRequest` / `WinOpportunityRequest` /
  `LoseOpportunityRequest`. These execute inside the parent transaction, so
  intermediate rollup values are never committed – only the final refresh at
  the end of each cascade persists.
* The rollup write targets the **opportunity** but the rollup plugin only
  fires on **quote** Create/Update/Delete, so it cannot recurse into itself.
  A `Depth > 3` guard is kept as a belt-and-braces safeguard against
  unrelated chains of plugins triggering quote updates.

## 6. Manual overrides

The plugin never mutates an opportunity that is not in the Open state at the
moment it runs. If a user manually closes the opportunity as Won/Lost while
quotes are still active, subsequent quote state changes will only refresh the
rollup, not re-open the opportunity.
