_taaem wrote:_

If I see it correctly, I only send one IP in that address but the second one gets added by the bitwarden nginx. This is the behavior described in https://github.com/bitwarden/server/pull/486#issuecomment-487144227, so I guess its the correct behavior. If I understand the comment afterwards, bitwarden should use the first IP in that header as the originating IP address. I don't know if anything after this PR changed, breaking that functionality.
