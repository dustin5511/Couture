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

Create the following custom fields before deploying the assembly. The
**Opportunity** column set drives both the existing rollup and the new
quote-creation validation / tax calculation pipeline:

### Opportunity (Project)

| Schema name                        | Type      | Notes                                                                 |
| ---------------------------------- | --------- | --------------------------------------------------------------------- |
| `eb_activewonquotestotal`          | Money     | Populated by `OpportunityQuoteRollupPlugin`.                          |
| `eb_jobsitezip`                    | Text (10) | Delivery ZIP. Required before a Quote can be created. Accepts 5-digit, 9-digit dashed (`55337-1234`), or 9-digit undashed (`553371234`); the plugin strips non-digits and uses `BeginsWith` against `eb_taxrate`. |
| `eb_deliverypreference`            | Choice    | Values: `1 = FOB`, `2 = Delivery`, `3 = FOB and Delivery`. Required before a Quote can be created. |
| `eb_jobsitestreet1`                | Text      | Copied onto Quote `shipto_line1` at quote Create.                     |
| `eb_jobsitestreet2`                | Text      | Copied onto Quote `shipto_line2` at quote Create.                     |
| `eb_jobsitecity`                   | Text      | Copied onto Quote `shipto_city` at quote Create.                      |
| `eb_jobsitestate`                  | Text      | Copied onto Quote `shipto_stateorprovince` at quote Create.           |
| `eb_jobsitecountry`                | Text      | Copied onto Quote `shipto_country` at quote Create.                   |
| `eb_customerjobprojectnumber`      | Text      | Customer's internal job/project reference. Copied onto Quote `eb_customerjobprojectnumber` at quote Create. |

### Quote

| Schema name                        | Type   | Notes                                                                            |
| ---------------------------------- | ------ | -------------------------------------------------------------------------------- |
| `eb_fobtotal`                      | Money            | Σ (`priceperunit × quantity`) across line items. Never includes tax.            |
| `eb_totaldeliveredtrailer`         | Money            | Σ (`eb_deliveredpricetrailer × qty`) + `eb_taxtotaltrailer` when preference is Delivery (2) or FOB+Delivery (3). |
| `eb_totaldeliveredstraight`        | Money            | Σ (`eb_deliveredpricestraight × qty`) + `eb_taxtotalstraight` when preference is Delivery (2) or FOB+Delivery (3). |
| `eb_taxtotaltrailer`               | Money            | Σ of per-line `eb_taxamounttrailer`.                                            |
| `eb_taxtotalstraight`              | Money            | Σ of per-line `eb_taxamountstraight`.                                           |
| `eb_taxratepercent`                | Decimal (4 dp)   | Tax rate applied to this quote, stored as a **percentage** (e.g. `8.125` for 8.125%). Source rate from `eb_taxrate.eb_rate` is a fraction; plugin multiplies by 100 before writing. Word template renders it as `{eb_taxratepercent}%`. |
| `eb_deliverypreference`            | Choice           | Per-quote copy of the project's preference. Values: `1 = FOB`, `2 = Delivery`, `3 = FOB and Delivery`. Seeded from the parent Opportunity at quote Create; editable on the quote afterward. All downstream tax / delivered-total calculations read from this field, so each quote under a project can model a different scenario. |
| `eb_customerjobprojectnumber`      | Text             | Customer's internal job/project reference. Seeded from the parent Opportunity at quote Create. |
| `eb_ownerfullname`                 | Text             | Denormalized from owning user's `fullname` at Create. Word Template XML mapper can't traverse `ownerid → systemuser`, so the contact details get stamped here. Create-only — not refreshed on Assign. |
| `eb_owneremail`                    | Text             | Denormalized from owning user's `internalemailaddress` at Create.    |
| `eb_ownerdirect`                   | Text             | Denormalized from owning user's `address1_telephone1` at Create.     |
| `eb_ownermobile`                   | Text             | Denormalized from owning user's `mobilephone` at Create.             |
| `eb_ownerfax`                      | Text             | Denormalized from owning user's `address1_fax` at Create.            |
| `eb_ownertitle`                    | Text             | Denormalized from owning user's `jobtitle` at Create.                |

