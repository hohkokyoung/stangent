# Page Content Body (scrolling container)

`ts|frontend/src/Components/Page/PageContentBody.tsx#PageContentBody` · component · sonarr

From the code: PageContentBody

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Shows its child content inside a vertically scrolling container. [1][2]

## Actions

- **A1** When the user scrolls the content, the scroll position is recorded under the page's scroll position key and the optional onScroll callback is called, unless scrolling is locked. [1][3][4]

## Rules

- **R1** Scroll events are ignored (not stored and not passed on) while the scroll lock is active. [1][4]
- **R2** The initial scroll position is restored from the saved value for the key only when navigating back (POP) and a key is given; otherwise it starts at 0. [3]
- **R3** Scroll positions are only saved when a scroll position key is provided. [3]
- **R4** On navigation that is not a back navigation (PUSH or REPLACE), the window scrolls to the top (0, 0) when the path changes. [3]

## Side effects

- **E1** The latest scroll position is stored in memory per scroll position key so it can be restored on back navigation. [3]

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/Page/PageContentBody.tsx#PageContentBody`
2. [`ts|frontend/src/Components/Scroller/Scroller.tsx#Scroller`](../components/scroller-32f810.md)
3. `ts|frontend/src/Helpers/Hooks/useScrollPosition.ts#useScrollPosition`
4. `ts|frontend/src/Utilities/scrollLock.ts#isLocked`

<sub>commit 0bffefe · inputs d8d6392 · slice of 19 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
