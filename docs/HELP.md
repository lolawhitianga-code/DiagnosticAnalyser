# Diagnostic Analyser - help

What every button and field does. Written for the person using the app, not the person building it.

Each topic below is marked with an anchor comment carrying an id such as `main.analyse` or
`production.browse`. Those anchors are the link between this file and the app: the **?** button
reads them so a control can show its own topic. Keep the ids stable - renaming one breaks the help
for that control. Adding a topic is safe.

---

## The main window

<!--help:main.stats-->
### The tiles across the top

A quick read on the last week without touching the grid.

- **arrived today** and **last 7 days** count bundles that came in.
- **failed** counts bundles that could not be unpacked or had no serial number in them. Anything
  above zero is worth a look - those bundles are in the list with a red status and a reason.
- **machines seen** and **total stored** are all-time, not the last week.
- **busiest customer (7 days)** is whoever has sent the most. A customer suddenly at the top of
  that tile is usually a machine having a bad week.

<!--help:main.watchfolders-->
### Watch folders

The folders the app checks for new diagnostic bundles. Most sites use two: wherever the mail
client saves attachments, and wherever people drop files by hand.

Sub-folders are included. **Add folder...** opens a picker; **Remove** takes the highlighted one
off the list. Changes take effect next time you press Start Monitoring.

<!--help:main.extensions-->
### Extensions

Which file types count as a diagnostic bundle. `.szip` is the normal one. Add `.zip` if a customer
renames them before sending. Separate several with commas.

<!--help:main.maxage-->
### Only files newer than ... days

Ignores bundles older than this when scanning a folder, so pointing the app at a folder with years
of history does not import all of it. Set it to 0 to process everything.

The age comes from the bundle itself - the date in its file name, or the newest file inside it -
not from when it was copied onto this PC.

<!--help:main.monitoring-->
### Start Monitoring / Stop Monitoring

Starts watching the folders above. New bundles are unpacked, read and added to the list on their
own while this is running. Stopping does not remove anything already imported.

<!--help:main.notify-->
### Notify on arrival

Pops a tray notification when a bundle lands. Useful if the app sits minimised on a second screen.

<!--help:main.groupby-->
### Group by

Groups the list by customer, machine or status instead of one flat list. Handy when a site has sent
a lot at once.

<!--help:main.import-->
### Import files

Takes bundles straight from wherever they are - no need to save them into a watched folder first.
Pick one or several.

You can also **drag bundles onto the window** from Explorer or an email, which is the shortest way
in.

Imported files skip the "newer than N days" filter that watched folders use. A folder needs that
filter or re-copying an archive would flood the list; a file you handed over deliberately does not.

<!--help:main.refresh-->
### Refresh

Re-reads the list from the database. Only needed if something looks out of date.

<!--help:main.logsearch-->
### Search in logs...

Searches inside the log files of every stored bundle, not just the file names. Use it to find which
machines have ever reported a particular error.

<!--help:main.cleanup-->
### Keep extracts for ... days / Clean up now

Unpacked bundles take up disk space. This deletes the unpacked copies older than the number of days
given, keeping the database entry and the original `.szip`.

A bundle whose unpacked files have been cleaned up cannot be analysed again until it is
re-processed. Set the number to 0 to keep everything.

<!--help:main.integrations-->
### Alerts and Zoho...

Opens the settings for burst alerting, outgoing email and the Zoho Desk connection. See the
**Alerts and Zoho** section below.

<!--help:main.cleardatabase-->
### Clear database...

Empties the stored history. It asks first. The original `.szip` files are not touched, so
everything can be re-imported - but case notes, ticket numbers and benchmark marks are lost.

<!--help:main.search-->
### Search

Filters the list on file name, serial number, customer, model and site. Type part of a serial and
the list narrows as you go.

<!--help:main.statusfilter-->
### Status

Shows only bundles that processed cleanly, or only ones that failed. Failed means the bundle could
not be unpacked, or had no serial number in it.

<!--help:main.datefilter-->
### Arrived from / to

Narrows the list to bundles that arrived between two dates. Leave either end empty for open-ended.

<!--help:main.baselinesonly-->
### Baselines only

Shows just the bundles marked as known-good. Use it to find the benchmark for a machine type.

<!--help:main.clearfilters-->
### Clear

Clears the search box, the status filter and both dates in one go.

