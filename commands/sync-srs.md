Manually push the SRS to your configured external document (Google Docs or OneDrive).

Usage: /sync-srs
No arguments needed — target is configured in .stangent/config.json.

Use this to:
  - Do a one-off sync after making manual edits to SRS.md
  - Test your MCP connection before relying on auto-sync
  - Re-sync after fixing a failed auto-sync

---

## Step 1 — Load configuration

Read `.stangent/config.json`.
If it does not exist: output "Run init.py first." and stop.

Extract:
  - stangent_path = config.stangent_path
  - srs_path      = config.paths.srs_path

Check `config.integrations.srs_sync`:
  - If missing or `enabled = false`:
    Output:
      "SRS sync is not configured.

       To set it up, add this to .stangent/config.json:

         \"integrations\": {
           \"srs_sync\": {
             \"enabled\": true,
             \"provider\": \"google_docs\",
             \"target_id\": \"your-doc-id-here\",
             \"trigger\": \"manual\",
             \"mcp_tool\": \"mcp__gdrive__update_document\"
           }
         }

       See {stangent_path}/agents/srs_sync_agent.md for the full MCP setup guide."
    Stop.

## Step 2 — Run sync agent

Read the full contents of: {stangent_path}/agents/srs_sync_agent.md

Execute the srs_sync_agent with:
  - config_path  : (absolute path to .stangent/config.json)
  - triggered_by : "manual"

## Step 3 — Report result

On SYNCED:
  Output: "✓ SRS synced successfully."

On SKIPPED:
  Output the reason from the agent.

On FAILED:
  Output the error from the agent.
  Output: "Fix the issue above, then run /sync-srs again."
