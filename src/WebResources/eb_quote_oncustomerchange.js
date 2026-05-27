var Couture = Couture || {};
Couture.Quote = Couture.Quote || {};

Couture.Quote.onCustomerChange = function (executionContext) {
    var formContext = executionContext.getFormContext();

    var customerAttr = formContext.getAttribute("customerid");
    if (!customerAttr) return;

    var customerVal = customerAttr.getValue();
    if (!customerVal || customerVal.length === 0) return;

    var customer = customerVal[0];
    if (customer.entityType !== "account") return;

    Xrm.WebApi.retrieveRecord(
        "account",
        customer.id,
        "?$select=_defaultpricelevelid_value"
    ).then(function (acct) {
        var plId = acct["_defaultpricelevelid_value"];
        if (!plId) return;

        var plName = acct["_defaultpricelevelid_value@OData.Community.Display.V1.FormattedValue"] || "Default";

        var priceList = formContext.getAttribute("pricelevelid");
        if (!priceList) return;

        priceList.setValue([{
            id: plId,
            entityType: "pricelevel",
            name: plName
        }]);

    }).catch(function (error) {
        console.error("Couture.Quote.onCustomerChange: " + error.message);
    });
};
