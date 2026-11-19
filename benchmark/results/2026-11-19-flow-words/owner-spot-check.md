# The owner's lines (D416 (4))

Drawn by code before any judge ran: `random.Random(4162).sample` of 10 over the 163 judged lines, sorted by (doc, n). For each, answer **true**, **false** or **can't tell**: is the line a true plain-words statement of what the code at `at` does, under its conditions? (Several conditions all hold together.)

## 1. kavita-fsd #2 · Save · C1 (change)

**Line:** The account's existing password is removed, once all the checks have passed.

**Code's label:** `saves the Identity user store through UserManager.RemovePasswordAsync — var result = await userManager.RemovePasswordAsync(user);`  
**Conditions:** only if `passwordValidationIssues.Count != 0` is false (an earlier guard)

**At:** `Kavita.Services/AccountService.cs:44`

```
    38  
    39      public async Task<IEnumerable<ApiException>> ChangeUserPassword(AppUser user, string newPassword, CancellationToken ct = default)
    40      {
    41          var passwordValidationIssues = (await ValidatePassword(user, newPassword, ct)).ToList();
    42          if (passwordValidationIssues.Count != 0) return passwordValidationIssues;
    43  
>   44          var result = await userManager.RemovePasswordAsync(user);
    45          if (!result.Succeeded)
    46          {
    47              logger.LogError("Could not update password");
    48              return result.Errors.Select(e => new ApiException(400, e.Code, e.Description));
    49          }
    50  
```

## 2. kavita-fsd #12 · Save · X8 (stop)

**Line:** If the request has no signed-in user identity, it stops with a not-authenticated error.

**Code's label:** `throw: var userClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? throw new KavitaException(NotAuthenticatedMessage);`  
**Conditions:** only if `user.FindFirst(ClaimTypes.NameIdentifier)`

**At:** `Kavita.Common/Extensions/ClaimsPrincipalExtensions.cs:28`

```
    22          var userClaim = user.FindFirst(JwtRegisteredClaimNames.Name) ?? throw new KavitaException(NotAuthenticatedMessage);
    23          return userClaim.Value;
    24      }
    25  
    26      public static int GetUserId(this ClaimsPrincipal user)
    27      {
>   28          var userClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? throw new KavitaException(NotAuthenticatedMessage);
    29          return int.Parse(userClaim.Value);
    30      }
    31  
    32      public static bool HasVerifiedEmail(this ClaimsPrincipal user)
    33      {
    34          var emailVerified = user.FindFirst(EmailVerifiedClaimType);
```

## 3. kavita-fsd #29 · Inviting… · X7 (stop)

**Line:** If no invite token can be produced, the user sees a generic invite error.

**Code's label:** `BadRequest: return BadRequest(await localizationService.TranslateAsync(UserId, "generic-invite-user"));`  
**Conditions:** only if `string.IsNullOrEmpty(token)`; only if `!result.Succeeded` is false (an earlier guard); only if `emailValidationErrors.Any()` is false (an earlier guard); only if `string.IsNullOrEmpty(dto.Email)` is false (an earlier guard); only if `adminUser == null` is false (an earlier guard)

**At:** `Kavita.Server/Controllers/AccountController.cs:842`

```
   836              user.AgeRestrictionIncludeUnknowns = hasAdminRole || dto.AgeRestriction.IncludeUnknowns;
   837  
   838              var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
   839              if (string.IsNullOrEmpty(token))
   840              {
   841                  logger.LogError("There was an issue generating a token for the email");
>  842                  return BadRequest(await localizationService.TranslateAsync(UserId, "generic-invite-user"));
   843              }
   844  
   845              user.ConfirmationToken = token;
   846              unitOfWork.UserRepository.Update(user);
   847              await unitOfWork.CommitAsync(ct);
   848          }
```

## 4. kavita-fsd #43 · handleRelatedReload · X2 (stop)

**Line:** If the user has no access to the series, they get a not found response.

**Code's label:** `NotFound: context.Result = new NotFoundResult();`  
**Conditions:** only if `!hasAccess`; only if `entityId == null` is false (an earlier guard); only if `alwaysAllowAdmin && user.IsInRole(PolicyConstants.AdminRole)` is false (an earlier guard)

**At:** `Kavita.Server/Attributes/EntityAccessAttribute.cs:135`

```
   129  
   130          var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
   131  
   132          var hasAccess = await CheckAccess(unitOfWork, userId, entityId.Value, context.HttpContext.RequestAborted);
   133          if (!hasAccess)
   134          {
>  135              context.Result = new NotFoundResult();
   136          }
   137      }
   138  
   139      protected abstract Task<bool> CheckAccess(IUnitOfWork unitOfWork, int userId, int entityId, CancellationToken ct);
   140  
   141      private static int? ExtractId(HttpContext httpContext, string key)
```

