_vinnyperella wrote:_

Just wanted to note I'm seeing the same thing I'm using Bitwarden without a reverse proxy I think it has something to do with the "::ffff:" prefix which appears to be an "IPv6 prefix for an IPv4 address mapped into IPv6 space." This is breaking fail2ban integration for failed logins because it's not forwarding the actual client IP.

I don't know when this stopped working I only noticed it recently but I have not changed anything on my end other than upgrading Bitwarden and I have old fail2ban emails banning failed logins so I know it used to work.

A bit more detail in the issue I opened in the self-host repo linked above this comment and also here: https://github.com/bitwarden/self-host/issues/104
