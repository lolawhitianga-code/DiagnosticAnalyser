#!/usr/bin/env python3
"""Static check: every {Binding Path} in the XAML must exist as a real property
on the compiled ViewModel/row types, and every {StaticResource} must be defined."""
import json
import re
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).parent
REPO = TOOLS.parent
APP = REPO / "src/DiagFileMonitor.App"

# Properties that come from WPF itself rather than our own types.
WPF_PROVIDED = {
    "Name", "ItemCount",          # CollectionViewGroup, in GroupItem templates
    "IsChecked", "Text", "SelectedItem", "Tag", "DataContext",
}


TARGETS = [
    TOOLS / "vmcheck/bin/Debug/net8.0-windows/vmcheck.dll",                       # App ViewModels + converters
    TOOLS / "vmcheck/bin/Debug/net8.0-windows/DiagFileMonitor.Core.dll",          # Core models bound by the grid
]


def ensure_dumpprops_built():
    """
    dumpprops is run with --no-build for speed, which means a clone that has never built it
    fails with 'No such file or directory' rather than anything that names the real problem.
    Build it once if it is not there.
    """
    project = TOOLS / "dumpprops/dumpprops.csproj"
    if (TOOLS / "dumpprops/bin/Debug/net8.0/dumpprops").exists():
        return

    print("building dumpprops (first run in this clone)...")
    out = subprocess.run(["dotnet", "build", str(project), "-v", "q", "--nologo"],
                         capture_output=True, text=True, cwd=TOOLS)
    if out.returncode != 0:
        print(f"FAILED to build dumpprops:\n{out.stdout}\n{out.stderr}")
        sys.exit(2)




def check_help_topics():
    """
    The ? button looks a control's topic up in docs/HELP.md by id. A typo in an id is a popup that
    silently says nothing, and a topic left behind after a control is renamed is help nobody can
    reach. Neither shows up at build time.
    """
    problems = []

    help_path = REPO / "docs" / "HELP.md"
    if not help_path.exists():
        return ["docs/HELP.md is missing - the ? button has nothing to read"]

    defined = set(re.findall(r"<!--\s*help:([a-z0-9.]+)\s*-->", help_path.read_text()))

    # Topics that are prose only - they explain something that is not a single control.
    prose_only = {
        "analysis.report",
        "main.grid",
        "common.greyedout",
    }

    used = set()
    for xaml_file in APP.rglob("*.xaml"):
        for m in re.finditer(r"help:Help\.Topic=\"([^\"]+)\"", xaml_file.read_text()):
            topic = m.group(1)
            used.add(topic)
            if topic not in defined:
                problems.append(
                    f"{xaml_file.name}: help topic '{topic}' is not in docs/HELP.md")

    # Every button, tick and menu item needs a topic. The ? works on greyed out controls too, so
    # "it is never clickable anyway" is not a reason to leave one out - a greyed out button is
    # exactly the one somebody wants explained.
    for xaml_file in sorted(APP.rglob("*.xaml")):
        text = xaml_file.read_text()

        for m in re.finditer(r"<(Button|CheckBox|MenuItem)\b", text):
            i, quote = m.end(), None
            while i < len(text):
                c = text[i]
                if quote:
                    if c == quote: quote = None
                elif c in "\"'":
                    quote = c
                elif c == ">":
                    break
                i += 1

            tag = text[m.start():i + 1]
            if "help:Help.Topic" in tag or "HelpModeButton" in tag:
                continue

            label = re.search(r"(?:Content|Header)=\"([^\"]*)\"", tag)
            problems.append(
                f"{xaml_file.name}: {m.group(1)} "
                f"'{label.group(1) if label else '?'}' has no help:Help.Topic")

    for topic in sorted(defined - used - prose_only):
        problems.append(
            f"docs/HELP.md: topic '{topic}' is not used by any control "
            f"(tag a control with it, or list it in prose_only)")

    return problems


