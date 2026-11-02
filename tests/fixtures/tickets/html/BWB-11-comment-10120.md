_taaem wrote:_

If I check `<url>/api/ip` I can see:

```json
{
  "ip": "::ffff:172.29.60.10",
  "headers": {
    "X-Forwarded-For": {
      "key": "X-Forwarded-For",
      "value": [
        "xxx.xxx.xxx.xxx, xxx.xxx.xxx.xxx"
      ]
    }
  }
}
```

where `xxx.xxx.xxx.xxx` is again my real ip, but twice, maybe that's the issue?
