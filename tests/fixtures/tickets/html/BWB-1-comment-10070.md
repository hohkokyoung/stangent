_Neonwarden wrote:_

Hi there,

To update your license file, log in to the Bitwarden web application at https://vault.bitwarden.com or https://vault.bitwarden.eu, depending on your server's geographical location. For additional details on server geography, please check here: https://bitwarden.com/help/server-geographies/

Navigate to Admin Console > {YOUR ORG NAME} > Billing > Subscription, where you can download your new license file. To generate this file, you will need your Installation ID, which can be found in the ./bwdata/env/global.override.env configuration file on your server. Alternatively, you can retrieve it from the Bitwarden System Administration Portal at https://{YOUR URL}/admin

Once you have obtained the new license file, log in to your self-hosted web application and go to Admin Console > {YOUR ORG NAME} > Billing > Subscription to apply the update. After applying the new license, log out and then log back in to ensure the changes take effect.

If you encounter an error stating “Version X not supported”, verify that your server is running the latest version. If an update is required, ensure you have a backup, clone, or full copy of the bwdata directory before proceeding. You can follow the official update procedure outlined here: https://bitwarden.com/help/updating-on-premise/

We use GitHub issues as a place to track bugs and other development related issues. If your issue persists, please write us back using our “Contact support” form located on our Help Center (https://bitwarden.com/help/).

You can include a link to this issue in the message content.

Alternatively, you can also search for an answer in our help documentation or get help from other Bitwarden users on our community forums (https://community.bitwarden.com/c/support/).

The issue here will be closed.

Thanks!
