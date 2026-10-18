---
description: Show what's in this repo's stangent store, whether AI steps are on, and where telemetry goes.
allowed-tools: Bash(stangent status:*)
---

Run `stangent status` in the current repo and report its result in two or three plain sentences:
how many artifacts the store holds (by type), whether AI steps are enabled, and the stangent version.

The command prints one JSON envelope. If `ok` is false, say what `error.message` says and,
when `error.code` is `not_initialised`, suggest running `stangent init`. Don't read store
files directly: the CLI is the store's only way in.
