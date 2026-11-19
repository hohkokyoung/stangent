# The owner's 10 lines (D416 (4))

Drawn by code before the judge ran: `random.Random(417).sample` over the 163 judged lines (Kavita's 63, Sonarr's 100), sorted by (app, n).
For each, answer **true**, **false** or **can't tell**: is the line a true plain-words statement of what the code at `at` does, under its conditions?

## 1. kavita #1 · Save · A (actor)

**Line:** Any signed-in user except Read Only users, who are refused.

**Code's label:** `user who passes DisallowRole(roles = ["Read Only"]) (access filter on the action (method))`

**At:** `UI/Web/src/app/user-settings/change-password/change-password.component.html:49`

```
    43          </div>
    44  
    45          <div class="col-auto d-flex d-md-block justify-content-sm-center text-md-end mb-3">
    46            <button type="button" class="flex-fill btn btn-secondary me-2" aria-describedby="password-panel"
    47                    (click)="resetPasswordForm()">{{t('reset')}}</button>
    48            <button type="submit" class="flex-fill btn btn-primary" aria-describedby="password-panel"
>   49                    (click)="savePasswordForm()" [disabled]="!formGroup().valid() || !(formGroup().dirty() || formGroup().touched())">{{t('save')}}</button>
    50          </div>
    51        </form>
    52  
    53      </ng-template>
    54    </app-setting-item>
    55  </ng-container>
```

## 2. kavita #2 · Save · C1 (change)

**Line:** The user's existing password is removed from the account, when there are no password validation problems.

**Code's label:** `saves the Identity user store through UserManager.RemovePasswordAsync — var result = await userManager.RemovePasswordAsync(user);`  
**When:** `passwordValidationIssues.Count != 0`

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

## 3. kavita #11 · Save · X7 (stop)

**Line:** Stops with an error response when the user holds one of the roles the action is closed to.

**Code's label:** `StatusCode: context.Result = new ContentResult`  
**When:** `roles.Any(role => !string.IsNullOrEmpty(role) && user.IsInRole(role))`

**At:** `Kavita.Server/Attributes/DisallowRoleAttribute.cs:36`

```
    30              var localizationService = context.HttpContext.RequestServices.GetRequiredService<ILocalizationService>();
    31              var userId = user.GetUserId();
    32  
    33              var message = await localizationService.TranslateAsync(userId, "permission-denied");
    34  
    35              // Pipeline is stopped in IAsyncAuthorizationFilter if result is non nil
>   36              context.Result = new ContentResult
    37              {
    38                  StatusCode = StatusCodes.Status400BadRequest,
    39                  Content = message,
    40                  ContentType = "text/plain"
    41              };
    42          }
```

## 4. kavita #12 · Save · X8 (stop)

**Line:** Stops with a not-authenticated error when the signed-in user's identity cannot be read from the sign-in.

**Code's label:** `throw: var userClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? throw new KavitaException(NotAuthenticatedMessage);`  
**When:** `user.FindFirst(ClaimTypes.NameIdentifier)`

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

## 5. kavita #21 · Inviting… · C8 (change)

**Line:** An invite email is sent in the background, when the email address is valid and email is set up on the server.

**Code's label:** `queues IEmailService.SendInviteEmail through BackgroundJob.Enqueue, which runs later, in the background (now, in the background) — BackgroundJob.Enqueue(() => emailService.SendInviteEmail(new ConfirmationEmailDto()`  
**When:** `!emailService.IsValidEmail(dto.Email) || !settings.IsEmailSetup()`, `emailValidationErrors.Any()`, `string.IsNullOrEmpty(dto.Email)`, `adminUser == null`

**At:** `Kavita.Server/Controllers/AccountController.cs:874`

```
   868                      EmailLink = emailLink,
   869                      EmailSent = false,
   870                      InvalidEmail = true
   871                  });
   872              }
   873  
>  874              BackgroundJob.Enqueue(() => emailService.SendInviteEmail(new ConfirmationEmailDto()
   875              {
   876                  EmailAddress = dto.Email,
   877                  LocaleUserId = adminUser.Id, // Use the admin's locale for the invite to server
   878                  InvitingUser = adminUser.UserName,
   879                  ServerConfirmationLink = emailLink
   880              }));
```

## 6. sonarr #11 · /add/import · X3 (stop)

**Line:** A request with an ID of zero or less is refused, saying the ID is not valid.

**Code's label:** `throw: throw new BadRequestException(id + " is not a valid ID");`  
**When:** `id <= 0`

**At:** `src/Sonarr.Http/REST/RestController.cs:37`

```
    31          private ResourceValidator<TResource> IdValidator { get; set; }
    32  
    33          protected void ValidateId(int id)
    34          {
    35              if (id <= 0)
    36              {
>   37                  throw new BadRequestException(id + " is not a valid ID");
    38              }
    39          }
    40  
    41          protected RestController()
    42          {
    43              _logger = NzbDroneLogger.GetLogger(this);
```

## 7. sonarr #27 · /add/new · X3 (stop)

**Line:** A request with an ID of zero or less is refused, saying the ID is not valid.

**Code's label:** `throw: throw new BadRequestException(id + " is not a valid ID");`  
**When:** `id <= 0`

**At:** `src/Sonarr.Http/REST/RestController.cs:37`

```
    31          private ResourceValidator<TResource> IdValidator { get; set; }
    32  
    33          protected void ValidateId(int id)
    34          {
    35              if (id <= 0)
    36              {
>   37                  throw new BadRequestException(id + " is not a valid ID");
    38              }
    39          }
    40  
    41          protected RestController()
    42          {
    43              _logger = NzbDroneLogger.GetLogger(this);
```

## 8. sonarr #37 · /settings/general · A (actor)

**Line:** Any signed-in user; people who are not signed in are refused.

**Code's label:** `signed-in user (no access rule on the action; the app's fallback policy requires a signed-in user)`

**At:** `frontend/src/App/AppRoutes.tsx:135`

```
   129          path="/settings/metadatasource"
   130          element={<MetadataSourceSettings />}
   131        />
   132  
   133        <Route path="/settings/tags" element={<TagSettings />} />
   134  
>  135        <Route path="/settings/general" element={<GeneralSettings />} />
   136  
   137        <Route path="/settings/ui" element={<UISettings />} />
   138  
   139        {/*
   140          System
   141        */}
```

## 9. sonarr #56 · /settings/mediamanagement · X1 (stop)

**Line:** An empty request is refused with the message that the request body can't be empty.

**Code's label:** `throw: throw new BadRequestException("Request body can't be empty");`  
**When:** `resource == null`

**At:** `src/Sonarr.Http/REST/RestController.cs:131`

```
   125          }
   126  
   127          protected void ValidateResource(TResource resource, bool validateId = false, bool skipValidate = false, bool skipSharedValidate = false)
   128          {
   129              if (resource == null)
   130              {
>  131                  throw new BadRequestException("Request body can't be empty");
   132              }
   133  
   134              var errors = new List<ValidationFailure>();
   135  
   136              if (!skipSharedValidate)
   137              {
```

## 10. sonarr #90 · /system/tasks · X2 (stop)

**Line:** An empty request is refused with a list of validation errors.

**Code's label:** `throw: throw new ValidationException(errors);`  
**When:** `errors.Any()`, `resource == null`

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
