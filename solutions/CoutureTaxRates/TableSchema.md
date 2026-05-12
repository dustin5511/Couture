# `eb_taxrate` table — manual build path

If the packed solution doesn't import cleanly in your environment (the
hand-crafted `Entity.xml` is best-effort), build the table by clicking
through the maker portal. It takes about 5 minutes.

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
| Enable for              | nothing extra (audit on if you want) |
| Ownership               | Organization                         |

Save.

## 2. Add columns

Inside the new table → **Columns** tab → **+ New column** for each:

| Display name           | Schema name           | Data type      | Format       | Required        | Max len / precision |
| ---------------------- | --------------------- | -------------- | ------------ | --------------- | ------------------- |
| State                  | `eb_state`            | Single line    | Text         | **Required**    | 50                  |
| ZIP                    | `eb_zip`              | Single line    | Text         | **Required**    | 10                  |
| Rate                   | `eb_rate`             | Decimal Number | —            | Optional        | 6 dp, min 0, max 1  |

That's it — three columns. The plugin only reads these three plus the
system-managed `eb_name` primary column.

Decimal `eb_rate` stores the rate as a fraction (`0.068750` = 6.875%). The
refresh flow writes the MN DOR "Combined Rate" column straight into this
field with no transformation.

`eb_state` stores the 2-letter state code (`MN`). The refresh flow writes
that literal; the `CalculateTaxPlugin` queries by
`SchemaConstants.TaxRate.MinnesotaStateName` which is also `"MN"`. The
match is case-sensitive — keep them in sync. ZIPs in `eb_zip` are stored
as 9-digit strings
with no dashes (e.g. `553371234`). The plugin strips non-digits from the
opportunity's `eb_jobsitezip` and uses `BeginsWith`, so a 5-digit ZIP on the
opportunity still resolves to the right rate row.

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
| Name   | 200   |
| State  | 120   |
| ZIP    | 120   |
| Rate   | 120   |

Sort by `eb_state` ascending, then `eb_zip` ascending.

---

Once the table exists, jump back to the README and import only the flow
JSON from `flows/TaxRateRefresh-MN.json`.
