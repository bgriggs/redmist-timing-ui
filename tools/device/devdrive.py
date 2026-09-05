"""Drive the RedMist Android app on a USB-connected device, by element rather than by
coordinate.

Avalonia publishes a real automation tree to Android's accessibility bridge, so
``uiautomator dump`` comes back with Avalonia's own control type names, the text inside
TextBlocks, and AutomationProperties.Name as content-desc. That is what makes this worth
having: selectors are written against control names and visible text rather than screen
positions, so they survive a layout change or a different sized phone.

This is not a substitute for RedMist.Timing.UI.Tests. Those run headless on the desktop in
milliseconds and cover logic. Reading one screen here costs around three and a half
seconds and needs a phone on a cable, so it exists for the questions the headless tests
cannot answer: does it survive a cold start on real hardware, and what does memory do over
a long session.
"""
import json
import os
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

PKG = "com.bigmissionmotorsports.redmist"

# The two list screens no longer carry a title - the header was reduced to the logo - so
# what names them is the toggle at the bottom, and it names each one by the other. The
# button reading "Older Events" is therefore the live list offering to switch away, and the
# button reading "Latest Events" is the archive offering to switch back.
TO_COMPLETED = "Older Events"
TO_HOME = "Latest Events"
HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.join(HERE, "results")

# Nothing machine specific is hard coded. Everything below is discovered, or comes from
# the environment, so this runs on whatever machine has the phone plugged into it.
_ADB_CANDIDATES = [
    os.path.expandvars(r"%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe"),
    r"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe",
    r"C:\Program Files\Android\android-sdk\platform-tools\adb.exe",
    os.path.expanduser("~/Android/Sdk/platform-tools/adb"),
    os.path.expanduser("~/Library/Android/sdk/platform-tools/adb"),
]

_adb_path = None
_serial = None
_activity = None


def adb_path():
    global _adb_path
    if _adb_path is None:
        found = os.environ.get("ADB") or shutil.which("adb")
        if not found:
            for c in _ADB_CANDIDATES:
                if os.path.isfile(c):
                    found = c
                    break
        if not found:
            raise RuntimeError(
                "adb not found. Put it on PATH or set the ADB environment variable to it.")
        _adb_path = found
    return _adb_path


def serial():
    """The device to drive. ANDROID_SERIAL wins; otherwise there must be exactly one."""
    global _serial
    if _serial is None:
        _serial = os.environ.get("ANDROID_SERIAL")
        if not _serial:
            out = subprocess.run([adb_path(), "devices"], capture_output=True, text=True).stdout
            ready = [l.split()[0] for l in out.splitlines()[1:]
                     if l.strip() and l.split()[-1] == "device"]
            if len(ready) != 1:
                raise RuntimeError(
                    "expected exactly one authorized device, saw %r. Set ANDROID_SERIAL, and "
                    "check the phone for an unaccepted USB debugging prompt." % ready)
            _serial = ready[0]
    return _serial


def adb(*args, timeout=180):
    # UTF-8 explicitly. Without it Python decodes with the console's preferred encoding -
    # cp1252 on a default Windows install - while the automation tree arrives as UTF-8. A
    # name with an accent or a dash in it then turns to mojibake, errors="replace" makes
    # sure that raises nothing, and the XML still parses, so the only symptom is a selector
    # that never matches and garbled text saved into the results.
    return subprocess.run([adb_path(), "-s", serial(), *args], capture_output=True,
                          text=True, timeout=timeout, encoding="utf-8",
                          errors="replace")


def shell(cmd, timeout=180):
    return adb("shell", cmd, timeout=timeout).stdout


def activity():
    """Resolved rather than written down: the launcher class name is a hash of the
    namespace, so it changes if the Android head is ever renamed."""
    global _activity
    if _activity is None:
        out = shell("cmd package resolve-activity --brief %s" % PKG).strip().splitlines()
        hit = [l.strip() for l in out if l.strip().startswith(PKG + "/")]
        if not hit:
            raise RuntimeError("%s is not installed on %s" % (PKG, serial()))
        _activity = hit[-1]
    return _activity


