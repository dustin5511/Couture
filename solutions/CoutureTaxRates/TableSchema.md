# `eb_taxrate` table — manual build path

If the packed solution doesn't import cleanly in your environment (the
hand-crafted `Entity.xml` is best-effort), build the table by clicking
through the maker portal. It takes about 10 minutes.

## 1. Create the table

`make.powerapps.com` → pick the target environment → **Tables** → **+ New
table** → **Table (advanced properties)**.

| Property                | Value                                |
| ----------------------- | ------------------------------------ |
| Display name            | `Tax Rate`                           |
| Plural display name     | `Tax Rates`                          |
| Schema name             | `eb_taxrate`                         |
| Primary column display  | `Name`                               |
| Primary column schema   | `eb_name`                            |
| Primary column max len  | `60`                                 |
| Enable for              | nothing extra (no activities, audit on if you want) |
| Ownership               | Organization                         |

Save.

## 2. Add columns

Inside the new table → **Columns** tab → **+ New column** for each:

| Display name           | Schema name           | Data type      | Format       | Required        | Max len / precision |
| ---------------------- | --------------------- | -------------- | ------------ | --------------- | ------------------- |
| State                  | `eb_state`            | Single line    | Text         | **Required**    | 2                   |
| ZIP                    | `eb_zip`              | Single line    | Text         | **Required**    | 10                  |
| City                   | `eb_city`             | Single line    | Text         | Optional        | 100                 |
| County                 | `eb_county`           | Single line    | Text         | Optional        | 100                 |
| State Rate             | `eb_staterate`        | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| City Rate              | `eb_cityrate`         | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| County Rate            | `eb_countyrate`       | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| Transit Rate           | `eb_transitrate`      | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| Special District Rate  | `eb_specialrate`      | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| Combined Rate          | `eb_combinedrate`     | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |
| Effective Date         | `eb_effectivedate`    | Date Only      | Date Only    | Optional        | —                   |
| Last Refreshed On      | `eb_lastrefreshedon`  | Date and Time  | DateAndTime  | Optional        | UserLocal           |
| Source URL             | `eb_sourceurl`        | Single line    | URL          | Optional        | 500                 |

Decimal columns: store the rate as a fraction (`0.068750` = 6.875%). If you'd
rather store percentages, change the precision to 4 and divide in the flow.

## 3. Add the alternate key

In the new table → **Keys** tab → **+ New key**.

| Property      | Value                          |
| ------------- | ------------------------------ |
| Display name  | `State + ZIP`                  |
| Name          | `eb_taxrate_state_zip`         |
| Columns       | `eb_state`, `eb_zip` (in that order) |

Wait for the system job that builds the supporting index to finish (status
goes from *Pending* → *Active*) before you turn the flow on, or the upsert
calls will 400.

## 4. (Optional) Configuration row

The flow reads the source URL from a row in `eb_configuration` so you can
change it without re-publishing the flow. If you don't already have an
`eb_configuration` table, either:

- create a tiny one with columns `eb_name` (primary, e.g.
  `MN_TaxRateSourceUrl`) and `eb_value` (Text 500), then add a row, **or**
- hard-code the URL in the flow's first **Compose** action and remove the
  `List rows — eb_configuration` step.

## 5. Add a model-driven view

So users can browse the data:

`Tables → eb_taxrate → Views → + New view`:

| Column | Width |
| ------ | ----- |
| Name | 200 |
| State | 80 |
| ZIP | 100 |
| City | 200 |
| Combined Rate | 120 |
| Effective Date | 120 |
| Last Refreshed On | 160 |

Sort by `eb_state` ascending, then `eb_zip` ascending.

---

Once the table exists, jump back to the README and import only the flow
JSON from `flows/TaxRateRefresh-MN.json`.
