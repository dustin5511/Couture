# Couture Tax Rates solution

A Power Platform unmanaged solution that:

1. Adds a multi-state Dataverse table **`eb_taxrate`** for storing sales-tax
   rates keyed by `(state, ZIP)`.
2. Adds a scheduled **Power Automate cloud flow** (`Tax Rate Refresh - MN`)
   that downloads the Minnesota Department of Revenue rate spreadsheet
   once a month and upserts every ZIP row into `eb_taxrate`.

This directory is the **unpacked solution source** — i.e. the layout produced
by `pac solution unpack`. To deploy you pack it into a ZIP and import it via
the maker portal or `pac solution import`.

---

## Quick start

```powershell
# 1. From repo root, pack the solution into a ZIP
cd solutions\CoutureTaxRates
.\build.ps1                       # or: pac solution pack -p Unmanaged -z .\bin\CoutureTaxRates.zip -f .\src

# 2. Import via maker portal:
#    https://make.powerapps.com  >  Solutions  >  Import solution
#    Pick bin\CoutureTaxRates.zip

# 3. After import, open the new flow once and fix:
#      - the SharePoint connection reference
#      - the Dataverse connection reference
#      - the eb_configuration row "MN_TaxRateSourceUrl" with the live MN DOR URL
#      - the SharePoint site/library where the downloaded file should land
#    Then turn the flow ON.
```

If the packed solution fails to import (the hand-rolled XML is best-effort —
Dataverse schema XML is sensitive to formatting), use the **manual path** in
[`TableSchema.md`](./TableSchema.md): build the table by clicking through the
maker portal in ~10 minutes, then import only the flow from
[`flows/TaxRateRefresh-MN.json`](./flows/TaxRateRefresh-MN.json).

---

## Folder layout

```
solutions/CoutureTaxRates/
├── README.md                       ← you are here
├── TableSchema.md                  ← column-by-column spec for eb_taxrate
├── build.ps1                       ← pac solution pack helper
├── flows/
│   └── TaxRateRefresh-MN.json      ← exportable flow definition (clientdata)
└── src/
    ├── Other/
    │   ├── Solution.xml            ← solution metadata + publisher
    │   ├── Customizations.xml      ← components list
    │   └── Relationships.xml       ← (empty — no relationships)
    ├── Entities/
    │   └── eb_taxrate/
    │       └── Entity.xml          ← table definition
    └── Workflows/
        └── TaxRateRefreshMN-<guid>.json  ← flow inside the solution
```

---

## What the table looks like

| Logical name           | Display name          | Type            | Notes                                              |
| ---------------------- | --------------------- | --------------- | -------------------------------------------------- |
| `eb_taxrateid`         | Tax Rate              | Uniqueidentifier| Primary key (auto)                                 |
| `eb_name`              | Name                  | Text (60)       | Primary name field, e.g. `MN-55337`                |
| `eb_state`             | State                 | Text (2)        | **Part of alternate key**, ISO 2-letter (MN, WI…)  |
| `eb_zip`               | ZIP                   | Text (10)       | **Part of alternate key**, supports ZIP+4          |
| `eb_city`              | City                  | Text (100)      |                                                    |
| `eb_county`            | County                | Text (100)      |                                                    |
| `eb_staterate`         | State Rate            | Decimal (6 dp)  | e.g. `0.068750` for 6.875%                         |
| `eb_cityrate`          | City Rate             | Decimal (6 dp)  |                                                    |
| `eb_countyrate`        | County Rate           | Decimal (6 dp)  |                                                    |
| `eb_transitrate`       | Transit Rate          | Decimal (6 dp)  |                                                    |
| `eb_specialrate`       | Special District Rate | Decimal (6 dp)  |                                                    |
| `eb_combinedrate`      | Combined Rate         | Decimal (6 dp)  | Sum of all of the above                            |
| `eb_effectivedate`     | Effective Date        | Date only       | From the source spreadsheet                        |
| `eb_lastrefreshedon`   | Last Refreshed On     | DateTime        | Set by the flow on each upsert                     |
| `eb_sourceurl`         | Source URL            | Text (500)      | The URL the row was downloaded from                |

**Alternate key:** `eb_taxrate_state_zip` over `(eb_state, eb_zip)`. The flow
uses this for `PATCH` upserts so a refresh is idempotent.

See [`TableSchema.md`](./TableSchema.md) for the exact maker-portal click path
if you'd rather build it by hand.

---

## What the flow does

