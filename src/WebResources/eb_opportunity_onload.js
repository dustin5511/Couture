var Couture = Couture || {};
Couture.Opportunity = {

    DEFAULT_ACCOUNT_NAME: "Default (do not remove)",

    onLoad: function (executionContext) {
        var formContext = executionContext.getFormContext();

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

            formContext.getAttribute("parentaccountid").setValue([{
                id: acct["accountid"],
                entityType: "account",
                name: acct["name"]
            }]);

        }).catch(function (error) {
            console.error("Couture.Opportunity.onLoad: " + error.message);
        });
    }
};
