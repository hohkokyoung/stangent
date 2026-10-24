# Tag List

`ts|frontend/src/Components/TagList.tsx#TagList` · component · sonarr

From the code: TagList

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Shows a group of labels, one for each tag, displaying the tag's label text. [1][2]
- **S2** Tags are shown in order of label text, sorted with numeric-aware comparison. [1][3]
- **S3** Each label uses the given kind, which defaults to the info kind. [1]

## Actions

Nothing found in the code.

## Rules

- **R1** Each tag id in the list is looked up in the tag list by id. Ids with no matching tag are left out. [1]
- **R2** Sorting compares the label values using locale comparison with numeric ordering. [3]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Cited symbols

1. `ts|frontend/src/Components/TagList.tsx#TagList`
2. [`ts|frontend/src/Components/Label.tsx#Label`](../components/label-250d13.md)
3. `ts|frontend/src/Utilities/Array/sortByProp.ts#sortByProp`

<sub>commit 0bffefe · inputs a225040 · slice of 17 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
