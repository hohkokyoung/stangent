_dlrudie wrote:_

@c4539 Thanks for giving me an idea where to start looking around.

I added the lines to my global override file and adjusted the paths accordingly.

It still doesn't appear to work. Now as soon as I click Save it logs me out instantly. The errors in the inspector are 401 which has to do with authorization. The file never gets saved. I triple-checked the paths I added and they are correct. Permissions on the directories are also correct.

There is a CSP error at the bottom that appears even before I hit save so I'm not sure if that's the whole cause of the problem either.

```
globalSettings__baseServiceUri__vault=https://site.org
globalSettings__sqlServer__connectionString="Data Source=tcp:mssql,1433;Initial Catalog=vault;Persist Security Info=False;User ID=sa;Password=...;MultipleActiveResultSets=False;Connect Timeout=30;Encrypt=True;TrustServerCertificate=True"
globalSettings__identityServer__certificatePassword=...
globalSettings__internalIdentityKey=...
globalSettings__oidcIdentityClientKey=...
globalSettings__duo__aKey=...
globalSettings__installation__id=...
globalSettings__installation__key=...
globalSettings__mail__replyToEmail=no-reply@site.org
globalSettings__mail__smtp__host=smtp.gmail.com
globalSettings__mail__smtp__port=587
globalSettings__mail__smtp__ssl=false
globalSettings__mail__smtp__username=no-reply@site.org
globalSettings__mail__smtp__password=...
globalSettings__disableUserRegistration=false
adminSettings__admins=...
globalSettings__attachment__baseDirectory=/root/bwdata/core/attachments
globalSettings__attachment__baseUrl=https://site.org/attachments
globalSettings__send__baseDirectory=/root/bwdata/core/attachments/send
globalSettings__send__baseUrl=https://site.org/attachments/send
```

![image](https://user-images.githubusercontent.com/1780069/120387225-3bbac400-c2ef-11eb-915c-8bf84a35fe65.png)

![image](https://user-images.githubusercontent.com/1780069/120387562-b257c180-c2ef-11eb-8fb4-39f9e42573da.png)

**Update:**
I decided to test it out using the CLI client hoping it would provide more information.

If I do not add the four lines above send will save the file successfully but it ends up in the **attchments** directory and can't be downloaded.

If I add any of the lines above it will log me out from the CLI client as well as say a token not found. I tried to eliminate the settings one by one. Using just this one setting, globalSettings__send__baseDirectory, then send will fail on upload and log me out.

![image](https://user-images.githubusercontent.com/1780069/120398975-c821b280-c300-11eb-8169-bf59ea0249ff.png)

I'm not sure why changing one override would cause the whole thing to fail but it does.
