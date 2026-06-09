# Supabase Edge Functions (Deno 2.x)

## Current function structure

```ts
// supabase/functions/hello-world/index.ts
import { withSupabase } from 'npm:@supabase/server'

export default {
  fetch: withSupabase({ auth: ['publishable', 'secret'] }, async (req, ctx) => {
    const { name } = await req.json()
    return Response.json({ message: `Hello ${name}!` })
  }),
}
```

## User-scoped vs admin client

```ts
export default {
  fetch: withSupabase({ auth: 'user' }, async (_req, ctx) => {
    // ctx.supabase — user-scoped, RLS enforced
    const { data } = await ctx.supabase.from('posts').select()

    // ctx.supabaseAdmin — bypasses RLS (use sparingly)
    const { data: all } = await ctx.supabaseAdmin.from('posts').select()

    return Response.json({ posts: data })
  }),
}
```

## CORS handling

```ts
// Modern — import from SDK (v2.95.0+)
import { corsHeaders } from '@supabase/supabase-js/cors'

export default {
  fetch: async (req) => {
    if (req.method === 'OPTIONS') {
      return new Response('ok', { headers: corsHeaders })
    }
    try {
      const { name } = await req.json()
      return Response.json({ message: `Hello ${name}!` }, { headers: corsHeaders })
    } catch (err) {
      return Response.json({ error: err.message }, { status: 400, headers: corsHeaders })
    }
  },
}
```

## Environment variables

```ts
const openAIKey = Deno.env.get('OPENAI_API_KEY')
const supabaseUrl = Deno.env.get('SUPABASE_URL')               // auto-injected
const supabaseServiceKey = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') // auto-injected
```

## AI mistakes cheatsheet

| AI generates | Correct |
|---|---|
| `Deno.serve(handler)` | `export default { fetch: ... }` |
| `import from 'https://deno.land/x/...'` | Use `npm:` specifier |
| No OPTIONS preflight check | Always check `req.method === 'OPTIONS'` first |
| `createClient(url, key)` inside handler | Use `ctx.supabase` from `withSupabase` |
| Parse body on OPTIONS request | Check method first — body is empty on preflight |
| `var` in Deno | `const`/`let` — GraalJS (ECMAScript 2022) only |
