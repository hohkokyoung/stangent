# React Data Fetching

## TanStack Query v5 — breaking changes from v4

```ts
// WRONG — v4 overloaded signatures (removed in v5)
useQuery(key, fn, options)
useMutation(fn, options)
queryClient.fetchQuery(key, fn, options)

// CORRECT — v5 single object signature
useQuery({ queryKey, queryFn, ...options })
useMutation({ mutationFn, ...options })
queryClient.fetchQuery({ queryKey, queryFn })
```

### Status rename

```ts
// WRONG — v4 status name
const { isLoading } = useQuery(...)  // status: 'loading'

// CORRECT — v5
const { isPending, isLoading } = useQuery(...)
// isPending = no data yet (was isLoading in v4)
// isLoading = isPending && isFetching
// status: 'pending' | 'error' | 'success'  (not 'loading')
```

### Options renamed

```ts
// WRONG — v4 option names
useQuery({ cacheTime: 600000, keepPreviousData: true, useErrorBoundary: true })

// CORRECT — v5
useQuery({ gcTime: 600000, placeholderData: keepPreviousData, throwOnError: true })
```

### onSuccess/onError callbacks removed from useQuery

```ts
// WRONG — v4 callbacks (removed in v5)
useQuery({ queryKey, queryFn, onSuccess: (data) => toast(data) })

// CORRECT — use useEffect
const { data } = useQuery({ queryKey, queryFn })
useEffect(() => { if (data) toast(data); }, [data])
```

### Infinite query requires initialPageParam

```ts
// WRONG — v4 default pageParam
useInfiniteQuery({ queryKey, queryFn: ({ pageParam = 0 }) => fetch(pageParam) })

// CORRECT — v5
useInfiniteQuery({
  queryKey,
  queryFn: ({ pageParam }) => fetch(pageParam),
  initialPageParam: 0,
  getNextPageParam: (lastPage) => lastPage.nextCursor,
})
```

### Hydrate renamed

```ts
// WRONG — v4
import { Hydrate } from '@tanstack/react-query'
<Hydrate state={dehydratedState}><App /></Hydrate>

// CORRECT — v5
import { HydrationBoundary } from '@tanstack/react-query'
<HydrationBoundary state={dehydratedState}><App /></HydrationBoundary>
```

## v5 best practices

```ts
// Query key factories — type-safe, consistent
export const userKeys = {
  all: ['users'] as const,
  detail: (id: string) => [...userKeys.all, 'detail', id] as const,
}

// queryOptions() — reusable, typed
export const userQueryOptions = (userId: string) =>
  queryOptions({
    queryKey: userKeys.detail(userId),
    queryFn: () => fetchUser(userId),
    staleTime: 5 * 60 * 1000,
  })

// Optimistic mutation with rollback
useMutation({
  mutationFn: updateTodo,
  onMutate: async (updated) => {
    await queryClient.cancelQueries({ queryKey: todoKeys.detail(updated.id) })
    const previous = queryClient.getQueryData(todoKeys.detail(updated.id))
    queryClient.setQueryData(todoKeys.detail(updated.id), old => ({ ...old, ...updated }))
    return { previous }
  },
  onError: (err, updated, context) => {
    queryClient.setQueryData(todoKeys.detail(updated.id), context?.previous)
  },
  onSettled: () => queryClient.invalidateQueries({ queryKey: todoKeys.all }),
})
```

## Plain fetch with cleanup (no library)

```jsx
useEffect(() => {
  let cancelled = false;
  async function load() {
    try {
      const data = await fetchUsers();
      if (!cancelled) setUsers(data);
    } catch (err) {
      if (!cancelled) setError(err.message);
    } finally {
      if (!cancelled) setLoading(false);
    }
  }
  load();
  return () => { cancelled = true; };
}, []);
```

## AI mistake cheatsheet

| AI generates | Correct |
|---|---|
| `useQuery(key, fn, opts)` — 3-arg | `useQuery({ queryKey, queryFn, ...opts })` |
| `status === 'loading'` (v5) | `status === 'pending'` or `isPending` |
| `onSuccess` in `useQuery` | `useEffect` on `data` |
| `cacheTime` | `gcTime` |
| `keepPreviousData: true` | `placeholderData: keepPreviousData` |
| `<Hydrate>` | `<HydrationBoundary>` |
| `pageParam = 0` as default arg | `initialPageParam: 0` in options |
