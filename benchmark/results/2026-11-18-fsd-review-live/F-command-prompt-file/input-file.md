# Instructions

You are reviewing a functional specification (FSD) against the real source code of the app it describes. Find every
place where the FSD and the code disagree, and every statement of the FSD the code does as written.

## What you have
- The FSD, below under "The FSD". Each heading shows its section id in brackets, e.g. `[section id: 3.1]`.
- The source: the repo is your current folder. Treat the code as the truth of what the app does today.
- The scope, below under "Scope": the screens the FSD covers.

## Rules
- Read only files inside your current folder. Permission rules keep you out of stangent's own files (`specs/`,
  `.stangent/`, `stangent.yaml`); don't try them, nor any other folder on this machine, nor the web. You can't change files.
- Use search (Grep, Glob) and reading freely. Back every finding with code you actually read.

## What to report, four kinds
1. **gap**: the FSD describes a feature or behaviour the code does not have.
2. **conflict**: the FSD states a rule, limit, default or behaviour that the code implements differently (say what the
   code does).
3. **undocumented**: something the code has that the FSD leaves out: either (a) a whole screen inside the scope that the
   FSD does not describe, or (b) a user-visible option or action on a screen the FSD does describe, that the FSD does not
   mention.
4. **match**: a statement of the FSD (a rule, limit, default, behaviour, option or action) that the code does as written.
   Quote it and give the line(s) that do it. Report a match only for a statement whose code you read and found doing
   it, one match per statement, so a reader can see which sentences the code backs.

A statement is one kind only: a match, a gap or a conflict, never two. When one sentence holds two rules and the code
follows one but not the other (the list shows 20 orders per page, newest first; the code pages by 20 but sorts oldest
first), report the difference only, quoting the whole sentence; no match for it. Report each difference and each match you can
back with code. A false alarm costs a reviewer's time; a missed difference is a missed bug.

## Output
Write one JSON object matching the output schema below to the file `/private/tmp/claude-501/-Users-hohkokyoung-Projects-stangent/df5ab460-040d-4e70-a1ab-7c2592e09d0f/scratchpad/diagF/out/output.json`:

- `state`: gap, conflict, undocumented or match.
- `section`: the FSD section id the finding belongs to, exactly as in the brackets; for an undocumented screen with no
  fitting section, null.
- `fsdQuote`: for a gap, conflict or match, the FSD sentence(s) concerned, copied word for word from that section; for
  undocumented, null.
- `screen`: for undocumented, the screen's full frontend route as the router resolves it, e.g. `/orders/:orderId`;
  otherwise null.
- `note`: one or two sentences: what differs (for a match, what the code does), naming the option, action or rule in
  plain words.
- `evidence`: `path:line` for code you read, the path relative to your current folder. A match gives the line(s) that
  do it, never a path alone.

One finding per difference or matched statement (don't merge two into one; don't split one into several). Code checks
that each section exists, each `fsdQuote` is word for word inside its section and each `path:line` exists; a finding that
fails gets one chance to be fixed, then it is dropped.

# Scope

The FSD covers the screens whose routes start with these prefixes: `/home`, `/library`, `/all-series`, `/want-to-read`, `/collections`, `/lists`, `/browse`, `/person`, `/settings`.

# The FSD

# Kavita Functional Specification: Reading and Administration Screens

## 1 Home  [section id: 1]

The Home screen is the default screen and shows rows of series such as On Deck, Recently Updated Series, Newly Added Series and More In a randomly picked genre. The On Deck row shows up to 50 series. Clicking the title of one of these rows opens All Series with a matching filter and sort order. Users without the Read Only role can choose which rows appear, and in what order, from the Customize page in their settings. Each row on the Home screen can be collapsed, and Kavita remembers the choice on the next visit.

## 2 Libraries  [section id: 2]

### 2.1 Library screen  [section id: 2.1]

The library screen shows the library's name, its actions menu and the number of series it holds. Series appear as cards with a jump bar for skipping to a starting letter, and several series can be selected at once for a bulk action. A Random button on the library screen opens a randomly chosen series from that library.

### 2.2 Filtering series  [section id: 2.2]

The filter panel builds rules on fields such as genre, tag, people, read progress, rating and publication status, and the rules can match all or any of them. Results can be sorted by a chosen field in either direction and limited to a set number, and a filter can be named and saved as a smart filter.

### 2.3 All Series  [section id: 2.3]

All Series lists the series from every library the user can access, with the same filter panel and bulk selection as a library screen.

## 3 Series, volumes and chapters  [section id: 3]

### 3.1 Series details  [section id: 3.1]

- The series screen shows the cover, summary and ratings, with a Read button labelled Read, Continue or Re-Read depending on the user's progress.
- The arrow beside the Read button offers the same action in incognito mode, which does not save progress.
- A star button adds the series to Want to Read or removes it.
- The Edit button is shown only to administrators, and the Download button only to administrators and users with the Download role.
- The actions menu marks the series as read or unread, adds it to a reading list or collection, or sends it to a device; administrators also get actions such as scan, refresh metadata and delete.

