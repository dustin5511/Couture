# Couture

## Dynamics 365 – Multiple Active Quotes plugin

`src/Couture.Plugins.MultipleQuotes` contains a plug-in assembly that lets an
opportunity have multiple Active quotes at the same time, closes the
opportunity only when appropriate, and maintains a money rollup of all
Active/Won quote totals.

See [`docs/MultipleQuotes.md`](docs/MultipleQuotes.md) for the full design,
build steps and plugin registration table.

## Quote Word template

`templates/Quote_Template.docx` is the Dynamics 365 Word template that
renders a Quote in the Kraemer Mining & Materials house style. See
[`templates/README.md`](templates/README.md) for the field mapping guide.

## Tax rates solution

`solutions/CoutureTaxRates/` is an unpacked Power Platform solution with the
`eb_taxrate` Dataverse table and the monthly **Tax Rate Refresh - MN** cloud
flow that downloads the MN Department of Revenue rate spreadsheet and
upserts every ZIP row. See
[`solutions/CoutureTaxRates/README.md`](solutions/CoutureTaxRates/README.md)
for the build/import recipe.
