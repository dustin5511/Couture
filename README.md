# Couture

Dynamics 365 plugin that redirects outbound customer email to the Support Intake
Queue so replies always come from `support@tabperformance.com` rather than the
CSR who drafted them.

## Plugin

`Couture.Plugins.SupportEmail.SwitchOutboundEmailToSupportQueue` runs
synchronously in the PreOperation stage of `Create` on the `email` entity. For
any outbound email (`directioncode != false`) it:

1. Looks up the queue whose name is `Support Intake Queue` or whose email
   address is `support@tabperformance.com`.
2. Replaces `email.from` with a single `activityparty` pointing at that queue.
3. Sets `email.sender` to `support@tabperformance.com`.

Incoming email and records where the support queue cannot be resolved are left
untouched.

## Building

The plugin assembly must be strong-named. Generate a key once and drop it next
to the csproj:

```
sn -k src/Couture.Plugins.SupportEmail/Couture.Plugins.SupportEmail.snk
```

Then build:

```
dotnet build Couture.sln -c Release
```

The signed assembly is produced at
`src/Couture.Plugins.SupportEmail/bin/Release/net462/Couture.Plugins.SupportEmail.dll`.

## Registration

Use the Plugin Registration Tool against the target environment.

1. **Prerequisite:** a Queue named `Support Intake Queue` exists with email
   address `support@tabperformance.com` and incoming/outgoing mail is approved.
2. Register new assembly -> select the DLL above, Isolation = Sandbox, Location
   = Database.
3. Register a new step on the type `SwitchOutboundEmailToSupportQueue`:
   - Message: `Create`
   - Primary Entity: `email`
   - Event Pipeline Stage: `PreOperation`
   - Execution Mode: `Synchronous`
   - Deployment: `Server`
   - Filtering Attributes: (leave empty)
4. No images are required — the plugin only reads the Target.

## Verifying

1. In the CSR app, open a Case and click Reply on an incoming email.
2. Save the draft.
3. Re-open the draft — the **From** field should now read **Support Intake
   Queue** and the header metadata should show `support@tabperformance.com`.
4. Send the email and confirm the recipient sees it from the support address.
