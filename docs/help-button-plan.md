# The ? button - plan

Status: **built.** All 62 topics are tagged to controls across seven windows, and `tools/check.sh`
is green. This is the plan as it was written, with a note at the end on what changed while building
it.

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
| `App/Help/HelpPopupHost.cs` | the popup: title, body (built in code, not XAML - see below) |
| `Theme.xaml` `HelpButton` style | the **?** itself, so every window gets the same one |
| `Core/Help/HelpText.cs` | turns a topic body into paragraphs and bullets |

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


---

## What changed while building it

Three things came out differently from the plan.

**The popup is written in C#, not XAML.** XAML cannot be compiled on the Linux box this is
developed on, so anything written there ships unverified. As plain C# it is covered by the same
type-check sweep as the ViewModels. That moved `HelpPopup.xaml` to `HelpPopupHost.cs`, and the
**?** button became a style in `Theme.xaml` rather than its own control.

**The paragraph handling moved into Core.** Joining the file's hard-wrapped lines back up, keeping
a wrapped bullet as one bullet, and splitting on `**bold**` is pure text work with no WPF in it -
and it is the part most likely to be quietly wrong. In `Core/Help/HelpText.cs` it is tested,
including against every topic in the shipped file.

**Pressing ? a second time did not come out of the mode.** The mode swallows every click so that a
click asks about a control rather than pressing it - which swallowed the press that turns it off
too, leaving Escape as the only way out. The mode now recognises a click on its own button. This is
the sort of thing that cannot be found by running it here, so it had to be reasoned through.

The two static checks in `tools/check_xaml.py` were worth having immediately: the first run
reported 30 topics written but not yet tagged to any control, which is exactly the list of what was
left to do.