<!--help:common.close-->
### Close

Shuts this window. Nothing is lost - anything already saved or built stays where it is.

<!--help:common.greyedout-->
### Why is a button greyed out?

A greyed out button is waiting for something. Most need a file selected in the list first;
**Compare** also needs a benchmark set, and **Create package** needs the two feedback boxes filled
in.

The **?** works on greyed out buttons too, so you can find out what one is for without having to
make it available first.

<!--help:main.grid-->
### The file list

One row per bundle. Most columns come from the machine's own `Machine.xml`:

- **Serial Number**, **Model**, **Customer**, **Location**, **Version** - what the machine reported
  about itself.
- **Ticket** and **Zoho #** - what has been raised for it.
- **Repeat** - marked when the same machine has sent another bundle inside the repeat window. A
  repeat usually means the first fix did not hold.
- **Baseline** - marked when this bundle is held up as known-good.
- **Arrived** - when the bundle was made, from its file name or from the newest file inside it.
- **Status** - processed, or failed with the reason in **Details**.

Select one row for most buttons; select several for Analyse.

<!--help:main.analyse-->
### Analyse

Reads the three Spida logs in the selected bundle and writes a report saying what went wrong.

It starts from the last entry in the machine log and works backwards, because these bundles are
usually sent within minutes of the problem. The report leads with what the operator typed into
`SupportInfo.txt`, then how the session ended, then what the operator last asked the machine to do.

Select several rows to analyse them together.

<!--help:main.sendtoclaude-->
### Send to Claude

For when the report got it wrong. It analyses the bundle, then opens a window for you to write what
was really wrong and how you knew. What comes out is a zip holding the whole bundle, the report and
your notes, ready to hand to Claude so the next version reads cases like it properly.

This is the main way the analysis gets better. A note saying *how you read the log* is worth more
than the bundle on its own.

<!--help:main.compare-->
### Compare

Measures the selected bundle against the one marked as benchmark master: step sequence, how long
each step took, and which settings differ.

Needs a benchmark set first - right-click a known-good bundle from the same machine type and choose
**Set as benchmark master**.

<!--help:main.report-->
### Report

Builds an HTML report from the stored bundles: every fault occurrence across the fleet, or the case
for a design change. See the **Reports** section below.

Whatever is highlighted in the list carries across, so the serial is already filled in when the
window opens. Highlight several rows and all their machines carry across, which is how a comparison
gets set up. Several bundles from the same machine count once.

With nothing selected the report covers every machine, which is what it has always done.

<!--help:main.production-->
### Production

Reads ProdLogV2 weekly production logs into this PC and builds a production report - panels, cube,
lineal metres and availability. See the **Production reports** section below.

<!--help:main.openfolder-->
### Open folder

Opens the unpacked bundle in Explorer, so you can get at files the app does not read - the job
files, the machine configuration, the CloudLog folder.

<!--help:main.openlogs-->
### errorlog.txt / machinelog.txt / changelog.txt

Opens that log from the selected bundle in your text editor. Greyed out when the bundle does not
carry it.

<!--help:main.iostate-->
### I/O at a moment

Opens machinelog.txt at a point in time and shows what every input and output was doing right then.

Reading a log line by line tells you what changed. It does not tell you what was already held on,
which is usually the thing that explains the fault - a clamp still energised, a sensor still made.
This works that out by replaying every change from the top of the file.

Greyed out when the bundle has no machinelog.txt. Also on the right-click menu.

<!--help:main.casenotes-->
### Case notes

**Ticket number** and **Notes** are yours to fill in - they are not read from the bundle. **Save
notes** writes them against this bundle so they are still there next time.

**Copy summary for ticket** puts a short summary on the clipboard - machine, serial, customer,
what arrived and when - ready to paste into a ticket.

<!--help:main.clearbenchmark-->
### Clear benchmark (right-click)

Unsets the benchmark master, so no bundle is held up as the one to measure against. Compare stays
greyed out until another is set.

<!--help:main.setmaster-->
### Set as benchmark master (right-click)

Marks this bundle as the one to measure others against. One at a time; setting a new one replaces
the old. **Clear benchmark** unsets it.

Pick a bundle from a machine running well, ideally the same model as the machines you will compare.

<!--help:main.markbaseline-->
### Mark as known-good baseline (right-click)

Flags this bundle as an example of the machine working properly. Baselines are a reference library;
the benchmark master is the single one Compare uses.

