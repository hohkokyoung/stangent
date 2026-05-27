# Realtime subscriptions

## Subscribe to a filtered set
```ts
const channel = supabase
  .channel("profile-changes")
  .on(
    "postgres_changes",
    { event: "UPDATE", schema: "public", table: "profiles", filter: `user_id=eq.${userId}` },
    (payload) => { /* handle */ },
  )
  .subscribe();
```

## Always filter
Unfiltered subscriptions broadcast every row change in the table to every connected client (subject to RLS). That's a bandwidth and privacy bomb. Always provide a `filter:`.

## Cleanup
```ts
supabase.removeChannel(channel);
```
Forgetting to remove leaks WebSocket connections.

## RLS still applies
Realtime respects RLS. If a user can't SELECT the row, they can't see the change event for it. Don't disable RLS just to make realtime "work" — fix the policy.

## Don't
- Don't subscribe to a whole table from a mobile/web client.
- Don't replace polling with realtime everywhere — for low-frequency UI, polling on focus is simpler and cheaper.