Content is split into tabs such as Storyline, Volumes, Chapters, Specials, Related, Recommendations, Reviews and Details, and in book and light novel libraries the Volumes tab is called Books. Manga series open on the Volumes tab by default. Users can keep a private note on a series that only they can see.

### 3.2 Volume details  [section id: 3.2]

A volume's screen shows its cover, writers, cover artists, genres and tags, with its own Read and Download buttons and a Chapters tab listing its chapters. The Reviews tab appears on a volume only when the volume holds a single chapter.

### 3.3 Chapter details  [section id: 3.3]

A chapter's screen shows its release date, writers, cover artists, genres and tags, always has a Reviews tab, and has a Related tab listing the reading lists that contain the chapter when there are any.

## 4 Readers  [section id: 4]

Archive and image files open in the image reader and EPUB files open in the book reader, both using the settings of the user's reading profiles.

### 4.1 Image reader  [section id: 4.1]

The top bar shows a back button, the chapter title and how far through the series the user is. Users with the Bookmark role, and administrators, can bookmark the current page from the top bar or by double-clicking the page. The reading mode cycles between left-right paging, up-down paging and webtoon scrolling, and the layout can show one page, two pages side by side, or two pages in manga order. Image splitting and image scaling can only be changed in single-page layout. Brightness can be set anywhere from 0% to 100%. In incognito mode a glasses icon appears beside the title, and clicking it turns incognito off so progress is saved again. Settings changed in the reader can be saved to the reading profile they came from or turned into a new reading profile. A slideshow mode turns the page automatically after a set number of seconds.

### 4.2 Book reader  [section id: 4.2]

The settings drawer sets the font family, font size, line spacing and margin, along with reading direction, writing style, tap pagination, immersive mode and fullscreen. Font size can be set from 50% to 200%. The layout can be Scroll, 1 Column or 2 Column, with 2 Column only offered for horizontal writing, and the colour themes are Dark, Black, White and Paper. Other drawers show the table of contents and the user's bookmarks and annotations, and selecting text offers to copy, bookmark or annotate it. Selected text can be read aloud using the device's text-to-speech voice.

## 5 Want to Read  [section id: 5]

The Want to Read screen lists the series the user has starred, with a count and the same filter panel and bulk selection as a library screen. Kavita removes a series from Want to Read automatically as soon as the user finishes reading it.

## 6 Collections  [section id: 6]

### 6.1 Collections screen  [section id: 6.1]

The Collections screen shows every collection the user can see, with the name of the user who created it and a mark on promoted collections. It has a jump bar but no filter panel.

### 6.2 Collection details  [section id: 6.2]

A collection's screen shows its title, number of series and actions menu, and a collection synced from an outside service also shows how many of the source's series are on this server and when it last synced. Its series can be filtered and selected in bulk. The series in a collection can be exported to a CSV file.

## 7 Reading lists  [section id: 7]

### 7.1 Reading lists screen  [section id: 7.1]

The Reading Lists screen shows every reading list the user can see, with a filter panel and a jump bar. Administrators and users with the Promote role can promote or unpromote a list from its actions menu. New reading lists are created from the Add to Reading List action on a series, volume or chapter.

### 7.2 Reading list details  [section id: 7.2]

A reading list's screen shows its summary, writers, cover artists, tags and number of items, with a Read button whose menu also offers incognito reading. The list's owner sees an Edit button and, on larger screens, a Reorder button. In reorder mode each item gets a remove button, and an Order Numbers switch adds a box to each item for typing its new position. A filter box for finding items appears once a list has more than 25 items. The edit dialog holds the title, which is required and must be unique, the summary, the starting and ending month and year, tags and the cover image, and only administrators see its Promote switch. The owner can share a reading list through a public link that opens without signing in.

## 8 Browse  [section id: 8]

### 8.1 People  [section id: 8.1]

The People screen lists people with the number of series and issues each appears in, along with a filter panel and a jump bar, and clicking a person opens that person's page.

### 8.2 Person page  [section id: 8.2]

A person's page shows their description, the other names they are known by and their roles. A Known For row lists their works, followed by a row for each role they have held.

### 8.3 Tags  [section id: 8.3]

The Tags screen lists every tag with the number of series and issues that carry it, and clicking a tag opens All Series filtered to that tag, though tags that no series uses cannot be clicked.

## 9 User settings  [section id: 9]

### 9.1 Account  [section id: 9.1]

- The Account page shows the user's username, email address, password and age restriction, and users with the Read Only role cannot change their username or email address.
- Changing the email address requires the current password, and the page shows whether the email has been confirmed.
- Changing the password requires the current password and is only open to administrators and users with the Change Password role.
- A new password must be at least 8 characters long.
- The age restriction can only be changed by users with the Change Restriction role.
- Users can turn on two-factor sign-in with an authenticator app.