### Quote Product (quotedetail)

| Schema name                        | Type   | Notes                                                                            |
| ---------------------------------- | ------ | -------------------------------------------------------------------------------- |
| `eb_taxamounttrailer`              | Money  | `eb_deliveredpricetrailer × quantity × rate` (0 for FOB-only preference or incoming material). |
| `eb_taxamountstraight`             | Money  | `eb_deliveredpricestraight × quantity × rate` (0 for FOB-only preference or incoming material). |
| `eb_unitpricetrailerwithtax`       | Money  | Per-ton delivered unit price including tax: `eb_deliveredpricetrailer × (1 + rate)` (0 for FOB-only preference or incoming material). Customer-facing column in the quote-line grid and on each line of the printed Word template. |
| `eb_unitpricestraightwithtax`      | Money  | Per-ton delivered unit price including tax: `eb_deliveredpricestraight × (1 + rate)` (0 for FOB-only preference or incoming material). Customer-facing column in the quote-line grid and on each line of the printed Word template. |

### Tax Rate (`eb_taxrate`)

The tax calculation depends on the three-column `eb_taxrate` table from
the **CoutureTaxRates** solution (`solutions/CoutureTaxRates/`):
`eb_state`, `eb_zip`, `eb_rate` (plus the system primary name). The
plugin queries it with `eb_state = "MN"` and `eb_zip BeginsWith
<cleaned jobsite ZIP>` and reads `eb_rate` as a **fraction** (e.g.
`0.081250` = 8.125%). Build that solution's table before deploying the
assembly.

If your publisher prefix is not `eb_`, or any of the column names
above differ in your environment, edit the corresponding constant in
`Constants/SchemaConstants.cs` before building. The `MinnesotaStateName`
constant on `SchemaConstants.TaxRate` is the literal the lookup uses; the
refresh flow writes the same literal into `eb_taxrate.eb_state`.

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

| #  | Plugin class                          | Message | Primary entity | Stage             | Order | Filtering attrs                                                                                                | Images                                                                                                                                       |
| -- | ------------------------------------- | ------- | -------------- | ----------------- | ----- | -------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| 1  | `ValidateFreightBeforeQuoteCreate`    | Create  | quote          | PreValidation 10  | 1     | –                                                                                                              | –                                                                                                                                            |
| 2  | `QuoteWinPlugin`                      | Win     | quote          | PreOperation 20   | –     | –                                                                                                              | –                                                                                                                                            |
| 3  | `QuoteWinPlugin`                      | Win     | quote          | PostOperation 40  | –     | –                                                                                                              | –                                                                                                                                            |
| 4  | `QuoteClosePlugin`                    | Close   | quote          | PostOperation 40  | –     | –                                                                                                              | –                                                                                                                                            |
| 5  | `OpportunityQuoteRollupPlugin`        | Create  | quote          | PostOperation 40  | –     | –                                                                                                              | –                                                                                                                                            |
| 6  | `OpportunityQuoteRollupPlugin`        | Update  | quote          | PostOperation 40  | –     | totalamount, statecode, statuscode, opportunityid                                                              | PreImage `preImage`: opportunityid, statecode                                                                                                |
| 7  | `OpportunityQuoteRollupPlugin`        | Delete  | quote          | PostOperation 40  | –     | –                                                                                                              | PreImage `preImage`: opportunityid                                                                                                           |
| 8  | `PopulateIncomingMaterialFlag`        | Create  | quotedetail    | PreOperation 20   | 1     | –                                                                                                              | –                                                                                                                                            |
| 9  | `CalculateDeliveryPricingPlugin`      | Create  | quotedetail    | PostOperation 40  | 2     | –                                                                                                              | PostImage `PostImage`: priceperunit, quantity, eb_isincomingmaterial, quoteid, productid                                                     |
| 10 | `CalculateDeliveryPricingPlugin`      | Update  | quotedetail    | PostOperation 40  | 2     | priceperunit, quantity, eb_isincomingmaterial                                                                  | PostImage `PostImage`: priceperunit, quantity, eb_isincomingmaterial, quoteid, productid                                                     |
| 11 | `CalculateTaxPlugin`                  | Create  | quotedetail    | PostOperation 50  | –     | –                                                                                                              | PostImage `PostImage`: priceperunit, quantity, eb_isincomingmaterial, quoteid, eb_deliveredpricetrailer, eb_deliveredpricestraight           |
| 12 | `CalculateTaxPlugin`                  | Update  | quotedetail    | PostOperation 50  | –     | priceperunit, quantity, eb_isincomingmaterial, eb_deliveredpricetrailer, eb_deliveredpricestraight              | PostImage `PostImage`: priceperunit, quantity, eb_isincomingmaterial, quoteid, eb_deliveredpricetrailer, eb_deliveredpricestraight           |
| 13 | `RecalcDeliveryOnProjectChange`       | Update  | opportunity    | PostOperation 40  | –     | eb_shippingrateperhour, eb_cycletime, eb_loadtime, eb_unloadtime                                               | PostImage `PostImage`: eb_shippingrateperhour, eb_cycletime, eb_loadtime, eb_unloadtime                                                      |
| 14 | `RecalcOnQuoteDeliveryPreferenceChange` | Update | quote        | PostOperation 40  | –     | eb_deliverypreference                                                                                          | –                                                                                                                                            |