**`Tax Rate Refresh - MN`** (recurrence trigger, 1st of every month at
02:00 UTC):

1. **Recurrence** — `Frequency: Month, Interval: 1, StartTime: 02:00, Day: 1`.
2. **List rows — `eb_configuration`** — read the row named
   `MN_TaxRateSourceUrl` to get the live MN DOR URL (so you don't need to
   re-publish the flow when the URL changes).
3. **HTTP — `GET <sourceUrl>`** — download the spreadsheet bytes.
4. **Create file** in SharePoint at
   `Documents/TaxRates/MN_Rates_<yyyy-MM-dd>.xlsx`.
5. **List rows present in a table** (Excel Online Business) on that file —
   the MN DOR spreadsheet must have a named table inside it (Excel → Format
   as Table). The flow expects the table to be called `Rates`.
6. **Apply to each row:**
    - Compose a parsed object (`zip`, `city`, `county`, rates, effective date)
      with explicit `float()` casts.
    - **Add a new row (Dataverse)** for the `eb_taxrate` table with the
      alternate key header `eb_taxrate_state_zip(eb_state='MN',eb_zip='<zip>')`
      so the operation is an *upsert*.
7. **Delete file** — the downloaded XLSX in SharePoint (audit copy kept in
   version history if you turn that on at the library level).
8. **Send email** to the owner with row counts.

### Why SharePoint + Excel Online?

Power Automate has no built-in XLSX parser. The Excel Online (Business)
connector reads named tables inside an XLSX file, but only if the file is in
OneDrive or SharePoint. The flow drops the file in SharePoint just long
enough to enumerate it. If your MN DOR file is published as CSV instead,
swap the Excel step for `compose` + `split(body('HTTP'), decodeUriComponent('%0A'))`
and parse line-by-line — see commented branch in `flows/TaxRateRefresh-MN.json`.

---

## The source file

Minnesota Department of Revenue publishes the rate data on the
**Sales Tax Rate Spreadsheets** page of their website (search "Minnesota
Department of Revenue sales tax rate spreadsheets"). The file is updated
quarterly. You'll find:

- A downloadable XLSX with one row per ZIP code, columns roughly:
  `ZIP Code | City | County | State Rate | County Rate | City Rate | Transit Rate | Special Rate | Combined Rate | Effective Date`.

**Action:** download the current file once manually, confirm the exact column
headers in the named table (case-sensitive!), and adjust the `Compose` step
in the flow if any header text differs. Then put the URL in the
`eb_configuration` row.

If you'd rather use the Streamlined Sales Tax (SST) project file (a
standardized multi-state TAR feed), see "Multi-state expansion" below.

---

## Adding other states later

The table is already multi-state (the alternate key includes `eb_state`).
To add Wisconsin or Iowa:

1. Clone `flows/TaxRateRefresh-MN.json` → `TaxRateRefresh-WI.json`.
2. Add a new `eb_configuration` row `WI_TaxRateSourceUrl`.
3. In the flow, change:
    - the configuration row name lookup
    - the hard-coded `eb_state` value (`MN` → `WI`)
    - the column-name mappings in the Compose step (each state's file has
      slightly different headers)
4. Import the new flow into the same solution.

For a single multi-state flow, use the Streamlined Sales Tax (SST) project
data — it's a single feed with a `State` column. The trade-off is the SST
file is less detailed than each state's native publication.

---

## Operational notes

- **First run** will create ~1,000 MN ZIP rows (~30 seconds at default
  concurrency). Subsequent runs only update changed rows, but the flow
  still iterates the full file. If you want a delta-only refresh, add a
  pre-filter step on the parsed rows comparing `eb_effectivedate` against
  the existing row's value.
- **Throttling:** the Dataverse connector caps at ~500 calls/min. The
  Apply-to-each step has `concurrencyControl.repetitions = 20` set in the
  JSON — bump it down if you hit 429s.
- **Failure handling:** the HTTP step is configured with `retryPolicy.count = 4`
  and exponential backoff. The flow runs as the owner; turn on owner-failure
  notifications in the flow properties so you find out when it breaks.
- **Cost:** the Excel Online and Dataverse connectors are both Standard
  (no premium licence required). The HTTP connector is **Premium** — every
  user/account that runs the flow needs a Power Automate Premium licence.
  If you don't want to take that licence dependency, use the SharePoint
  "Send an HTTP request to SharePoint" trick or have a human upload the
  file into a designated SharePoint folder and change the trigger to "When
  a file is created" instead.
