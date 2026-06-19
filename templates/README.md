# Kraemer Quote Document Template

`Quote_Template.docx` is the Dynamics 365 Word template used to render a
Quote in the format of `KraemerQ-3595.pdf` (the sample provided by the
customer).  `Quote_Template.original.docx` is the prior version, kept for
reference.

## What's bound to D365 already

The following content controls are bound to the standard Quote schema and
will populate automatically when the template is uploaded to Dynamics:

| Section            | Field on the page                | XPath                                                              |
| ------------------ | -------------------------------- | ------------------------------------------------------------------ |
| Customer block     | Customer name                    | `quote/customeridname`                                             |
|                    | Address line 1                   | `quote/quote_customer_accounts/address1_line1`                     |
|                    | Address line 2                   | `quote/quote_customer_accounts/address1_line2`                     |
|                    | City, State, Postal              | `quote/quote_customer_accounts/address1_city / _stateorprovince / _postalcode` |
| Quote header       | Quote number (Q-####)            | `quote/quotenumber`                                                |
|                    | Date                             | `quote/effectivefrom`                                              |
|                    | Expiration date                  | `quote/effectiveto`                                                |
|                    | Project Name                     | `quote/opportunity_quotes/name`                                    |
| Products table     | Repeating row                    | `quote/quote_details`                                              |
|                    | Product name                     | `productidname`                                                    |
|                    | Product description (italic)     | `productdescription`                                               |
|                    | Quantity + UoM (e.g. `9,000 T`)  | `quantity` and `uomidname`                                         |
|                    | Picked up unit price             | `priceperunit`                                                     |
| Bottom block       | Quoted By                        | `quote/owner_quotes/name`                                          |

## Placeholder content controls (need to be mapped in Word)

Open the template in Word, turn on the **XML Mapping Pane** (Developer →
XML Mapping Pane), pick the `urn:microsoft-crm/document-template/quote/1084/`
custom XML part, then right-click the matching field in the tree and choose
**Insert Content Control → Plain Text** to bind each placeholder. The
placeholders are deliberately greyed-out text wrapped in plain-text content
controls so they are easy to spot and replace.

| Placeholder shown in template | Suggested mapping                                     |
| ----------------------------- | ----------------------------------------------------- |
| `[Attention contact]`         | `quote/quote_customer_contacts[1]/fullname` (or any custom contact field you keep on the quote) |
| `[Plant Name]`                | A new custom field on Quote, e.g. `eb_plantname`      |
| `[Note text]`                 | `quote/description` *or* a custom `eb_quotenote`      |
| `[Direct Phone]`              | `quote/owner_quotes[1]/internalemailaddress` style — pick the user's phone field, e.g. `address1_telephone1` |
| `[Mobile Phone]`              | `quote/owner_quotes[1]/mobilephone`                   |
| `[Fax Number]`                | `quote/owner_quotes[1]/address1_fax`                  |
| `[Email]`                     | `quote/owner_quotes[1]/internalemailaddress`          |
| `[Approximate Start Date]`    | A new custom field, e.g. `eb_approximatestartdate`    |
| `[Customer Job/Project Number]` | A new custom field on Opportunity or Quote, e.g. `eb_customerjobnumber` |
| `[Signature]`                 | Leave as a blank / hand-signed line, or bind to an electronic-signature field |
| `[Insert Kraemer logo here]`  | Replace with **Insert → Picture** of the company logo |

If a custom field doesn't exist yet in your environment, add it to the
Quote (or related entity) in the maker portal first, **then re-upload the
template** so the entity schema picker offers the new field. The
placeholder text will not break the template if a field is left unmapped –
it just renders as that grey text.

## Layout reference

The layout matches `KraemerQ-3595.pdf` as closely as is practical with
content-control-only Word features:

* Two-column header — logo / company address on the left, Customer block
  on the right.
* Left-aligned QUOTE / Date / Expiration / Project / Plant block.
* Red horizontal divider between every major section (paragraph border,
  hex `#C81E1E` ≈ Kraemer red).
* Three-column products table (Product / Quantity / Picked up unit price)
  with a repeating section bound to `quote_details`.
* Static bulleted Terms & Conditions exactly as on the sample quote.
* Four-column footer block – `QUOTED BY` / contact lines on the left,
  signature lines for `APPROXIMATE START DATE`, `CUSTOMER JOB/PROJECT
  NUMBER`, and `JOB AWARDED BY/SIGNATURE` on the right.
* Word page footer – red banner with the company name, dispatch number
  and `Page X of Y` (`footer1.xml`).

## Uploading to Dynamics 365

1. In the model-driven app: **Settings → Templates → Document Templates**.
2. Click **Upload Template** and pick `templates/Quote_Template.docx`.
3. Open any Quote, then **Word Templates** → select the template – the
   document downloads pre-populated.

## Iterating on the template

If you need to tweak the layout, edit `templates/Quote_Template.docx`
directly in Word (don't re-export the existing one from D365 – that round
trip strips placeholder content controls). Commit the updated `.docx` to
this repo so the change is versioned alongside the plug-in code.