<!--help:main.showall-->
### Show all machines (right-click)

Undoes **Show this machine's history** and puts every bundle back in the list.

<!--help:main.reportonmachine-->
### Report on this machine (right-click)

Opens the report builder with this machine's serial already in the scope box.

Right-clicking a row that is part of a larger selection carries the whole selection across, for
comparing machines against each other. Right-clicking anywhere else takes just that row.

<!--help:main.machinehistory-->
### Show this machine's history (right-click)

Filters the list to every bundle from this serial number, oldest to newest. The quickest way to see
whether a fault is new or has been going on for months. **Show all machines** puts the list back.

---

## The analysis window

<!--help:analysis.report-->
### The report

Plain text, in the order a technician would work: what the operator said, how the session ended,
what the operator last asked for, whether the motors confirmed, drive faults, then the machine's
own history and what to check.

Anything the app is unsure about is labelled **Inferred** or **Unconfirmed** rather than stated
flat. Those labels matter - the report gets quoted to customers.

<!--help:analysis.copy-->
### Copy to clipboard

Copies the whole report, ready to paste into a ticket or an email.

---

## Sending feedback to Claude

<!--help:feedback.verdict-->
### How did it do?

Four choices: it got it right, partly right, missed it, or sent me the wrong way. Pick honestly -
"missed it" is the most useful one, because that is where the analysis has a gap.

<!--help:feedback.whatwaswrong-->
### What was actually wrong?

The real fault, in your words. What you would tell another technician over the phone.

<!--help:feedback.howyouknew-->
### How did you know?

The most valuable box on this window. Not what was wrong, but *how you worked it out* - which lines
you looked at, what you compared, what told you. A rule can be written from that; it cannot be
written from a conclusion.

Paste the actual log lines if that is what convinced you.

<!--help:feedback.whatshouldchange-->
### What should the report have done?

What you wanted it to say and did not. Optional, but it turns your reading into something the next
version can check on every export.

<!--help:feedback.raisedby-->
### Your name

So a follow-up question has somewhere to go.

<!--help:feedback.create-->
### Create package

Builds the zip: the whole bundle, the report as it stood, and your notes turned into prompts.
Greyed out until *what was wrong* and *how you knew* are both filled in - a package without those
teaches nothing.

**Show me the file** opens it in Explorer.

---

## Reports

<!--help:report.kind-->
### What sort of report

**Fault benchmarking** lists every fault occurrence across the fleet, most recent first, then the
pattern. **Mechanical change case** builds the argument for a design change on the same evidence.

Both are marked internal use only.

<!--help:report.subject-->
### What it is about

Goes in the title, e.g. `PlatePresentSwitch`. Leave it empty for a general report.

<!--help:report.period-->
### Period

How far back to look. The period is printed in the report header, so a month's figures can never be
read as an all-time total.

<!--help:report.scope-->
### Serial numbers / Machine types

Leave both empty for every machine with a stored bundle. Separate several with commas.

Opened from the file list, this is already filled in with whatever was highlighted. Change it
freely - it is only a starting point.

Every machine in scope appears in every chart and table, including ones with nothing recorded - a
zero is a finding, and a machine quietly dropped is not a comparison.

<!--help:report.kinds-->
### What to count

Which kinds of fault to gather. Plate sensor drop-outs and drive faults are the usual starting
point. Software errors from `ErrLog.txt` are off by default because there are a lot of them and
most are not what stopped the machine.

<!--help:report.changecase-->
### The change case

**Seconds lost each time it happens** is the one figure the logs cannot give you - they record that
a fault happened, not what it cost. Leave it blank and the report says the downtime is not known
and how to find out. It will not make a number up.

**Where that number came from** is printed beside the estimate, so nobody later mistakes a guess
for a measurement.

<!--help:report.showfile-->
### Show me the file

Opens Explorer with the report highlighted, so you can attach it to an email or drop it somewhere
shared.

<!--help:report.build-->
### Build report

Writes one self-contained HTML file to the `Reports` folder beside the database. No internet needed
to open it - fonts and charts are inside the file, so it works on a factory PC with the network
unplugged.

---

## Production reports

<!--help:production.machine-->
### Which machine

