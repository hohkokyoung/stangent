### Steps To Reproduce

1. Follow the Bitwarden On-Premise installation manual. https://bitwarden.com/help/install-on-premise-linux/#install-bitwarden
2. ./bitwarden.sh install

### Expected Result

Setup should correctly handle situations like mine and provide more detailed information about error that occured.

### Actual Result

In my case, setup shows an unclear error message, that tells about Bitwarden server contacting problem, when the actual error message is "The given key was not present in the dictionary.".

### Screenshots or Videos

_No response_

### Additional Context

When i cURL the Bitwarden API with my `InstallationId` i get JSON response with lowercased property names.

![image](https://user-images.githubusercontent.com/30056636/153204205-6b7421e2-35ea-46a1-ae5f-7a1edde43188.png)

The response on screenshot is from my host machine. in the setup container i get same behavior.
Actually, Setup tries to get the `Enabled` property, but fails because the key name is `enabled`'.
When i replaced property name in the source, rebuilt Setup project and Docker image and did some modifications to shell scripts, to use my modified image, InstallationId validation went successfully.

### Build Version

1.45.2

### Environment

Self-Hosted

### Environment Details

```
root@westdev:~/server/util/Setup# uname -a
Linux westdev 5.4.0-77-generic #86-Ubuntu SMP Thu Jun 17 02:35:03 UTC 2021 x86_64 x86_64 x86_64 GNU/Linux
```

```
root@westdev:~/server/util/Setup# cat /etc/lsb-release
DISTRIB_ID=Ubuntu
DISTRIB_RELEASE=20.04
DISTRIB_CODENAME=focal
DISTRIB_DESCRIPTION="Ubuntu 20.04.3 LTS"
```
