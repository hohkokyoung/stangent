_taaem wrote:_

Please ignore my comment before, I think the issue is, that I would need to configure `globalSettings__knownProxies=::ffff:172.29.0.10`, but I my reverse proxy doesn't actually have a static IP, instead I would like to allow the whole subnet. According to https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-7.0 I would need to be able to configure the `KnownNetworks`, to allow my whole docker subnet to be used as a proxy IP.