ProdLogV2 files carry no machine identity at all - not in the file, not in the file name, not in
the content. So the serial has to come from somewhere else: **Browse...** reads it out of the
folder name where there is one, opening this window from a highlighted row carries that row's
serial across, and otherwise type it.

It is what the stored weeks are filed under, so getting it wrong files one machine's production
against another.

**Machine name** and **Site** are for the report header only.

<!--help:production.browse-->
### Browse...

Picks the folder holding the ProdLogV2 files, and reads the serial out of the folder name -
`D:\Production\M21737\Reports` gives M21737, closest folder first.

It only fills the serial box when it is empty, so it never overwrites something you typed. Machine
types like TornadoM450 are deliberately not matched, because filing production under a model name
would merge every machine of that type into one set of figures.

<!--help:production.folderhint-->
### The line under the folder box

Says what was found before anything is read - whether the folder holds production logs, or how many
sub-folders do and which have a serial in the name. A wrong folder is obvious before you import
rather than after.

<!--help:production.subfolders-->
### Each sub-folder is a different machine

For pointing at a parent folder with one folder per machine. Each is filed under the serial in its
own name. A sub-folder whose name has no serial in it is skipped and named in the notes, never
filed under a guess.

<!--help:production.replace-->
### Re-read weeks already stored

Normally a week already held is skipped rather than counted twice. Tick this to read it again and
replace what is stored - only needed if you think the earlier import was wrong.

<!--help:production.stored-->
### Already stored

What this PC holds: one line per machine with how many weeks and how many panels. **Refresh**
re-reads it.

<!--help:production.shift-->
### Shift model

Availability is worked out from this, not measured. It is rostered shift time, less breaks, less
the wait before the first panel, the wait after the last, and every gap in between longer than the
stop threshold.

Two machines only compare on availability if they are on the same model, and the report prints
whichever one was used. Change the model and every availability figure changes with it.

**Ignore shift** is there for when nobody has confirmed the roster. Availability is then not
reported at all rather than worked out from a guess - and panels, cube and lineal metres are
measured either way, so the report is still worth having.

<!--help:production.shifttimes-->
### Setting the shift by hand

Shift start and end, the breaks, and how long a gap has to be before it counts as an unplanned
stop.

Breaks are written as pairs of times: `10:00-10:15, 12:30-13:00`. Anything that is not a pair is
skipped rather than losing the whole line, and the summary underneath says what was understood -
check it matches before building.

Breaks are taken out of the middle of a gap, not just off the ends. A three hour stoppage that
happens to start at lunch counts as three hours less lunch, not as nothing at all.

<!--help:production.import-->
### Import logs

Reads the weekly logs into this PC's database. Logs are read once and kept, so a report covering a
year does not re-read a year of files.

Production data also arrives on its own: every diagnostic `.szip` carries a few days of it, and
because a bundle knows its own serial that data is filed automatically when the bundle is
processed.

<!--help:production.refresh-->
### Refresh

Re-reads what this PC has stored. Only needed after importing from somewhere else, or if the list
looks out of date.

<!--help:production.build-->
### Build report

Builds the production report from what is stored - panels, cube, lineal metres, output by month and
availability. **Open it** opens the file.

Two files are written. The one that opens is a page you work: switch the period between **month,
week, day and hour**, switch the measure between **panels, cube and lineal metres**, click any bar
or day to drill into it, and read the day drawn as a wall - one stud per panel, stood where it
finished, with breaks, stops and off-shift time shaded behind. The second, ending `-print`, is the
same figures laid out flat to print or paste into a ticket.

The page has its own **Shift and breaks** panel. Editing it there re-works availability, the stops
and the rates in front of you - so a site can put its own roster in. It changes nothing on this PC.

Everything is inside the one file, so it reads the same on a site PC with no internet.

---

## Searching inside logs

<!--help:logsearch.find-->
### Find text

Searches the log files of every stored bundle. Matches show with the machine, the file and the line
they were found on.

Use it to answer "has any other machine ever done this?".

<!--help:logsearch.open-->
### Open this log file

Opens the log the highlighted match came from, so you can read around it.

---

## Inputs and outputs at a moment

<!--help:iostate.browse-->
### Open a log

Reads any MachineLog.txt, not only one from a stored bundle. Use it for a log somebody has emailed
you before it has been through the watch folder.

<!--help:iostate.moment-->
### Moment

The moment being reported. Type a time like `07:53:38` or `07:53:38.9085106` and press Go, or just
click a line in the list.