### 9.2 Preferences  [section id: 9.2]

The Preferences page holds account-wide settings such as the site language, Disable Animations, ColorScape and Data Saver. When Prompt on Download is on, Kavita asks for confirmation before any download larger than 500 MB. Prompt for Re-Reads sets after how many days without progress Kavita offers to re-read, and 0 turns the prompt off. Social settings control whether the user shares reviews, annotations and reading activity with other users and whether they see other users' annotations, limited to chosen libraries and a maximum age rating. Users with the Read Only role cannot change their preferences.

## 10 Server administration  [section id: 10]

These pages are shown only to administrators.

### 10.1 General settings  [section id: 10.1]

The General page holds the host name, base URL, IP addresses and port, and the base URL must start and end with a slash. A notice warns that changing the port, base URL, cache size or IP addresses needs a restart of Kavita. Days of Backups and Days of Logs each accept a value from 1 to 30 and both default to 30. Cache Size must be at least 50 MB, and switches turn OPDS, folder watching and anonymous usage collection on or off. A Reset to Default button restores these settings after a confirmation. Changes on this page are stored only when the administrator presses Save. A maintenance mode switch blocks all non-admin users from signing in until it is turned off.

### 10.2 Users  [section id: 10.2]

The Users page lists each user with their last active time, the libraries they can access and their roles, and users who have not validated their email carry a Pending badge. The Invite button opens a form for the new user's email address, libraries, roles and age restriction. The Login role is selected by default, and choosing Admin locks the other roles. If the invite email cannot be sent, the form shows the invite link so the administrator can pass it on. Every row except the administrator's own has buttons to edit or delete the user, and pending users also get buttons to resend the invite or finish setting up the account. An invite can be given an expiry date, after which its link stops working.

### 10.3 Libraries  [section id: 10.3]

The Libraries page lists each library with its type, shared folders and last scan time, and each row can scan, edit or delete its library, with a confirmation before deleting. Adding a library walks through four steps, General, Folder, Cover Image and Advanced, and the library type can be Manga, Comic, Comic (Flexible), Book, Light Novel or Image. The library name must be unique, and a library cannot be saved without at least one folder and at least one file type. Each type starts with its own file types:

| Library type | File types selected by default |
|---|---|
| Manga | Archive, Image |
| Comic, Comic (Flexible) | Archive |
| Book | Pdf, Epub |
| Light Novel | Epub |
| Image | Image |

Include in Dashboard and Include in Search are turned on by default. Manage Collections is turned on by default for a new library.


# Output schema

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "urn:stangent:schema:fsd-review-output:v2",
  "title": "What stangent-fsd-reviewer returns (layer 5, D341; matches D366)",
  "description": "Every place the FSD and the code disagree, one finding per difference: a gap (the FSD describes what the code doesn't have), a conflict (the code does it differently) or undocumented (the code has it, the FSD leaves it out); and every FSD statement the code does as written, a match (D366). Code checks the section exists, every fsdQuote is word for word inside its section, and every evidence path (and line) exists at the reviewed commit.",
  "type": "object",
  "required": ["findings"],
  "additionalProperties": false,
  "properties": {
    "findings": {
      "type": "array",
      "maxItems": 600,
      "items": {
        "type": "object",
        "required": ["state", "section", "fsdQuote", "screen", "note", "evidence"],
        "additionalProperties": false,
        "properties": {
          "state": { "enum": ["gap", "conflict", "undocumented", "match"] },
          "section": { "description": "The FSD section id the finding belongs to, exactly as in its heading's brackets; null only for an undocumented screen no section fits.", "type": ["string", "null"], "maxLength": 300 },
          "fsdQuote": { "description": "For a gap, conflict or match: the FSD sentence(s) concerned, copied word for word from that section. For undocumented: null.", "type": ["string", "null"], "maxLength": 2000 },
          "screen": { "description": "For undocumented: the screen's full frontend route as the router resolves it, e.g. /orders/:orderId. Otherwise null.", "type": ["string", "null"], "maxLength": 300 },
          "note": { "description": "One or two sentences: what differs, or for a match what the code does, naming the option, action or rule in plain words.", "type": "string", "minLength": 3, "maxLength": 1000 },
          "evidence": {
            "description": "Code you read that backs the finding (for a match, the line(s) that do it): paths relative to the repo root with forward slashes, each with its line (path:line or path:from-to); a path alone for where a missing thing would be.",
            "type": "array",
            "minItems": 1,
            "maxItems": 12,
            "items": { "type": "string", "minLength": 1, "maxLength": 400 }
          }
        }
      }
    }
  }
}
```

When the file is written, reply with only the number of findings by kind and the file's path.
