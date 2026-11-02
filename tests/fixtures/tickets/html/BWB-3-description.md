### Steps To Reproduce

1. Go to self-hosted instance
2. Click on 'Admin Console'
3. Then, go to 'Billing' > 'Subscription'

### Expected Result

Webpage to renew/upload new license

### Actual Result

Loading spinner, nothing more.
500 error page.

### Screenshots or Videos

<img width="1166" alt="Screenshot 2025-01-07 at 10 52 22" src="https://github.com/user-attachments/assets/8225d30e-870a-4124-adf5-cdd0bb677497" />

### Additional Context

From `api.log`

```
2025-01-07 09:42:59.263 +00:00 [ERR] Unable to resolve service for type 'Bit.Core.Billing.Repositories.IOrganizationInstallationRepository' while attempting to activate 'Bit.Api.Billing.Controllers.OrganizationsController'.
System.InvalidOperationException: Unable to resolve service for type 'Bit.Core.Billing.Repositories.IOrganizationInstallationRepository' while attempting to activate 'Bit.Api.Billing.Controllers.OrganizationsController'.
   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)
   at lambda_method600(Closure, IServiceProvider, Object[])
   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)
   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)
   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()
--- End of stack trace from previous location ---
   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeNextExceptionFilterAsync>g__Awaited|26_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
```

### Githash Version

ed090c8e-dirty

### Environment Details

bitwarden.sh version 2024.12.1
Docker version 27.2.0
Docker Compose version v2.20.3
Ubuntu Linux

### Database Image

mariadb:10

### Issue-Link

https://github.com/bitwarden/server/issues/2480

### Issue Tracking Info

- [x] I understand that work is tracked outside of Github. A PR will be linked to this issue should one be opened to address it, but Bitwarden doesn't use fields like "assigned", "milestone", or "project" to track progress.