def check_command_guards():
    """
    A [RelayCommand(CanExecute = ...)] whose command is never told to re-check is stuck in
    whatever state it had at startup. For anything guarded on a selected file that means the
    button is greyed out forever, and the only clue is a dead button - it compiles, it binds,
    and nothing throws. This caught exactly that on SendFeedbackCommand.
    """
    problems = []

    for source in sorted((APP / "ViewModels").rglob("*.cs")):
        text = source.read_text()

        guarded = re.findall(
            r"\[RelayCommand\([^\]]*CanExecute\s*=\s*nameof\([^)]+\)[^\]]*\)\]\s*"
            r"(?:private|public|internal|protected)[^(\n]*?(\w+)\s*\(",
            text)

        for method in guarded:
            command = method[:-5] + "Command" if method.endswith("Async") else method + "Command"
            if f"{command}.NotifyCanExecuteChanged()" not in text:
                problems.append(
                    f"{source.name}: {command} has a CanExecute guard but nothing ever calls "
                    f"{command}.NotifyCanExecuteChanged() - the button will not re-enable")

    return problems


# Types that exist in BOTH System.Windows (WPF) and System.Windows.Forms, which this project
# builds with together. Used unqualified, they do not compile - and WPF will not build on Linux,
# so nothing here catches it until somebody runs the Windows build.
#
# This list is the scar tissue from three separate broken builds: KeyEventArgs / Cursor / Color /
# Brush, then Cursor again, then DragEventArgs / DataFormats / DragDropEffects.
AMBIGUOUS_WPF_WINFORMS = [
    "DragEventArgs", "DragDropEffects", "DataFormats", "DataObject",
    "KeyEventArgs", "MouseEventArgs", "KeyboardDevice",
    "Cursor", "Cursors", "Color", "Colors", "Brush", "Brushes", "Pen",
    "Clipboard", "MessageBox", "Application", "Control", "Label", "Button",
    "TextBox", "CheckBox", "ComboBox", "ListBox", "MenuItem", "ContextMenu",
    "Orientation", "HorizontalAlignment", "VerticalAlignment", "Size", "Point",
    "FontFamily", "FontStyle", "FontWeight", "Image", "Padding", "Binding",
]


def check_ambiguous_types():
    """
    A type that exists in both WPF and WinForms, written without its namespace, in any file the
    App project compiles.

    This project sets UseWPF, UseWindowsForms and ImplicitUsings all together, so every file has
    both System.Windows and System.Windows.Forms in scope and a bare shared name does not compile.
    The Linux stand-in type-checks ViewModels only - code-behind needs the XAML-generated partial
    that only a Windows build produces - so an ambiguous type in a .xaml.cs is never seen here at
    all. Cheap text rule, three real broken builds behind it.

    Only actual type usage counts. "FontWeight = FontWeights.SemiBold" is assigning a property
    that happens to share the name, and it compiles perfectly well.
    """
    problems = []
    names = "|".join(AMBIGUOUS_WPF_WINFORMS)

    # Used as a type: declaring something of it, reading a static off it, or newing it up.
    declaration = re.compile(r"(?<![\w.])(" + names + r")\s+[A-Za-z_]\w*")
    static_use = re.compile(r"(?<![\w.])(" + names + r")\s*\.")
    constructed = re.compile(r"\bnew\s+(" + names + r")\s*[({]")

    for source in sorted(APP.rglob("*.cs")):
        if "obj" in source.parts or "bin" in source.parts:
            continue

        for number, line in enumerate(source.read_text().splitlines(), 1):
            stripped = line.strip()

            if stripped.startswith(("//", "///", "*", "using ")):
                continue

            # Strip string literals so a word in a message does not trip the rule.
            code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)

            hits = {m.group(1) for pattern in (declaration, static_use, constructed)
                    for m in pattern.finditer(code)}

            for name in sorted(hits):
                problems.append(
                    f"{source.name}:{number}: '{name}' exists in both System.Windows and "
                    f"System.Windows.Forms - write it out in full or the Windows build fails")

    return problems


