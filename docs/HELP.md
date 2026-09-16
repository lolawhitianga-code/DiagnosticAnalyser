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

<!--help:main.casenotes-->
### Case notes

**Ticket number** and **Notes** are yours to fill in - they are not read from the bundle. **Save
notes** writes them against this bundle so they are still there next time.

**Copy summary for ticket** puts a short summary on the clipboard - machine, serial, customer,
what arrived and when - ready to paste into a ticket.

<!--help:main.setmaster-->
### Set as benchmark master (right-click)

Marks this bundle as the one to measure others against. One at a time; setting a new one replaces
the old. **Clear benchmark** unsets it.

Pick a bundle from a machine running well, ideally the same model as the machines you will compare.

<!--help:main.markbaseline-->
### Mark as known-good baseline (right-click)

Flags this bundle as an example of the machine working properly. Baselines are a reference library;
the benchmark master is the single one Compare uses.

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
folder name where there is one, otherwise type it.

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

Availability is worked out from this, not measured. It is planned shift time, less breaks, less any
gap between panels that was not a break.

Two machines only compare on availability if they are on the same model, and the report prints
whichever one was used. Change the model and every availability figure changes with it.

<!--help:production.import-->
### Import logs

Reads the weekly logs into this PC's database. Logs are read once and kept, so a report covering a
year does not re-read a year of files.

Production data also arrives on its own: every diagnostic `.szip` carries a few days of it, and
because a bundle knows its own serial that data is filed automatically when the bundle is
processed.

<!--help:production.build-->
### Build report

Builds the production report from what is stored - panels, cube, lineal metres, output by month and
availability. **Open it** opens the file.

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

<!--help:settings.utc-->
### Timestamps in support file names are UTC

Whether the time in a bundle's file name is UTC or the machine's local time. Getting this wrong
shifts every arrival time by your time zone offset. Leave it as it is unless arrival times are
consistently out by a fixed number of hours.

<!--help:settings.maxage-->
### Only process files newer than ... days

The same setting as on the main window, kept here so it can be set alongside the others. 0 processes
everything.
