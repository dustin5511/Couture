# Couture Tax Rates solution

A Power Platform **unmanaged solution** that adds a single multi-state
Dataverse table:

> **`eb_taxrate`** — sales-tax rate by `(state, ZIP)`, with all the columns
> needed to store a refresh of state-DOR rate spreadsheets.

This solution intentionally contains **only the table** — no cloud flow, no
forms, no roles. The companion refresh flow lives as standalone JSON in
[`flows/TaxRateRefresh-MN.json`](./flows/TaxRateRefresh-MN.json) and is
imported separately once the table exists.

---

## Quick start

```powershell
# 1. From repo root, pack the solution into a ZIP
cd solutions\CoutureTaxRates
.\build.ps1                       # or: pac solution pack -p Unmanaged -z .\bin\CoutureTaxRates.zip -f .\src

# 2. Import via maker portal:
#    https://make.powerapps.com  >  Solutions  >  Import solution
#    Pick bin\CoutureTaxRates.zip
```

After import you'll find a new table **Tax Rate** with the columns and
alternate key listed below.

If the import fails (hand-crafted Dataverse Entity.xml is sensitive to schema
quirks), use the **manual path** in [`TableSchema.md`](./TableSchema.md):
build the table by clicking through the maker portal in ~10 minutes. The
result is functionally identical.

---

## Folder layout

```
solutions/CoutureTaxRates/
├── README.md                       ← you are here
├── TableSchema.md                  ← manual-build recipe (fallback)
├── build.ps1                       ← pac solution pack helper
├── flows/
│   └── TaxRateRefresh-MN.json      ← standalone flow (NOT in the solution)
└── src/
    ├── Other/
    │   ├── Solution.xml            ← solution metadata + EdwardBros publisher
    │   ├── Customizations.xml      ← declares eb_taxrate as the only component
    │   └── Relationships.xml       ← (empty — no relationships)
    └── Entities/
        └── eb_taxrate/
            └── Entity.xml          ← table + columns + alternate key + default view
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
| `eb_lastrefreshedon`   | Last Refreshed On     | DateTime (User local) | Set by the flow on each upsert                |
| `eb_sourceurl`         | Source URL            | Text (500, URL) | The URL the row was downloaded from                |

**Alternate key:** `eb_taxrate_state_zip` over `(eb_state, eb_zip)`. The
refresh flow uses this for upserts so a monthly refresh is idempotent.

**Default view ("Active Tax Rates"):** shows Name / State / ZIP / City /
Combined Rate / Effective Date / Last Refreshed On, sorted by State then ZIP.

---

## Connecting the refresh flow

This solution does not include the cloud flow. After the table is in place:

1. Open `flows/TaxRateRefresh-MN.json`. The top-level object is the flow's
   `clientdata` payload (connection references + definition).
2. In Power Automate, **+ New flow → Import → Import Package (legacy)** is
   the path for full package imports. For just the JSON definition, the
   easiest route is:
   - Create a new scheduled cloud flow with the recurrence trigger.
   - Switch to the **"Edit in advanced mode"** code view and paste the
     `definition` portion.
   - Re-pick connection references (SharePoint, Excel Online, Dataverse,
     Outlook) since GUIDs in the JSON are placeholders.
3. Update the three placeholders documented at the top of the flow JSON:
   SharePoint site URL, Excel Online source/drive/file/table IDs, and
   the recipient email on the completion message.
4. Add an `eb_configuration` row named `MN_TaxRateSourceUrl` with the live
   MN DOR spreadsheet URL.
5. Turn the flow on.

---

## Adding other states later

The table is multi-state by design (the alternate key includes `eb_state`).
To add Wisconsin or Iowa:

1. Clone `flows/TaxRateRefresh-MN.json` → `TaxRateRefresh-WI.json`.
2. Add a new `eb_configuration` row `WI_TaxRateSourceUrl`.
3. In the cloned flow, change:
    - the configuration row name lookup,
    - the hard-coded `eb_state` value (`MN` → `WI`),
    - the column-name mappings in the Compose step (each state's file has
      slightly different headers).
4. Import the new flow.

No table changes required.
