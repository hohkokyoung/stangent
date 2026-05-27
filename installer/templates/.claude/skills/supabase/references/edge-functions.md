# Edge functions

Deno + TypeScript, served on Supabase's edge runtime.

## Shape
```ts
// supabase/functions/update-profile/index.ts
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("method not allowed", { status: 405 });
  }
  const authHeader = req.headers.get("authorization");
  if (!authHeader) return new Response("unauthorized", { status: 401 });

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_ANON_KEY")!,
    { global: { headers: { Authorization: authHeader } } },
  );

  const { name } = await req.json();
  const { data: user } = await supabase.auth.getUser();
  if (!user?.user) return new Response("unauthorized", { status: 401 });

  const { error } = await supabase
    .from("profiles")
    .update({ name })
    .eq("user_id", user.user.id);

  if (error) return new Response(error.message, { status: 400 });
  return new Response(JSON.stringify({ ok: true }), {
    headers: { "content-type": "application/json" },
  });
});
```

## Auth pattern
Forward the caller's bearer token via `global.headers.Authorization`. That makes RLS apply as that user. Don't use the service role for user requests.

## Deploy
```
supabase functions deploy update-profile
```

## Don't
- Don't use the service_role key for endpoints that act on behalf of a user — RLS won't protect you.
- Don't `await` inside an unhandled try block; wrap with proper status codes.
