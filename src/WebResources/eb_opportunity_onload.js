var Couture = Couture || {};
Couture.Opportunity = Couture.Opportunity || {};

Couture.Opportunity.DEFAULT_ACCOUNT_NAME = "Default (do not remove)";

Couture.Opportunity.DELIVERY_PREF_FOB = 1;

// Hidden + made optional when delivery preference = FOB.
Couture.Opportunity.HIDEABLE_FREIGHT_FIELDS = [
    "eb_cycletime",
    "eb_shippingrateperhour",
    "eb_straighttruckrateton",
    "eb_trailerrateton",
    "eb_loadtime",
    "eb_unloadtime",
    "eb_totaltripminutes"
];

// Stays visible regardless (FOB users can still capture lat/long for
// heat-mapping) but its required flag toggles with the preference.
Couture.Opportunity.JOBSITE_ZIP_FIELD = "eb_jobsitezip";

// ─────────────────────────────────────────────────────────────────────────
// onLoad — runs on every form load. Seeds placeholder defaults on Create
// and always applies the delivery-preference visibility rules.
// ─────────────────────────────────────────────────────────────────────────
Couture.Opportunity.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();

    Couture.Opportunity.applyDeliveryPreferenceVisibility(formContext);

    // Placeholder customer / price-list seed only fires on Create.
    if (formContext.ui.getFormType() !== 1) return; // 1 = Create

    var priceList = formContext.getAttribute("pricelevelid");
    if (!priceList || priceList.getValue()) return; // already set

    Xrm.WebApi.retrieveMultipleRecords(
        "account",
        "?$select=accountid,name,_defaultpricelevelid_value" +
        "&$filter=name eq '" + Couture.Opportunity.DEFAULT_ACCOUNT_NAME + "'" +
        "&$top=1"
    ).then(function (result) {
        if (!result.entities || result.entities.length === 0) return;

        var acct = result.entities[0];
        var plId = acct["_defaultpricelevelid_value"];
        if (!plId) return;

        var plName = acct["_defaultpricelevelid_value@OData.Community.Display.V1.FormattedValue"] || "Default";

        priceList.setValue([{
            id: plId,
            entityType: "pricelevel",
            name: plName
        }]);

        var parentAccount = formContext.getAttribute("parentaccountid");
        if (parentAccount) {
            parentAccount.setValue([{
                id: acct["accountid"],
                entityType: "account",
                name: acct["name"]
            }]);
        }

    }).catch(function (error) {
        console.error("Couture.Opportunity.onLoad: " + error.message);
    });
};

// ─────────────────────────────────────────────────────────────────────────
// Wire this as the OnChange handler on eb_deliverypreference.
// ─────────────────────────────────────────────────────────────────────────
Couture.Opportunity.onDeliveryPreferenceChange = function (executionContext) {
    Couture.Opportunity.applyDeliveryPreferenceVisibility(executionContext.getFormContext());
};

// ─────────────────────────────────────────────────────────────────────────
// Shared show/hide + required toggle. Called from onLoad and onChange.
// FOB → freight fields hidden + optional, ZIP optional but visible.
// Anything else → freight fields visible + required, ZIP required.
// ─────────────────────────────────────────────────────────────────────────
Couture.Opportunity.applyDeliveryPreferenceVisibility = function (formContext) {
    var prefAttr = formContext.getAttribute("eb_deliverypreference");
    if (!prefAttr) return;

    var pref = prefAttr.getValue();
    var isFob = pref === Couture.Opportunity.DELIVERY_PREF_FOB;

    // Freight fields: hide and un-require for FOB; show and require otherwise.
    Couture.Opportunity.HIDEABLE_FREIGHT_FIELDS.forEach(function (fieldName) {
        Couture.Opportunity._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ !isFob);
    });

    // Job site ZIP: always visible, required only when not FOB.
    Couture.Opportunity._setFieldState(formContext,
        Couture.Opportunity.JOBSITE_ZIP_FIELD,
        /* visible  */ true,
        /* required */ !isFob);
};

Couture.Opportunity._setFieldState = function (formContext, fieldName, visible, required) {
    var control = formContext.getControl(fieldName);
    if (control && control.setVisible) {
        control.setVisible(visible);
    }

    var attr = formContext.getAttribute(fieldName);
    if (attr && attr.setRequiredLevel) {
        attr.setRequiredLevel(required ? "required" : "none");
    }
};
