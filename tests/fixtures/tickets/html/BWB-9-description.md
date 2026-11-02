### Steps To Reproduce

1. Login with new device
2. Examine email notification of new device IP Address feld

### Expected Result

Real IP of logged on device is displayed

### Actual Result

IP of internal reverse proxy is displayed

### Screenshots or Videos

![image](https://github.com/bitwarden/server/assets/533249/51f66b67-3474-440c-9daf-5d8e02faa6b0)

### Additional Context

Previously we were able to get this IP reported correctly due to utilisaiton of the `X-Real-IP` header, however this appears to have been broken by the changes in https://github.com/bitwarden/server/pull/2847.

As I understand it the solution would now be to set the `KnownProxies` config option, however this is not very easily achievable in our environment as the proxies IP addresses are dynamically allocated in a dedicated 'reverse proxy' subnet and can be added to and removed dynamically.

As per my understanding of the .net side, the ideal solution would be to implement the ability to set [KnownNetworks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.forwardedheadersoptions.knownnetworks?view=aspnetcore-7.0)  as well as KnownProxies, so we could allocate the entire subnet as proxied.

Alternatively restore the ability to allow us to continue setting a header like `X-Real-IP` as trusted as this is a regression in functionality.

### Build Version

2023.7.0

### Environment

Self-Hosted

### Environment Details

Docker-swarm based deployment with a caddy-based reverse proxy terminating TLS to the containers.

### Issue Tracking Info

- [x] I understand that work is tracked outside of Github. A PR will be linked to this issue should one be opened to address it, but Bitwarden doesn't use fields like "assigned", "milestone", or "project" to track progress.
