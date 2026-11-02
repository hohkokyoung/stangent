### Steps To Reproduce

1. Upgrade docker container to latest 2023.2.1 release
2. Login to the Webvault
3. Click "Organizations" tab, vault tab under organization should be selected automatically

### Expected Result

A view showing the filter view with the collections etc. on the left and the collections/entries in the middle pane.

### Actual Result

A loading indicator on the left running "forever" (waited 15 minutes) and all entries of the entire vault loading in the middle with a gray empty square where normally the collections it is in are shown. (According to the online view in the webvault).

Also the "api.log" shows the following error:

```
fail[39m[22m[49m:` Bit.Api.Utilities.ExceptionHandlerFilterAttribute[0]
      => SpanId:8de51beafea0925d, TraceId:89ce70322580ad6bb13c9e4618aaeb44, ParentId:0000000000000000 => ConnectionId:0HMONBQUKRF78 => RequestPath:/organizations/<redacted>/collections/details RequestId:0HMONBQUKRF78:00000002 => Bit.Api.Controllers.CollectionsController.GetManyWithDetails (Api)
      The LINQ expression 'DbSet<CollectionGroup>()
          .Where(cg => cg.Collection.OrganizationId == __organizationId_0 && __Select_1
              .Contains(cg.Collection.Id))
          .GroupBy(cg => cg.CollectionId)
          .Select(g => g)' could not be translated. Additional information: Translation of 'Select' which contains grouping parameter without composition is not supported. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'. See https://go.microsoft.com/fwlink/?linkid=2101038 for more information.
      System.InvalidOperationException: The LINQ expression 'DbSet<CollectionGroup>()
          .Where(cg => cg.Collection.OrganizationId == __organizationId_0 && __Select_1
              .Contains(cg.Collection.Id))
          .GroupBy(cg => cg.CollectionId)
          .Select(g => g)' could not be translated. Additional information: Translation of 'Select' which contains grouping parameter without composition is not supported. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'. See https://go.microsoft.com/fwlink/?linkid=2101038 for more information.
         at Microsoft.EntityFrameworkCore.Query.Internal.NavigationExpandingExpressionVisitor.VisitMethodCall(MethodCallExpression methodCallExpression)
         at Microsoft.EntityFrameworkCore.Query.Internal.NavigationExpandingExpressionVisitor.VisitMethodCall(MethodCallExpression methodCallExpression)
         at Microsoft.EntityFrameworkCore.Query.Internal.NavigationExpandingExpressionVisitor.Expand(Expression query)
         at Microsoft.EntityFrameworkCore.Query.QueryTranslationPreprocessor.Process(Expression query)
         at Microsoft.EntityFrameworkCore.Query.QueryCompilationContext.CreateQueryExecutor[TResult](Expression query)
         at Microsoft.EntityFrameworkCore.Storage.Database.CompileQuery[TResult](Expression query, Boolean async)
         at Microsoft.EntityFrameworkCore.Query.Internal.QueryCompiler.CompileQueryCore[TResult](IDatabase database, Expression query, IModel model, Boolean async)
         at Microsoft.EntityFrameworkCore.Query.Internal.QueryCompiler.<>c__DisplayClass9_0`1.<Execute>b__0()
         at Microsoft.EntityFrameworkCore.Query.Internal.CompiledQueryCache.GetOrAddQuery[TResult](Object cacheKey, Func`1 compiler)
         at Microsoft.EntityFrameworkCore.Query.Internal.QueryCompiler.Execute[TResult](Expression query)
         at Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider.Execute[TResult](Expression expression)
         at System.Linq.Queryable.FirstOrDefault[TSource](IQueryable`1 source, Expression`1 predicate)
         at Bit.Infrastructure.EntityFramework.Repositories.CollectionRepository.<>c__DisplayClass10_1.<GetManyByUserIdWithAccessAsync>b__7(CollectionDetails collection) in /source/src/Infrastructure.EntityFramework/Repositories/CollectionRepository.cs:line 247
         at System.Linq.Enumerable.SelectListIterator`2.ToList()
         at Bit.Infrastructure.EntityFramework.Repositories.CollectionRepository.GetManyByUserIdWithAccessAsync(Guid userId, Guid organizationId) in /source/src/Infrastructure.EntityFramework/Repositories/CollectionRepository.cs:line 246
         at Bit.Api.Controllers.CollectionsController.GetManyWithDetails(Guid orgId) in /source/src/Api/Controllers/CollectionsController.cs:line 88
         at lambda_method1059(Closure , Object )
         at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.AwaitableObjectResultExecutor.Execute(IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeActionMethodAsync>g__Logged|12_1(ControllerActionInvoker invoker)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Rethrow(ActionExecutedContextSealed context)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.Next(State& next, Scope& scope, Object& state, Boolean& isCompleted)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeInnerFilterAsync>g__Awaited|13_0(ControllerActionInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
         at Microsoft.AspNetCore.Mvc.Infrastructure.ResourceInvoker.<InvokeNextExceptionFilterAsync>g__Awaited|26_0(ResourceInvoker invoker, Task lastTask, State next, Scope scope, Object state, Boolean isCompleted)
```

### Screenshots or Videos

![2023-02-27 20_00_37-Vaults _ Bitwarden Web Vault](https://user-images.githubusercontent.com/126351916/221660604-c19c2a25-c272-4d46-a537-0e3008ae8415.png)

### Additional Context

Seems to be related to some issue with EF-translation of the query. Looking at the code it seems that the "groups" and "user" query could have the same issue. (Lines 188 & 193 `src/Infrastructure.EntityFramework/Repositories/CollectionRepository.cs`)
Doing some research one possible solution from Stackoverflow was to add an "orderBy" in the query: https://stackoverflow.com/questions/70423147/translation-of-select-which-contains-grouping-parameter-without-composition-is

You can also not edit any existing user, add a new one or add a new collection, as it only shows the loading indicator in the popup and also narrows down to the same error in the logs.

### Githash Version

3cedc0c7-dirty

### Environment Details

- Synology DSM 7.1.1
- Docker
- Intel Pentium N3710

### Database Image

mariadb: 10.6

### Issue-Link

https://github.com/bitwarden/server/issues/2480

### Issue Tracking Info

- [x] I understand that work is tracked outside of Github. A PR will be linked to this issue should one be opened to address it, but Bitwarden doesn't use fields like "assigned", "milestone", or "project" to track progress.
