# Registry Update Procedure

After every status change and after feature creation, update the `features` map
in `{{paths.registry_path}}`.

## Entry format

```json
"features": {
  "{{feature_id}}": {
    "title":   "{{title from frontmatter, or raw_request if title not yet set}}",
    "status":  "{{current status}}",
    "branch":  "{{branch from frontmatter, or '' if not yet created}}",
    "created": "{{created from frontmatter}}",
    "updated": "{{current ISO date}}"
  }
}
```

## Atomic update protocol

1. Acquire the registry lock (same protocol as Step 1a: write `.stangent/features_registry.lock`).
2. Read `{{paths.registry_path}}` — parse JSON.
3. Set `registry.features["{{feature_id}}"]` to the entry above.
4. Write registry back.
5. Release lock (delete `.stangent/features_registry.lock`).

If the registry file is missing or malformed: log a warning and skip (do not block the pipeline).
If any step fails after the lock is created: always delete the lock before stopping.