Notes:

- `ValidateFreightBeforeQuoteCreate` runs at **PreValidation (10)** rather than PreOperation. PreValidation fires *outside* the database transaction so a thrown `InvalidPluginExecutionException` is rolled back instantly with a clean error dialog and no half-completed work.
- `PopulateIncomingMaterialFlag` runs at **PreOperation (20)** with Execution Order 1 on `quotedetail` Create — it mutates the Target directly (no extra Update) so the platform's insert carries the correct flag, and `CalculateDeliveryPricingPlugin` at rank 2 sees it.
- `CalculateDeliveryPricingPlugin` writes per-line delivered prices, extended amounts, **and** rolls up the five Quote-level totals (FOB, delivered trailer/straight, tax trailer/straight). The rollup includes the per-line tax fields written by `CalculateTaxPlugin` at rank 50 on subsequent passes.
- `RecalcDeliveryOnProjectChange` cascades opportunity freight-input edits to every active quote underneath, then runs the same 5-field rollup so the quote-level totals stay consistent.
- `RecalcOnQuoteDeliveryPreferenceChange` re-fires the line-level tax + rollup when the user edits `eb_deliverypreference` on a quote. It touches each child `quotedetail` by writing its current quantity back, which trips the filter attribute on the downstream pricing plugins so they re-read the (now updated) preference from the quote and produce new tax amounts and totals.

## 4. Behaviour summary

### Creating a quote

`ValidateFreightBeforeQuoteCreate` (PreValidation, Create on quote) reads
the parent Opportunity and throws `InvalidPluginExecutionException` with
a single bulleted error message listing every missing field across two
categories:

- Freight inputs: `eb_shippingrateperhour`, `eb_cycletime`, `eb_loadtime`,
  `eb_unloadtime`.
- Tax inputs: `eb_jobsitezip`, `eb_deliverypreference`.

The error surfaces on the form so the user can correct the Project record
without leaving the page.

On success the plugin also:

- Copies `eb_deliverypreference` from the parent Project onto the new
  Quote. From this point on every tax / delivered-total calculation
  reads the preference from the **quote**, so the user can change it
  per-quote (e.g. one FOB scenario, one Delivery scenario on the same
  project). Editing the quote's `eb_deliverypreference` fires
  `RecalcOnQuoteDeliveryPreferenceChange`, which re-triggers each line
  item's pricing/tax calc so the totals reflect the new preference.
