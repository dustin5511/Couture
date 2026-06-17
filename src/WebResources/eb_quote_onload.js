var Couture = Couture || {};
Couture.Quote = Couture.Quote || {};

Couture.Quote.DELIVERY_PREF_FOB = 1;

// User-entered inputs that drive the freight math. Hidden + optional
// for FOB; visible + required for Delivery / FOB+Delivery.
Couture.Quote.FREIGHT_INPUT_FIELDS = [
    "eb_cycletime",
    "eb_shippingrateperhour"
];

// Calculated outputs the plugin writes. Hidden for FOB; visible (but
// read-only and never required) for Delivery / FOB+Delivery so the
// user can see the math but can't break it.
Couture.Quote.FREIGHT_OUTPUT_FIELDS = [
    "eb_straighttruckrateton",
    "eb_trailerrateton",
    "eb_totaltripminutes"
];

// Load and unload are hard-coded to 10 minutes each. JS keeps them in
// sync on every form load + preference change so the data layer is
// always consistent. Always hidden and disabled — the user never
// touches these.
Couture.Quote.LOAD_UNLOAD_FIELDS = [
    "eb_loadtime",
    "eb_unloadtime"
];
Couture.Quote.LOAD_UNLOAD_VALUE = 10;

// Ship-to ZIP — always visible, required only when not FOB.
Couture.Quote.ZIP_FIELDS = [
    "shipto_postalcode"
];

// ─────────────────────────────────────────────────────────────────────────
// onLoad
// ─────────────────────────────────────────────────────────────────────────
Couture.Quote.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();
    Couture.Quote.applyDeliveryPreferenceVisibility(formContext);
};

// OnChange of eb_deliverypreference
Couture.Quote.onDeliveryPreferenceChange = function (executionContext) {
    Couture.Quote.applyDeliveryPreferenceVisibility(executionContext.getFormContext());
};

// ─────────────────────────────────────────────────────────────────────────
// Shared show/hide + required toggle.
// ─────────────────────────────────────────────────────────────────────────
Couture.Quote.applyDeliveryPreferenceVisibility = function (formContext) {
    var prefAttr = formContext.getAttribute("eb_deliverypreference");
    if (!prefAttr) return;

    var pref = prefAttr.getValue();
    var isFob = pref === Couture.Quote.DELIVERY_PREF_FOB;

    // Inputs — visible + required when not FOB.
    Couture.Quote.FREIGHT_INPUT_FIELDS.forEach(function (fieldName) {
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ !isFob,
            /* disabled */ false);
    });

    // Outputs — visible when not FOB, but always read-only, never required.
    Couture.Quote.FREIGHT_OUTPUT_FIELDS.forEach(function (fieldName) {
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ false,
            /* disabled */ true);
    });

    // Load / unload — always hidden, disabled, force-set to 10.
    Couture.Quote.LOAD_UNLOAD_FIELDS.forEach(function (fieldName) {
        var attr = formContext.getAttribute(fieldName);
        if (attr && attr.getValue() !== Couture.Quote.LOAD_UNLOAD_VALUE) {
            attr.setValue(Couture.Quote.LOAD_UNLOAD_VALUE);
        }
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ false,
            /* required */ false,
            /* disabled */ true);
    });

    // ZIP — always visible, required only when not FOB.
    Couture.Quote.ZIP_FIELDS.forEach(function (fieldName) {
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ true,
            /* required */ !isFob,
            /* disabled */ false);
    });
};

Couture.Quote._setFieldState = function (formContext, fieldName, visible, required, disabled) {
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