class Node:
    """One entry from the automation tree."""

    __slots__ = ("cls", "text", "desc", "bounds", "clickable", "scrollable", "checked")

    def __init__(self, el):
        self.cls = el.get("class", "")
        self.text = el.get("text", "")
        self.desc = el.get("content-desc", "")
        self.clickable = el.get("clickable") == "true"
        self.scrollable = el.get("scrollable") == "true"
        # A car row is a ToggleButton, and this is the only per-row signal that its panel
        # is open. Counting panels does not work once several are expanded at once, and the
        # node total moves in both directions.
        self.checked = el.get("checked") == "true"
        m = re.match(r"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]", el.get("bounds", ""))
        self.bounds = tuple(int(g) for g in m.groups()) if m else (0, 0, 0, 0)

    @property
    def center(self):
        l, t, r, b = self.bounds
        return ((l + r) // 2, (t + b) // 2)

    @property
    def width(self):
        return self.bounds[2] - self.bounds[0]

    @property
    def height(self):
        return self.bounds[3] - self.bounds[1]

    def __repr__(self):
        return "<%s %r desc=%r %s>" % (self.cls, self.text, self.desc, list(self.bounds))


def dump(retries=6):
    """The automation tree as a flat list.

    Retried on purpose. The bridge answers with a null root while the app is still
    starting, and refuses outright while a window is animating, so one failed dump means
    nothing.
    """
    out = ""
    for _ in range(retries):
        out = shell("uiautomator dump /sdcard/redmist-ui.xml 2>&1")
        if "dumped to" in out:
            xml = adb("exec-out", "cat", "/sdcard/redmist-ui.xml").stdout
            try:
                root = ET.fromstring(xml)
            except ET.ParseError:
                time.sleep(1.0)
                continue
            return [Node(el) for el in root.iter("node")]
        time.sleep(1.5)
    raise RuntimeError("uiautomator would not dump: %s" % out.strip())


def find_all(nodes, text=None, cls=None, desc=None, contains=None, clickable=None, min_h=0):
    """Matching nodes, in tree order.

    ``min_h`` is not decoration. A scrolled list leaves its last row clipped to a few
    pixels; the text still matches, and tapping the middle of a seven pixel sliver lands
    on whatever is drawn underneath. Anything meant to be tapped should ask for a real
    height.
    """
    hits = []
    for n in nodes:
        if n.width <= 0 or n.height < max(1, min_h):
            continue
        if text is not None and n.text != text:
            continue
        if desc is not None and n.desc != desc:
            continue
        if cls is not None and n.cls != cls:
            continue
        if contains is not None and contains.lower() not in n.text.lower():
            continue
        if clickable is not None and n.clickable != clickable:
            continue
        hits.append(n)
    return hits


def find(nodes, **kw):
    hits = find_all(nodes, **kw)
    return hits[0] if hits else None


def poll(pick, timeout=30, describe="a match"):
    """Read the screen until ``pick(nodes)`` returns something truthy, then return
    ``(that, nodes)``.

    A dump that fails outright counts as "not yet" rather than as an error. Through a cold
    start the bridge returns a null root for several seconds, and that is exactly the
    window this is usually asked to wait through. Every wait in the harness goes through
    here so that tolerance is not something each caller has to remember.
    """
    deadline = time.time() + timeout
    nodes, last_error = [], None
    while True:
        try:
            nodes = dump(retries=2)
        except RuntimeError as ex:
            last_error = ex
        else:
            last_error = None
            got = pick(nodes)
            if got:
                return got, nodes
        if time.time() >= deadline:
            raise AssertionError("%s never appeared: %s" % (
                describe, "dump kept failing (%s)" % last_error if last_error
                else "on screen %s" % texts(nodes)[:10]))
        time.sleep(0.5)


def wait_for(timeout=30, **kw):
    """Poll until a node matches ``find``'s arguments. Returns (node, nodes), so a caller
    can read the rest of the screen without paying for another dump."""
    return poll(lambda ns: find(ns, **kw), timeout=timeout, describe=str(kw))


def clickable_for(nodes, target):
    """The smallest clickable node containing the target's center.

    Avalonia's TextBlocks are not clickable themselves; the Button or ToggleButton around
    them is. Tapping the text usually works by accident, but resolving the real control
    puts the tap in the middle of the hit area rather than at the edge of a label.
    """
    cx, cy = target.center
    best = None
    for n in nodes:
        if not n.clickable or n.width <= 0 or n.height <= 0:
            continue
        l, t, r, b = n.bounds
        if l <= cx <= r and t <= cy <= b:
            area = n.width * n.height
            if best is None or area < best[0]:
                best = (area, n)
    if best is None:
        # Raised rather than falling back to the target. Tapping a label that has no
        # control behind it is a no-op, and a no-op that reports success is the worst
        # outcome available here: a scenario built on it keeps running, measures a screen
        # nothing happened on, and passes.
        raise AssertionError("nothing clickable around %r" % (target,))
    return best[1]


def tap(node):
    x, y = node.center
    shell("input tap %d %d" % (x, y))


def tap_match(timeout=30, settle=2.0, **kw):
    """Wait for a node matching ``find``'s arguments, then tap the control around it."""
    target, nodes = wait_for(timeout=timeout, **kw)
    hit = clickable_for(nodes, target)
    tap(hit)
    time.sleep(settle)
    return hit


def tap_text(contains, timeout=30, settle=2.0, min_h=0):
    """Find a control by its visible text and tap it. Returns the node tapped."""
    return tap_match(timeout=timeout, settle=settle, contains=contains, min_h=min_h)


def swipe(x1, y1, x2, y2, ms=350):
    shell("input swipe %d %d %d %d %d" % (x1, y1, x2, y2, ms))
    time.sleep(1.0)


def scroll_down(nodes, amount=0.5):
    """Scroll the tallest scrollable region down by a fraction of its height."""
    region = max((n for n in nodes if n.scrollable), key=lambda n: n.height, default=None)
    l, t, r, b = region.bounds if region else (0, 0, max(
        (n.width for n in nodes), default=720), max((n.bounds[3] for n in nodes), default=1400))
    x = (l + r) // 2
    span = int((b - t) * amount)
    # Start a quarter of the way up rather than at the very bottom. A swipe that begins in
    # the gesture navigation strip is taken by the system as a home gesture, which sends
    # the app to the background - and since the process is still alive, nothing here would
    # notice; the rest of the run would tap on the launcher.
    start = b - int((b - t) * 0.25)
    swipe(x, start, x, max(t + 10, start - span))


def ensure_visible(timeout=60, max_scrolls=8, **kw):
    """Scroll down until a match is on screen. Returns (node, nodes).

    Down only, so a caller has to work through a list in the order it is drawn. Anything
    that scrolls past a target puts it permanently out of reach and the failure reads as a
    missing control rather than as a harness that cannot go back up.
    """
    deadline = time.time() + timeout
    nodes = dump()
    for _ in range(max_scrolls + 1):
        hit = find(nodes, **kw)
        if hit is not None:
            return hit, nodes
        if time.time() >= deadline:
            break
        scroll_down(nodes)
        try:
            nodes = dump()
        except RuntimeError:
            time.sleep(1.5)
            nodes = dump()
    raise AssertionError("not found after scrolling: %s" % kw)


def list_rows(nodes, min_w_frac=0.85, min_h=40):
    """The repeated full width rows of a list - event rows, session rows, car rows.

    Rows clipped by the edge of the scroll area are dropped, by taking the median row
    height and keeping only rows close to it. That is not tidiness. The bottom bar is
    drawn over the end of the list, so the last row is both short and covered: its text
    still matches a search, its center still sits inside its own bounds, and tapping it
    silently activates nothing because the bar above it takes the touch.
    """
    if not nodes:
        return []
    screen_w = max(n.width for n in nodes)
    rows = [n for n in nodes if n.clickable and n.width >= screen_w * min_w_frac
            and n.height >= min_h]
    if len(rows) < 3:
        return rows
    heights = sorted(n.height for n in rows)
    median = heights[len(heights) // 2]
    return [n for n in rows if n.height >= median * 0.9]


def back(settle=1.5):
    shell("input keyevent KEYCODE_BACK")
    time.sleep(settle)


def texts(nodes):
    """Screen text worth reading. Drops Avalonia's ToString fallbacks, which show up
    wherever a control has no AutomationProperties.Name and no text of its own."""
    out = []
    for n in nodes:
        t = n.text
        if not t or n.width <= 0 or n.height <= 0:
            continue
        if t.startswith("Avalonia.") or t.startswith("RedMist.Timing.UI."):
            continue
        out.append(t)
    return out


def raw_strings(nodes):
    """Every visible string, unfiltered - both text and content-desc.

    ``texts()`` drops Avalonia's ToString fallbacks, and those begin with the same
    namespace prefixes an exception message does, so scanning ``texts()`` for exception
    text means the filter that removes the noise also removes the evidence. Content-desc is
    included because a message surfaced through an accessible name is just as visible to a
    driver as one in a label.
    """
    out = []
    for n in nodes:
        if n.width <= 0 or n.height <= 0:
            continue
        out += [v for v in (n.text, n.desc) if v]
    return out


def wait_home(timeout=60):
    """Wait for the home screen, told apart from the archive by what it offers to show.

    This used to look for the screen's own title near the top of the tree, since both lists
    carried both names and only their position separated them. The header renders no title
    now, so the toggle is the only thing naming either screen - and it names the one you are
    not on, which makes it unambiguous: the live list offers "Older Events" and the archive
    offers "Latest Events", never both.

    That title is commented out in EventsListView.axaml rather than deleted, and PageTitle
    still returns these same two strings the other way round. Re-binding it would put
    "Older Events" on the archive as its title and this would accept the archive as home -
    passing rather than failing, which is the worse direction. Change this if that comes
    back.

    Do not be tempted to also require the absence of a pager as a second opinion. The
    scroll bar contributes nodes whose text - not content-desc, so `contains` does match
    them - is "Page down", "Line up", "Line down" and "Position". A check for "Page "
    therefore matches every scrolling screen, which is this one, and rejects the home
    screen forever. "Page 1" is safe only because of the digit.
    """
    def pick(ns):
        return find(ns, contains=TO_COMPLETED) is not None or None

    _, nodes = poll(pick, timeout=timeout, describe="the home screen")
    return nodes


# Anything that reads like a .NET exception rather than something a driver could act on.
# The literal at the end is the string that used to greet drivers on the settings screen
# when the Keycloak configuration was blank.
#
# Deliberately narrow. This is matched against raw_strings(), which keeps Avalonia's
# ToString fallbacks, and those are full of type names - a bare "System\." or a bare
# "exception" would fire on something like AvaloniaList`1[System.Object] and report a
# driver-facing crash message that does not exist. Only exception-shaped evidence counts:
# a type name ending in Exception, a stack frame, or one of the known messages.
_RAW_EXCEPTION = re.compile(
    r"\w+Exception\b"                    # NullReferenceException, KeyNotFoundException
    r"|\bat [A-Z]\w*(?:\.\w+)+\("        # a stack frame
    r"|given key was not present"
    r"|object reference not set"
    r"|one or more errors occurred", re.I)


def looks_like_raw_exception(text):
    return bool(_RAW_EXCEPTION.search(text or ""))


def meminfo():
    """Megabytes, from dumpsys. ``unknown_mb`` is the interesting one for this app: the
    managed heap lands there rather than under Java Heap, because the runtime is Mono."""
    out = shell("dumpsys meminfo %s" % PKG)
    got = {}
    m = re.search(r"TOTAL PSS:\s*(\d+)\s+TOTAL RSS:\s*(\d+)", out)
    if m:
        got["pss_mb"] = int(m.group(1)) // 1024
        got["rss_mb"] = int(m.group(2)) // 1024
    for label, key in (("Java Heap", "java_mb"), ("Native Heap", "native_mb"),
                       ("Graphics", "gfx_mb"), ("Unknown", "unknown_mb")):
        m = re.search(r"^\s*%s:\s*(\d+)" % re.escape(label), out, re.M)
        if m:
            got[key] = int(m.group(1)) // 1024
    return got


def trim_memory(level="RUNNING_CRITICAL"):
    """Ask the app to release what it can, the way the platform would under pressure.

    Without this, measuring memory a few seconds after a collapse says nothing: a managed
    runtime has no reason to have collected yet, so the reading is flat whether or not
    anything became collectable. That is the difference between "nothing was released" and
    "nothing was asked for", and only the first is a finding.

    It is a request, not a guarantee - the runtime decides. Treat a flat reading after this
    as suggestive, not conclusive.
    """
    p = pid()
    if not p:
        return False
    r = adb("shell", "am send-trim-memory %s %s" % (p, level))
    out = (r.stdout or "") + (r.stderr or "")
    # Checked, because a silently rejected request looks exactly like a runtime that
    # declined to collect - and only one of those two says anything about the app.
    return r.returncode == 0 and "Error" not in out and "Unknown" not in out


def pid():
    out = shell("pidof %s" % PKG).strip()
    return out.split()[0] if out else None


def awake():
    """Screen on and unlocked.

    Both halves look for a positive signal rather than for the absence of a negative one.
    A locked phone hands uiautomator a null root, every wait then times out, and the run
    ends saying a screen never appeared - which reads as the app failing to render. Any
    check that concludes "unlocked" because a field was missing, or because dumpsys
    returned nothing at all, produces exactly that misdiagnosis.
    """
    if "mWakefulness=Awake" not in shell("dumpsys power"):
        shell("input keyevent KEYCODE_WAKEUP")
        time.sleep(1)
        if "mWakefulness=Awake" not in shell("dumpsys power"):
            return False
    win = shell("dumpsys window")
    if "mCurrentFocus=" not in win:
        return False
    focused = (re.search(r"mCurrentFocus=(.*)", win) or [""])[0]
    return ("mDreamingLockscreen=true" not in win
            and "Keyguard" not in focused and "NotificationShade" not in focused)


def foreground(retries=3):
    """True while our app owns the focused window.

    Worth checking after every Back, and after anything that swipes. The app has one
    activity, so a Back from its first screen leaves it entirely and a swipe can be taken
    as a home gesture; either way a scenario that keeps navigating afterwards drives
    whatever the launcher put in front, and reports its failures against someone else's UI.

    Retried because nothing holds focus midway through a window transition, and a single
    read taken in that gap says the app is gone when it is not.
    """
    for _ in range(retries):
        m = re.search(r"mCurrentFocus=(\S+.*)", shell("dumpsys window"))
        focused = m.group(1).strip() if m else ""
        if PKG in focused:
            return True
        if focused and "null" not in focused:
            return False
        time.sleep(1.0)
    return False


def launch(cold=True, settle=1.0):
    """Start the app, and return a logcat timestamp taken just before it started.

    Deliberately does not clear the log buffers. This runs on someone's own phone, and
    wiping crash and event history there destroys unrelated evidence that is not ours to
    throw away. Bounding reads by timestamp gets the same isolation without the collateral.
    """
    if cold:
        shell("am force-stop %s" % PKG)
        time.sleep(settle)
    # Timestamped after the force-stop, not before. ActivityManager logs a force-stop as
    # am_kill against our own package, so a window opened any earlier hands the fault
    # detector this harness's own teardown and fails the run on it.
    since = log_since()
    shell("am start -n %s" % activity())
    return since


def displayed_ms(timeout=60):
    """Milliseconds from launch to first frame, as the platform measures it.

    Timing this from the harness instead would mostly measure the harness: a screen read
    costs seconds, and through a cold start the first few fail and are retried, so a
    wall-clock number here comes out three times too large. The platform logs the real one.
    """
    pat = re.compile(r"Displayed %s/\S+: \+(?:(\d+)m)?(?:(\d+)s)?(\d+)ms" % re.escape(PKG))
    deadline = time.time() + timeout
    while time.time() < deadline:
        for line in shell("logcat -b main -d -s ActivityTaskManager:I").splitlines():
            m = pat.search(line)
            if m:
                mins, secs, ms = (int(g or 0) for g in m.groups())
                return mins * 60000 + secs * 1000 + ms
        time.sleep(0.5)
    return None


def log_since():
    """A logcat timestamp for 'now', read from the device's own clock."""
    return shell("date +'%m-%d %H:%M:%S.000'").strip()


def faults(since=None):
    """Everything that would end the process: a managed or native crash, an ANR, or a low
    memory kill.

    The last one matters most here. An LMK death produces no Sentry event and no Java
    stack - the app simply vanishes and the launcher comes back, which is exactly how the
    unreproducible crash on a long session was described.

    Bounded by timestamp, not by a line count. ``-t`` and ``-s`` cannot be combined:
    logcat applies the line cap first and the tag filter second, so the pair returns
    nothing at all, and the low-memory detector written that way could never have fired. A
    fixed line count is wrong here anyway, since evidence from early in a long soak scrolls
    out of a window that size.
    """
    window = "-T '%s'" % since if since else ""
    found = []

    crash = shell("logcat -b crash -d %s" % window)
    if PKG in crash:
        # Taken as a block, and only once the buffer is known to hold our crash. The FATAL
        # EXCEPTION header line carries no package name, so matching on it alone would
        # attribute any other app's crash to this run and fail it.
        found += [{"buffer": "crash", "line": l.strip()}
                  for l in crash.splitlines() if l.strip()]

    # Belt and braces alongside the timestamp above: any deliberate stop this harness
    # issues is logged as am_kill with ActivityManager's force-stop reason, and matched
    # narrowly enough here that a real kill - which carries a different reason - still gets
    # through.
    force_stopped = "stop %s due to from pid" % PKG
    for line in shell("logcat -b events -d %s" % window).splitlines():
        if (PKG in line and re.search(r"am_(crash|anr|kill|proc_died)", line)
                and force_stopped not in line):
            found.append({"buffer": "events", "line": line.strip()})

    for line in shell("logcat -b main -d %s -s lmkd:V lowmemorykiller:V"
                      % window).splitlines():
        if PKG in line:
            found.append({"buffer": "lmkd", "line": line.strip()})
    return found


def device_info():
    props = {}
    for key, prop in (("model", "ro.product.model"), ("android", "ro.build.version.release"),
                      ("manufacturer", "ro.product.manufacturer")):
        props[key] = shell("getprop %s" % prop).strip()
    m = re.search(r"MemTotal:\s*(\d+)", shell("cat /proc/meminfo"))
    props["total_ram_mb"] = int(m.group(1)) // 1024 if m else None
    # Not the serial. These records are committed to a public repository, and the hardware
    # serial identifies someone's personal phone while adding nothing a comparison needs -
    # model, memory and screen are what make two runs comparable.
    props["size"] = shell("wm size").strip().replace("Physical size: ", "")
    props["density"] = shell("wm density").strip().replace("Physical density: ", "")
    return props


def app_info():
    out = shell("dumpsys package %s" % PKG)
    m = re.search(r"versionCode=(\d+)", out)
    n = re.search(r"versionName=(\S+)", out)
    # The install time is here because the version is not enough to tell two builds apart.
    # Local builds carry the same versionName all day, so a before-and-after comparison of
    # a code change would otherwise report the two runs as the same build.
    u = re.search(r"lastUpdateTime=(\S+ \S+)", out)
    return {"package": PKG,
            "version_code": int(m.group(1)) if m else None,
            "version_name": n.group(1) if n else None,
            "installed_at": u.group(1) if u else None}


class Run:
    """One scenario run, and the JSON it leaves behind.

    Every run is written out, including a failed one, because a failure is a data point
    about the build and the device it ran on. Comparing against the stored baseline is how
    a slow regression gets noticed at all - one number in isolation says nothing.
    """

    def __init__(self, scenario, notes=None):
        self.scenario = scenario
        self.started = time.time()
        self.since = log_since()
        self.data = {
            "scenario": scenario,
            "started_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "device": device_info(),
            "app": app_info(),
            "notes": notes or {},
            "steps": [],
            "faults": [],
            "summary": {},
            "status": "running",
        }

    def step(self, name, **fields):
        entry = {"name": name, "at_s": round(time.time() - self.started, 1)}
        entry.update(fields)
        self.data["steps"].append(entry)
        return entry

    @property
    def baseline_name(self):
        """One baseline per mode, not one per scenario.

        A soak in ``cycle`` mode measures something different from one in ``hold`` mode, so
        a single shared file means recording one baseline silently destroys the other and
        every later run reports a mismatch it cannot fix without re-recording.
        """
        mode = self.data["notes"].get("mode")
        return "baseline.%s.json" % mode if mode else "baseline.json"

    def finish(self, status, **summary):
        try:
            self.data["faults"] = faults(self.since)
        except Exception as ex:
            # Collecting evidence must not destroy the record of the run that produced it.
            # This is reached when the device went away mid-run, which is exactly when the
            # saved result matters most.
            self.data["faults_unavailable"] = repr(ex)
        if self.data["faults"] and status == "pass":
            status = "fail"
        self.data["status"] = status
        self.data["duration_s"] = round(time.time() - self.started, 1)
        self.data["summary"].update(summary)
        return self.data

    def save(self, set_baseline=False):
        folder = os.path.join(RESULTS, self.scenario)
        os.makedirs(folder, exist_ok=True)
        stamp = re.sub(r"[:\-]", "", self.data["started_utc"]).replace("+0000", "Z")
        path = os.path.join(folder, "%s.json" % stamp)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(self.data, f, indent=2)
        if set_baseline:
            target = os.path.join(folder, self.baseline_name)
            if os.path.isfile(target):
                # Keep the one being replaced. A baseline stored as a plain copy is
                # otherwise gone the moment it is superseded, and "what was it before" -
                # the first question asked after a baseline moves - becomes unanswerable.
                shutil.copyfile(target, os.path.join(folder, "previous-" + self.baseline_name))
            with open(target, "w", encoding="utf-8") as f:
                json.dump(self.data, f, indent=2)
        return path


def baseline(scenario, mode=None):
    name = "baseline.%s.json" % mode if mode else "baseline.json"
    path = os.path.join(RESULTS, scenario, name)
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as f:
        return json.load(f)


# How far a number may move before it is worth a second look. One rule for everything does
# not work: memory on an idle phone is fairly steady, while a cold start swings by seconds
# between runs, so a shared threshold would flag every single run and train whoever reads
# the report to skip the line.
_TOLERANCE_PCT = {"cold_start_ms": 30, "home_ready_s": 30}


def _outside(key, was, delta):
    floor = 25 if key.endswith("_mb") else 0
    return abs(delta) > max(floor, abs(was) * _TOLERANCE_PCT.get(key, 10) / 100.0)


def compare(current, base):
    """Lines describing how this run differs from the baseline.

    The tolerances are deliberately loose. Memory on a real phone moves with whatever else
    is running on it, so one run over the line is a reason to run it again, not a verdict.
    """
    if not base:
        return ["No baseline stored for %s - nothing to compare against." % current["scenario"]]

    lines = []
    b_app, c_app = base.get("app", {}), current.get("app", {})
    if b_app.get("version_name") != c_app.get("version_name"):
        lines.append("Build differs: baseline %s, this run %s." %
                     (b_app.get("version_name"), c_app.get("version_name")))
    elif b_app.get("installed_at") != c_app.get("installed_at"):
        # The version alone does not separate two local builds - they carry the same
        # versionName all day - so a before-and-after comparison of a code change would
        # otherwise say nothing about the fact that the build changed at all.
        lines.append("Same version, different install: baseline installed %s, this run %s."
                     % (b_app.get("installed_at"), c_app.get("installed_at")))
    if base.get("device", {}).get("model") != current.get("device", {}).get("model"):
        lines.append("Different device: baseline %s, this run %s. Memory numbers are not "
                     "comparable across models." % (base.get("device", {}).get("model"),
                                                    current.get("device", {}).get("model")))
    if base.get("status") != current.get("status"):
        lines.append("Status changed: baseline %s, this run %s." %
                     (base.get("status"), current.get("status")))

    # Different arguments mean the two runs did different amounts of work, so the numbers
    # below are not measuring the same thing. Said first and plainly, because a summary
    # table that lines up neatly is easy to read as a comparison when it is not one.
    for key in sorted(set(base.get("notes", {})) | set(current.get("notes", {}))):
        was, now = base.get("notes", {}).get(key), current.get("notes", {}).get(key)
        if was != now:
            lines.append("Ran differently - %s: baseline %r, this run %r. The measurements "
                         "below are not comparable." % (key, was, now))

    for key, value in sorted(current.get("summary", {}).items()):
        was = base.get("summary", {}).get(key)
        if was is None or isinstance(value, bool) or not isinstance(value, (int, float)):
            if was != value:
                lines.append("%s: baseline %r, this run %r" % (key, was, value))
            continue
        delta = value - was
        lines.append("%s: %s -> %s (%+g)%s" % (
            key, was, value, delta,
            "   <-- outside tolerance" if _outside(key, was, delta) else ""))

    # Named explicitly rather than left out. A run that failed part way through produces a
    # short summary, and a comparison that simply omits what it never measured looks like a
    # complete one with nothing to report.
    missing = sorted(set(base.get("summary", {})) - set(current.get("summary", {})))
    if missing:
        lines.append("NOT MEASURED this run, so not compared: %s" % ", ".join(missing))
    return lines
