var Couture = Couture || {};
Couture.Quote = Couture.Quote || {};

// ─────────────────────────────────────────────────────────────────────────
// Ribbon button commands
// ─────────────────────────────────────────────────────────────────────────

/// Wires Couture.Quote.onDelayClick / onUndelayClick to the ribbon
/// buttons on Quote. Both pass the primary control (formContext) and
/// flip eb_delayed via the Web API – the SetStatusOnDelayedChange
/// plugin then takes care of the quote's status reason and the
/// project's cascade. We use the Web API rather than
/// formContext.getAttribute().setValue() + save() because the form is
/// read-only once a quote is Active.

Couture.Quote.onDelayClick = function (primaryControl) {
    Couture.Quote._setDelayed(primaryControl, true, "Quote marked as Delayed.");
};

Couture.Quote.onUndelayClick = function (primaryControl) {
    Couture.Quote._setDelayed(primaryControl, false, "Quote un-delayed.");
};

Couture.Quote._setDelayed = function (primaryControl, delayed, successMessage) {
    var formContext = primaryControl;
    var rawId = formContext.data.entity.getId();
    if (!rawId) {
        Xrm.Navigation.openAlertDialog({
            text: "Save the quote before changing its delayed state."
        });
        return;
    }

    var quoteId = rawId.replace(/[{}]/g, "");

    Xrm.WebApi.updateRecord("quote", quoteId, {
        "eb_delayed": delayed
    }).then(function () {
        return formContext.data.refresh(false);
    }).then(function () {
        Xrm.Navigation.openAlertDialog({ text: successMessage });
    }).catch(function (error) {
        Xrm.Navigation.openAlertDialog({
            text: "Couldn't update quote: " + (error && error.message ? error.message : error)
        });
    });
};

// ─────────────────────────────────────────────────────────────────────────
// Ribbon Workbench enable-rules (Custom Rule → Function name = one of these).
// Each returns a boolean. Wire the Delay button to canDelay, Undelay to
// canUndelay. Both hide automatically on Won / Closed quotes.
// ─────────────────────────────────────────────────────────────────────────

Couture.Quote.canDelay = function (primaryControl) {
    return Couture.Quote._isQuoteEditable(primaryControl)
        && !Couture.Quote._isDelayed(primaryControl);
};

Couture.Quote.canUndelay = function (primaryControl) {
    return Couture.Quote._isQuoteEditable(primaryControl)
        && Couture.Quote._isDelayed(primaryControl);
};

Couture.Quote._isDelayed = function (primaryControl) {
    var formContext = primaryControl;
    var attr = formContext.getAttribute && formContext.getAttribute("eb_delayed");
    if (!attr) return false;
    return attr.getValue() === true;
};

Couture.Quote._isQuoteEditable = function (primaryControl) {
    var formContext = primaryControl;
    var stateAttr = formContext.getAttribute && formContext.getAttribute("statecode");
    if (!stateAttr) return false;
    var state = stateAttr.getValue();
    // 0 = Draft, 1 = Active. 2 = Won, 3 = Closed are read-only and
    // shouldn't allow delaying.
    return state === 0 || state === 1;
};