## 5. sonarr-fsd #26 · /settings/connect · S (start)

**Line:** The user opens the Connect settings page.

**Code's label:** `/settings/connect: <Route path="/settings/connect" element={<NotificationSettings />} />`

**At:** `frontend/src/App/AppRoutes.tsx:124`

```
   118          path="/settings/downloadclients"
   119          element={<DownloadClientSettings />}
   120        />
   121  
   122        <Route path="/settings/importlists" element={<ImportListSettings />} />
   123  
>  124        <Route path="/settings/connect" element={<NotificationSettings />} />
   125  
   126        <Route path="/settings/metadata" element={<MetadataSettings />} />
   127  
   128        <Route
   129          path="/settings/metadatasource"
   130          element={<MetadataSourceSettings />}
```

## 6. sonarr-fsd #42 · /settings/indexers · S (start)

**Line:** The user opens the Indexers settings page.

**Code's label:** `/settings/indexers: <Route path="/settings/indexers" element={<IndexerSettings />} />`

**At:** `frontend/src/App/AppRoutes.tsx:115`

```
   109  
   110        <Route
   111          path="/settings/customformats"
   112          element={<CustomFormatSettingsPage />}
   113        />
   114  
>  115        <Route path="/settings/indexers" element={<IndexerSettings />} />
   116  
   117        <Route
   118          path="/settings/downloadclients"
   119          element={<DownloadClientSettings />}
   120        />
   121  
```

## 7. sonarr-fsd #64 · /statistics · X2 (stop)

**Line:** The submitted data fails validation: the app rejects it and returns the validation errors.

**Code's label:** `throw: throw new ValidationException(errors);`  
**Conditions:** only if `errors.Any()`; only if `resource == null` is false (an earlier guard)

**At:** `src/Sonarr.Http/REST/RestController.cs:157`

```
   151                      errors.AddRange(IdValidator.Validate(resource).Errors);
   152                  }
   153              }
   154  
   155              if (errors.Any())
   156              {
>  157                  throw new ValidationException(errors);
   158              }
   159          }
   160  
   161          protected Results<Accepted<TResource>, NotFound> TypedAccepted(int id)
   162          {
   163              var result = GetResourceById(id);
```

## 8. sonarr-fsd #72 · /system/logs/files · X2 (stop)

**Line:** The submitted data fails validation: the app rejects it and returns the validation errors.

**Code's label:** `throw: throw new ValidationException(errors);`  
**Conditions:** only if `errors.Any()`; only if `resource == null` is false (an earlier guard)

**At:** `src/Sonarr.Http/REST/RestController.cs:157`

```
   151                      errors.AddRange(IdValidator.Validate(resource).Errors);
   152                  }
   153              }
   154  
   155              if (errors.Any())
   156              {
>  157                  throw new ValidationException(errors);
   158              }
   159          }
   160  
   161          protected Results<Accepted<TResource>, NotFound> TypedAccepted(int id)
   162          {
   163              var result = GetResourceById(id);
```

## 9. sonarr-fsd #78 · /system/status · A (actor)

**Line:** Any signed-in user can open it; anyone not signed in is refused.

**Code's label:** `signed-in user (no access rule on the action; the app's fallback policy requires a signed-in user)`

**At:** `frontend/src/App/AppRoutes.tsx:143`

```
   137        <Route path="/settings/ui" element={<UISettings />} />
   138  
   139        {/*
   140          System
   141        */}
   142  
>  143        <Route path="/system/status" element={<Status />} />
   144  
   145        <Route path="/system/tasks" element={<Tasks />} />
   146  
   147        <Route path="/system/backups" element={<Backups />} />
   148  
   149        <Route path="/system/updates" element={<Updates />} />
```

## 10. sonarr-fsd #93 · /wanted/cutoffunmet · A (actor)

**Line:** Any signed-in user can open it; anyone not signed in is refused.

**Code's label:** `signed-in user (no access rule on the action; the app's fallback policy requires a signed-in user)`

**At:** `frontend/src/App/AppRoutes.tsx:96`

```
    90        {/*
    91          Wanted
    92        */}
    93  
    94        <Route path="/wanted/missing" element={<Missing />} />
    95  
>   96        <Route path="/wanted/cutoffunmet" element={<CutoffUnmet />} />
    97  
    98        {/*
    99          Settings
   100        */}
   101  
   102        <Route path="/settings" element={<Settings />} />
```