The state shown is as at that line, **including** it - so clicking a line that reads
"TopStudClamp Set On" shows that output on.

If the log runs past midnight the same clock time happens twice, and a typed time lands on the
later one. Click the line instead when that matters. The page says so underneath when it applies.

<!--help:iostate.step-->
### Stepping

`|<` and `>|` jump to the first and last line. `< Change` and `Change >` step to the previous or
next input or output change, skipping everything else, which is how you follow a sequence without
scrolling through thousands of step numbers.

<!--help:iostate.onlyon-->
### Only show what is on

Hides everything that was off, leaving the short list. Handy for a ticket. Everything else about
the reading is unchanged.

<!--help:iostate.copy-->
### Copy for ticket

Puts the whole reading on the clipboard as plain text - the moment, the line, and every input and
output with its state. Paste it straight into Zoho.

<!--help:iostate.lines-->
### The log lines

Every line of the file, in order. Clicking one sets the moment. Input and output changes are shown
in blue so they stand out from step numbers and motion events.

<!--help:iostate.filter-->
### Filter

Narrows the line list to lines whose tag, detail, kind or time contains what you type. Press Enter
or Filter. It only hides lines from the list - the state is still worked out from the whole file,
so a filtered view never changes the answer.

Stepping to a change outside the filter clears it rather than refusing to move.

<!--help:iostate.outputs-->
### Outputs

Every output this log ever moves, in name order so rows stay put as you step through time. On rows
are shaded.

**Held** is when it last changed. **Moves** is how many times it has changed by the moment you are
looking at - a clamp on its fortieth move of the shift is a different story from one on its first.
**In file** is the total for the whole log, there for scale.

A point is listed by name **and** address because neither is unique. The same name can be two
coils, one per side of the machine, and the same address can carry two named points. Where that
happens it is spelled out underneath.

A greyed italic row means nothing had touched that output yet at this moment, so its state is read
backwards from its next change rather than measured.

<!--help:iostate.inputs-->
### Inputs

The same, for inputs - sensors, switches and confirms. `Changed to 1` is on, `Changed to 0` is off.

The list is built from the log itself, because nothing else carries it: Machine.xml has no I/O map.
So an input only appears once it has moved at least once somewhere in the file. One that never
moves is not in the list at all, and that absence is itself worth noticing.

---

## Alerts and Zoho

<!--help:settings.burst-->
### Alert when a machine sends several files in a short window

A machine sending several bundles in a row is a machine somebody is fighting with. This raises an
alert when one serial sends more than the given number of files inside the given number of hours.

<!--help:settings.email-->
### Email me when a burst is detected

Sends the alert, with the analysis, to the addresses given. The **Host**, **Port**, **SSL**,
**Username**, **Password** and **From address** are your outgoing mail server - the same settings
your mail client uses. **Send test email** proves it before you rely on it.

<!--help:settings.zoho-->
### Post the analysis to a Zoho ticket

Raises or updates a Zoho Desk ticket with the analysis when a bundle arrives.

**Client ID**, **Client secret** and **Refresh token** come from your Zoho API console. The **API
base URL** and **Accounts base URL** must match your data centre - the wrong one fails to connect
with an error that does not say why. **Test Zoho** checks the connection.

<!--help:settings.zohoreuse-->
### Reuse a ticket raised for the same machine within ... hours

Stops a machine sending five bundles from raising five tickets. Inside the window the analysis is
added to the existing ticket instead.

<!--help:settings.zohoprivate-->
### Post as a private note

Adds the analysis as an internal note rather than a reply the customer sees. Leave this on unless
you want customers reading the raw analysis.

<!--help:settings.save-->
### Save

Writes these settings and closes. Nothing here takes effect until you press it.

<!--help:settings.ssl-->
### SSL

Whether your mail server wants an encrypted connection. Almost always yes on port 587 or 465. If
test emails fail with a connection error, this is the first thing to try changing.

<!--help:settings.utc-->
### Timestamps in support file names are UTC

Whether the time in a bundle's file name is UTC or the machine's local time. Getting this wrong
shifts every arrival time by your time zone offset. Leave it as it is unless arrival times are
consistently out by a fixed number of hours.

<!--help:settings.maxage-->
### Only process files newer than ... days

The same setting as on the main window, kept here so it can be set alongside the others. 0 processes
everything.
