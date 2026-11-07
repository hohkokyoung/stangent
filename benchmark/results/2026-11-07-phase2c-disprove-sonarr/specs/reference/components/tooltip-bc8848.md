# Tooltip

`ts|frontend/src/Components/Tooltip/Tooltip.tsx#Tooltip` · component · sonarr

From the code: Tooltip

> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. Generated is not verified: nobody has approved it yet.

## Shows

- **S1** Shows an anchor element inline; the tooltip content is displayed in a floating box next to it when open. [1]
- **S2** The floating box contains a small arrow pointing to the anchor and the tooltip body content. [1]
- **S3** The floating content is rendered in the portal root element and is not rendered when the tooltip is closed or disabled. [1]
- **S4** The anchor carries an optional accessible label as its aria-label. [1]

## Actions

- **A1** Hovering the anchor opens the tooltip; it stays open while the pointer moves toward the tooltip through a safe polygon. [1]
- **A2** Focusing the anchor opens the tooltip when the content role is 'tooltip'. [1]
- **A3** Clicking the anchor toggles the tooltip on mobile browsers or when the content role is 'dialog'. [1][2]
- **A4** The tooltip can be dismissed with the standard dismiss interactions (such as Escape or an outside press). [1]

## Rules

- **R1** A tooltip never shows when isDisabled is true; isDisabled defaults to false. [1]
- **R2** The content role defaults to 'tooltip'; when it is 'dialog', the anchor gets role button and the content is wrapped in a non-modal focus manager with no initial focus. [1]
- **R3** The anchor is keyboard focusable (tabIndex 0) by default, unless isAnchorFocusable is false. [1]
- **R4** The tooltip is offset 10 px from the anchor and may flip and shift to stay in the viewport; flipping is allowed by default (canFlip true). [1]
- **R5** The floating box's maximum width and height are limited to the available space in the viewport, never below 0. [1]
- **R6** Mobile is detected by a mobile-device check on the browser. [2]
- **R7** The tooltip kind defaults to the default kind and affects the styling of the arrow and box; its placement comes from the position prop. [1]

## Side effects

Nothing found in the code.

## Couldn't see

Nothing: every line in this page's slice was resolved.

## Reviewer's findings

A separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.

- F1 on A1 (unsupported): Code uses useHover with safePolygon, but that it opens on hover is fine; however 'stays open while pointer moves toward it' is supported. Minor: no issue found beyond this. **rejected**: The reviewer concedes the statement is supported; useHover with safePolygon is in the source.

## Cited symbols

1. `ts|frontend/src/Components/Tooltip/Tooltip.tsx#Tooltip`
2. `ts|frontend/src/Utilities/browser.ts#isMobile`

<sub>commit 0bffefe · inputs 45e1c9d · slice of 18 points, 6 lines deep · written by claude-sonnet-5-5, checked by claude-sonnet-5-5</sub>
