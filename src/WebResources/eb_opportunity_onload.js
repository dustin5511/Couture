var Couture = Couture || {};
Couture.Opportunity = Couture.Opportunity || {};

Couture.Opportunity.DEFAULT_ACCOUNT_NAME = "Default (do not remove)";

Couture.Opportunity.DELIVERY_PREF_FOB = 1;

// User-entered inputs that drive the freight math. Hidden + optional
// for FOB; visible + required for Delivery / FOB+Delivery.
Couture.Opportunity.FREIGHT_INPUT_FIELDS = [
    "eb_cycletime",
    "eb_shippingrateperhour"
];

// Calculated outputs the plugin writes. Hidden for FOB; visible (but
// read-only and never required) for Delivery / FOB+Delivery so the
// user can see the math but can't break it.
Couture.Opportunity.FREIGHT_OUTPUT_FIELDS = [
    "eb_straighttruckrateton",
    "eb_trailerrateton",
    "eb_totaltripminutes"
];

// Load and unload are hard-coded to 10 minutes each. JS keeps them in
// sync on every form load + preference change so the data layer is
// always consistent. Always hidden and disabled — the user never
// touches these.
Couture.Opportunity.LOAD_UNLOAD_FIELDS = [
    "eb_loadtime",
    "eb_unloadtime"
];
Couture.Opportunity.LOAD_UNLOAD_VALUE = 10;

// Job site ZIP — always visible, required only when not FOB.
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

// OnChange handler on eb_deliverypreference
Couture.Opportunity.onDeliveryPreferenceChange = function (executionContext) {
    Couture.Opportunity.applyDeliveryPreferenceVisibility(executionContext.getFormContext());
};

// ─────────────────────────────────────────────────────────────────────────
// Shared show/hide + required + read-only toggle.
// ─────────────────────────────────────────────────────────────────────────
Couture.Opportunity.applyDeliveryPreferenceVisibility = function (formContext) {
    var prefAttr = formContext.getAttribute("eb_deliverypreference");
    if (!prefAttr) return;

    var pref = prefAttr.getValue();
    var isFob = pref === Couture.Opportunity.DELIVERY_PREF_FOB;

    // Inputs — visible + required when not FOB.
    Couture.Opportunity.FREIGHT_INPUT_FIELDS.forEach(function (fieldName) {
        Couture.Opportunity._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ !isFob,
            /* disabled */ false);
    });

    // Outputs — visible when not FOB, but always read-only, never required.
    Couture.Opportunity.FREIGHT_OUTPUT_FIELDS.forEach(function (fieldName) {
        Couture.Opportunity._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ false,
            /* disabled */ true);
    });

    // Load / unload — always hidden, disabled, force-set to 10.
    Couture.Opportunity.LOAD_UNLOAD_FIELDS.forEach(function (fieldName) {
        var attr = formContext.getAttribute(fieldName);
        if (attr && attr.getValue() !== Couture.Opportunity.LOAD_UNLOAD_VALUE) {
            attr.setValue(Couture.Opportunity.LOAD_UNLOAD_VALUE);
        }
        Couture.Opportunity._setFieldState(formContext, fieldName,
            /* visible  */ false,
            /* required */ false,
            /* disabled */ true);
    });

    // Job site ZIP — always visible, required only when not FOB.
    Couture.Opportunity._setFieldState(formContext,
        Couture.Opportunity.JOBSITE_ZIP_FIELD,
        /* visible  */ true,
        /* required */ !isFob,
        /* disabled */ false);
};

Couture.Opportunity._setFieldState = function (formContext, fieldName, visible, required, disabled) {
    var control = formContext.getControl(fieldName);
    if (control) {
        if (control.setVisible) control.setVisible(visible);
        if (control.setDisabled) control.setDisabled(disabled);
    }

    var attr = formContext.getAttribute(fieldName);
    if (attr && attr.setRequiredLevel) {
        attr.setRequiredLevel(required ? "required" : "none");
    }
};