def dump_properties():
    ensure_dumpprops_built()

    merged = {}
    for target in TARGETS:
        out = subprocess.run(
            ["dotnet", "run", "--project", str(TOOLS / "dumpprops/dumpprops.csproj"), "--no-build", "--",
             str(target),
             str(TOOLS / "vmcheck/bin/Debug/net8.0-windows"),
             "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.31/ref/net8.0",
             "/root/.nuget/packages/microsoft.windowsdesktop.app.ref/8.0.31/ref/net8.0"],
            capture_output=True, text=True, cwd=TOOLS)
        if out.returncode != 0:
            print(f"FAILED to dump properties from {target}:\n{out.stderr}")
            sys.exit(2)
        merged.update(json.loads(out.stdout))
    return merged


def binding_paths(xaml: str):
    """Yield (path, context) for every binding expression in the file."""
    # {Binding Foo}, {Binding Path=Foo}, {Binding Foo, Converter=...}
    for m in re.finditer(r"\{Binding\s+([^}]*)\}", xaml):
        body = m.group(1).strip()
        if not body:
            continue
        first = body.split(",")[0].strip()
        if first.startswith("Path="):
            first = first[len("Path="):].strip()
        elif "=" in first:
            continue  # e.g. {Binding ElementName=x, Path=y} handled below
        if first:
            yield first, m.group(0)
    # <Binding Path="Foo" /> inside MultiBinding
    for m in re.finditer(r"<Binding\s+Path=\"([^\"]+)\"", xaml):
        yield m.group(1), m.group(0)


def main():
    props = dump_properties()
    known = set(WPF_PROVIDED)
    for type_name, names in props.items():
        known.update(names)

    problems_at_start = check_ambiguous_types()
    problems = []
    checked = 0

    all_keys = set()
    for xaml_file in APP.rglob("*.xaml"):
        # A repeated x:Key inside one dictionary is not a build error - WPF throws when it
        # parses the file, so the app dies on startup with no warning beforehand.
        keys_here = re.findall(r"x:Key=\"([^\"]+)\"", xaml_file.read_text())
        seen = set()
        for key in keys_here:
            if key in seen:
                problems_at_start.append(f"{xaml_file.name}: x:Key '{key}' is defined twice")
            seen.add(key)
        all_keys.update(keys_here)

    problems.extend(problems_at_start)
    problems.extend(check_command_guards())
    problems.extend(check_help_topics())

    for xaml_file in sorted(APP.rglob("*.xaml")):
        text = xaml_file.read_text()

        # Malformed hex colours are a XAML parse error at runtime, not build time.
        for m in re.finditer(r"\"(#[0-9A-Za-z]+)\"", text):
            colour = m.group(1)[1:]
            if len(colour) not in (3, 4, 6, 8) or not re.fullmatch(r"[0-9A-Fa-f]+", colour):
                problems.append(f"{xaml_file.name}: malformed colour '#{colour}'")

        defined_keys = all_keys
        for m in re.finditer(r"\{StaticResource\s+([^}]+)\}", text):
            key = m.group(1).strip()
            # {StaticResource {x:Type Button}} is a style inheriting the default - not a named key.
            if key.startswith("{"):
                continue
            if key not in defined_keys:
                problems.append(f"{xaml_file.name}: undefined StaticResource '{key}'")

        for path, ctx in binding_paths(text):
            checked += 1
            segments = [s.strip() for s in re.split(r"[.\[]", path) if s.strip()]
            if not segments or segments[0].startswith("("):
                continue
            # A RelativeSource/ElementName binding starts from another object (e.g.
            # PlacementTarget.Tag.FooCommand), so only its final segment is ours to verify.
            redirected = "RelativeSource" in ctx or "ElementName" in ctx
            target = segments[-1] if redirected else segments[0]
            if target not in known:
                problems.append(f"{xaml_file.name}: binding '{path}' has no matching property '{target}' ({ctx})")

    print(f"checked {checked} bindings across {len(list(APP.rglob('*.xaml')))} xaml file(s)")
    if problems:
        print("\nPROBLEMS:")
        for p in problems:
            print("  - " + p)
        sys.exit(1)
    print("XAML binding check: PASS")


if __name__ == "__main__":
    main()
