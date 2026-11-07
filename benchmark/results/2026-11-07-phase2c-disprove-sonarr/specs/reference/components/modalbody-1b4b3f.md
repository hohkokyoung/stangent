# Modal Body

`ts|frontend/src/Components/Modal/ModalBody.tsx#ModalBody` · component · sonarr

From the code: ModalBody

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Displays the modal's content inside a scroller, with the scroll top fixed at 0. [1]
- **S2** When scrolling is enabled, the children are wrapped in an inner container. [1]
- **S3** When scrolling is 'none', the children are shown directly without an inner container. [1]

## Actions

Nothing found in the code.

## Rules

- **R1** The scroll direction defaults to 'vertical'. Any value other than 'none' counts as scrolling enabled. [1]
- **R2** If no class name is supplied, a scrolling style is used when scrolling is enabled and a plain body style when it is 'none'. [1]
- **R3** The inner container uses a default inner style unless a different one is supplied. [1]
- **R4** Other supplied properties and the ref are passed on to the scroller. [1][2]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/Modal/ModalBody.tsx#ModalBody`
2. [`ts|frontend/src/Components/Scroller/Scroller.tsx#Scroller`](../components/scroller-32f810.md)

<sub>commit 0bffefe · inputs dabb1c2 · slice of 11 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
