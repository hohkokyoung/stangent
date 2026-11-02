_taaem wrote:_

According to https://github.com/bitwarden/server/blob/5a850f48e2adf309a05741a43eb45eb390354677/src/Identity/appsettings.json#L72 the client IP is set from the `CF-Connecting-IP` and not `X-Forwarded-For` or `X-Real-IP` headers. But for selfhosted instances the `CF-Connecting-IP` header is not set.
