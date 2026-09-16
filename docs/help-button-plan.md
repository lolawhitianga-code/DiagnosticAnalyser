# The ? button - plan

Status: **plan only, nothing built.** The help file it reads from, `docs/HELP.md`, is written and
has 62 topics.

## What it does

Press **?** in the title row. The cursor turns into a question mark and the app goes into "ask
about" mode. Click any button, box or tick and a small popup appears beside it with that control's
topic from the help file. Click anywhere else, press Escape, or press **?** again to come out of it.

Nothing happens to the machine or the data while the mode is on - a click asks about the control
rather than pressing it. That is the whole point: somebody can find out what **Clear database...**
does without finding out the hard way.

## Why it reads the help file rather than tooltips

Tooltips are already on most of these controls and they are one line each. This is a different job:
a paragraph that says what a thing is *for*, what it needs first, and what it will not do.

Keeping that text in `HELP.md` means:

- One place to edit. A support person can correct the help in a text file without a rebuild.
- The same words on the page and in the popup, so they cannot drift apart.
- The help file stays readable on its own - it is a document first and a data file second.

The alternative, putting the text in the XAML or a resource file, was rejected for the opposite
reason: nobody reads a `.resx` for help, so it would rot.

## How a control finds its topic

An attached property on the control names its topic id:

```xml
<Button Content="Analyse"
        help:Help.Topic="main.analyse"
        Command="{Binding AnalyseSelectedCommand}" />
```

`HELP.md` carries the matching anchor:

```
<!--help:main.analyse-->
### Analyse
Reads the three Spida logs in the selected bundle ...
```

The parser is deliberately dull: find `<!--help:ID-->`, take the `###` line after it as the title,
and everything up to the next anchor as the body. No Markdown library, no front matter, no build
step. The ids are lower case, dotted, `window.control`.

**Where the topic is not on the control itself**, the lookup walks up the visual tree. That lets a
whole panel carry one topic - the six stat tiles, the case notes panel - without tagging every
`TextBlock` inside it.

## Shipping the help file

`HELP.md` is embedded in the exe as a resource, so a fresh build has working help with nothing to
copy. A `HELP.md` sitting beside the exe wins over the embedded one, the same way `logo.png`
already does. That way a site can fix a wrong sentence, or add a note about their own machines,
without waiting for a build.

## Pieces of work

| File | Job |
|---|---|
| `Core/Help/HelpTopic.cs` | id, title, body |
| `Core/Help/HelpFile.cs` | parses `HELP.md` into topics; `Find(id)` |
| `App/Services/HelpLibrary.cs` | loads the embedded or beside-the-exe file once, caches it |
| `App/Help/Help.cs` | the `Help.Topic` attached property and the tree walk |
| `App/Help/HelpModeBehaviour.cs` | turns the mode on, swaps the cursor, catches the next click |
| `App/Help/HelpPopup.xaml` | the popup: title, body, a "more help" link |
| `App/Controls/HelpButton.xaml` | the **?** itself, so every window gets the same one |

Then tagging the controls: 62 topics against roughly 70 controls across seven windows.

## Order

1. **Parser and topics first**, with tests, because everything else depends on the file being read
   correctly and that part can be tested on any platform.
2. **The attached property and the tree walk**, with a static check (below).
3. **The mode and the popup** - the only genuinely WPF-shaped piece, and the only part that cannot
   be verified here.
4. **Tag the controls**, window by window, main window first.

## Keeping it honest

WPF cannot be built or run on the Linux box this is developed on, so the guard has to be static.
`tools/check_xaml.py` already checks bindings and resource keys; it gains two rules:

- Every `help:Help.Topic="x"` in any XAML names a topic that exists in `HELP.md`. A typo in an id
  is otherwise a popup that silently says nothing.
- Every topic in `HELP.md` is used by at least one control, or is listed as prose-only. That catches
  the opposite rot: help written for a button that has since been renamed or removed.

Both are cheap and they are the only things that can go quietly wrong here.

## What it will not do

- **No editing from inside the app.** The file is the source; editing it in a popup invites two
  copies.
- **No search box.** 62 topics in one scrollable document is small enough to read. If it grows past
  a couple of hundred, revisit.
- **No context help on the grid rows.** A row is data, not a function. The **?** on the grid
  explains the columns once.
- **It does not replace tooltips.** They stay - they are faster for a reminder. This is for the
  first time somebody meets a button.

## Open questions

1. **Does the popup need a "show me the whole help" link?** Leaning yes, opening `HELP.md` in the
   default viewer. Cheap, and it gives somebody a way to read it end to end.
2. **Should ? stay on until Escape, or turn itself off after one click?** Leaning stay on, so
   somebody new can click round the toolbar learning it. Easy to change once it is in somebody's
   hands.
3. **Should the help file ship as Markdown or as something already formatted?** Markdown, on the
   grounds that a support person will edit it. The popup renders the handful of things the file
   actually uses - bold, bullets, paragraphs - rather than pulling in a Markdown renderer.
