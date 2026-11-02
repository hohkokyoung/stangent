_mklitzn wrote:_

Still the same in 2025.4.1-beta :-(

```
2025-04-23 20:51:34.166 +02:00 [ERR] Unable to resolve service for type 'Bit.Core.Billing.Repositories.IOrganizationInstallationRepository' while attempting to activate 'Bit.Api.Billing.Controllers.OrganizationsController'.
System.InvalidOperationException: Unable to resolve service for type 'Bit.Core.Billing.Repositories.IOrganizationInstallationRepository' while attempting to activate 'Bit.Api.Billing.Controllers.OrganizationsController'.
   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)
   at lambda_method1491(Closure, IServiceProvider, Object[])
   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider.<>c__DisplayClass6_0.<CreateControllerFactory>g__CreateController|0(ControllerContext controllerContext)
   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)
   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeInnerFilterAsync()
--- End of stack trace from previous location ---
   at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeNextExceptionFilterAsync>g__Awaited|26_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
```

Thinking about switching to a more reliable product ...