- Copies the Project's job-site address (`eb_jobsitestreet1/2`, city,
  state, ZIP, country) onto the Quote's standard `shipto_*` columns
  so the printed quote shows the delivery address without any extra
  lookup. The seed skips any `shipto_*` field the user already typed
  on the form before save, and skips any source column that's blank.
- Copies `eb_customerjobprojectnumber` from the Project to the Quote
  so the customer's internal job/PO reference travels with the quote.
- Stamps the owning user's contact info into the `eb_owner*` columns
  on the Quote (`fullname`, `email`, `direct`, `mobile`, `fax`,
  `title`). Word Template's XML mapper can't traverse `ownerid →
  systemuser`, so denormalizing the values is the only way to get
  them onto the printed quote. Owner is read from `target["ownerid"]`
  when explicitly set on the form, otherwise from the calling user
  (`InitiatingUserId`). Team-owned quotes skip the stamp. This is a
  Create-only seed — reassigning the quote later does **not** refresh
  these fields.
- Looks up the current tax rate via `TaxRateService` and stamps
  `eb_taxratepercent` (as a percentage – e.g. `8.125`) directly on the
  target row before insert, so a freshly created Quote already shows
  the applied rate even before any line items exist.
  `CalculateTaxPlugin` re-syncs this field whenever line tax is
  recalculated, which keeps it honest if the opportunity ZIP changes
  after the quote was created.

### Calculating tax on a quote line

`CalculateTaxPlugin` runs on quote-detail Create/Update **after**
`CalculateDeliveryPricingPlugin`, so the trailer and straight-truck
delivered prices are already on the row. The logic per Delivery
Preference value (mirrored for both the qty-dependent tax dollar
amounts **and** the qty-independent with-tax unit prices):

| Preference          | `eb_taxamount*` (× qty)                            | `eb_unitprice*withtax` (per ton)                    |
| ------------------- | -------------------------------------------------- | --------------------------------------------------- |
| `1` FOB             | 0                                                  | 0                                                   |
| `2` Delivery        | `deliveredprice × qty × rate` (0 if incoming)      | `deliveredprice × (1 + rate)` (0 if incoming)       |
| `3` FOB + Delivery  | same as Delivery                                   | same as Delivery                                    |

Rate is resolved by `TaxRateService.LookupCombinedRate("MN",
opportunity.eb_jobsitezip)`. The service strips non-digit characters from
the ZIP and queries `eb_taxrate` with `BeginsWith`, so user-entered ZIPs
in any of the three common formats (5-digit, 9-digit dashed, 9-digit
undashed) all resolve. If no row matches the plugin throws – we'd rather
fail a save than silently store $0 tax on a six-figure order.

### Aggregating Quote totals

`CalculateDeliveryPricingPlugin.RollUpQuoteTotals` runs at the end of
every quote-detail Create / Update (and is also called by
`RecalcDeliveryOnProjectChange` when freight inputs change on the
opportunity). It rewrites all five Quote-level totals:

```
eb_fobtotal               = Σ priceperunit × quantity                  (always)
eb_taxtotaltrailer        = Σ eb_taxamounttrailer                      (always)
eb_taxtotalstraight       = Σ eb_taxamountstraight                     (always)
eb_totaldeliveredtrailer  = Σ eb_deliveredpricetrailer × qty
                            + eb_taxtotaltrailer  (if pref ∈ {2, 3})
eb_totaldeliveredstraight = Σ eb_deliveredpricestraight × qty
                            + eb_taxtotalstraight (if pref ∈ {2, 3})
```

FOB Total never includes tax. Tax flows into the delivered totals only
when the customer is actually taking delivery (preferences 2 and 3) – for
FOB-only orders the per-line tax fields are 0 anyway, so the math
collapses cleanly without a branch in the rollup.

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
