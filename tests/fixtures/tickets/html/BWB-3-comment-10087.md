_maxkreja wrote:_

For everyone that is having issues with renewing their licenses because of this issue, there is an easy workaround: we can skip the broken web interface and access the API to upload our new license.

> **DISCLAIMER:** I have used AI to quickly search through the code and figure out this workaround and to generate the following instructions. It worked for me, but it might not work for you, and you should carefully review the following information before making any changes to productive installations.

# Manual License Renewal for Self-Hosted Bitwarden Organizations

This is a workaround for the broken subscription management in the web interface. The API endpoint is the same one the web interface would use, we're just bypassing the broken UI.

## Prerequisites

- Your new license file (JSON format) from Bitwarden
- Your organization ID (can be found in the URL when viewing your organization in the web vault)
- A valid authentication token

## Step-by-Step Instructions

### 1. Get an Authentication Token

1. Log into your Bitwarden web vault
2. Open browser developer tools (F12)
3. Go to Network tab
4. Make any action in the vault
5. Find any request to your Bitwarden instance
6. Copy the full `Authorization` header value (starts with "Bearer ")

### 2. Upload the License

Use the following curl command:

```bash
curl -X POST "https://YOUR_BITWARDEN_DOMAIN/api/organizations/licenses/self-hosted/YOUR_ORG_ID" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "License=@path/to/your/license.json"
```

Replace:

- `YOUR_BITWARDEN_DOMAIN` with your instance's domain
- `YOUR_ORG_ID` with your organization ID
- `YOUR_TOKEN` with the Bearer token from step 1
- `path/to/your/license.json` with the path to your license file

No restart is required. The changes should take effect immediately and premium features should be available again. You might lock- and unlock your vault once, if the client doesn't immediately recognize the renewed license.
