var Couture = Couture || {};
Couture.Quote = Couture.Quote || {};

Couture.Quote.DELIVERY_PREF_FOB = 1;

// Hidden + made optional when delivery preference = FOB.
Couture.Quote.HIDEABLE_FREIGHT_FIELDS = [
    "eb_cycletime",
    "eb_shippingrateperhour",
    "eb_straighttruckrateton",
    "eb_trailerrateton",
    "eb_loadtime",
    "eb_unloadtime",
    "eb_totaltripminutes"
];

// Ship-to ZIP stays visible regardless (FOB users can still capture
// lat/long for heat-mapping) but its required flag toggles with the
// preference. If you also surface a custom eb_jobsitezip on the Quote
// form, add it to this array.
Couture.Quote.ZIP_FIELDS = [
    "shipto_postalcode"
];

// ─────────────────────────────────────────────────────────────────────────
// onLoad — runs on every Quote form load. Applies the delivery-preference
// visibility rules immediately so the form opens in the right shape.
// ─────────────────────────────────────────────────────────────────────────
Couture.Quote.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();
    Couture.Quote.applyDeliveryPreferenceVisibility(formContext);
};

// ─────────────────────────────────────────────────────────────────────────
// Wire as the OnChange handler on eb_deliverypreference on the Quote.
// ─────────────────────────────────────────────────────────────────────────
Couture.Quote.onDeliveryPreferenceChange = function (executionContext) {
    Couture.Quote.applyDeliveryPreferenceVisibility(executionContext.getFormContext());
};

// ─────────────────────────────────────────────────────────────────────────
// Shared show/hide + required toggle.
//   FOB → freight fields hidden + optional, ZIP optional but visible.
//   anything else → freight fields visible + required, ZIP required.
// ─────────────────────────────────────────────────────────────────────────
Couture.Quote.applyDeliveryPreferenceVisibility = function (formContext) {
    var prefAttr = formContext.getAttribute("eb_deliverypreference");
    if (!prefAttr) return;

    var pref = prefAttr.getValue();
    var isFob = pref === Couture.Quote.DELIVERY_PREF_FOB;

    // Freight fields: hide and un-require for FOB; show and require otherwise.
    Couture.Quote.HIDEABLE_FREIGHT_FIELDS.forEach(function (fieldName) {
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ !isFob,
            /* required */ !isFob);
    });

    // ZIP field(s): always visible, required only when not FOB.
    Couture.Quote.ZIP_FIELDS.forEach(function (fieldName) {
        Couture.Quote._setFieldState(formContext, fieldName,
            /* visible  */ true,
            /* required */ !isFob);
    });
};

Couture.Quote._setFieldState = function (formContext, fieldName, visible, required) {
    var control = formContext.getControl(fieldName);
    if (control && control.setVisible) {
        control.setVisible(visible);
    }

    var attr = formContext.getAttribute(fieldName);
    if (attr && attr.setRequiredLevel) {
        attr.setRequiredLevel(required ? "required" : "none");
    }
};
