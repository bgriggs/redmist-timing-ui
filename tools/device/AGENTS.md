# Device exercise harness — instructions for an AI agent

You have been asked to exercise the RedMist Android app on a **physical phone plugged into
this machine over USB** and report an assessment. This file tells you how, and what to do
with the results.

Read this whole file before running anything.

---

## What this is, and what it is not

`uiautomator` can read Avalonia's control tree on Android — real Avalonia type names,
the text inside `TextBlock`s, and `AutomationProperties.Name` as `content-desc`. So these
scenarios find controls by name and text, not by screen coordinates.

This is **not** the test suite. `RedMist.Timing.UI.Tests` runs headless on the desktop in
milliseconds and covers logic; run that with `dotnet test` and do not duplicate it here.
Reading one screen on the device costs about 3.5 seconds. This harness exists only for the
questions the headless tests cannot answer:

- Does it survive a cold start on real hardware, and how long does that take?
- What does memory do over a long session, and does the process survive it?
- Does any screen show a driver a raw .NET exception instead of something actionable?

It **cannot run in CI** — there is no phone on the runner. It is always a manual,
local, USB-attached run.

---

## Preflight

Check all of these before you run anything. If one fails, stop and tell the user rather
than working around it.

1. **A device is attached and authorized.** `adb devices` shows exactly one line ending in
   `device`. If it says `unauthorized`, the phone is showing a USB debugging prompt that a
   human has to accept — ask them; you cannot tap it, because `adb` is how you would tap
   and `adb` is what is blocked.
   - `adb` is found on `PATH`, via the `ADB` environment variable, or in the usual SDK
     locations. If more than one device is attached, set `ANDROID_SERIAL`.
2. **The phone is unlocked and awake.** `uiautomator` returns a null root against a lock
   screen, which fails in a way that looks like the app is broken. Both scenarios check
   this and refuse to start with exit code 2 — as they do for a missing device, a missing
   `adb`, and an app that is not installed.
3. **The app is installed:** package `com.bigmissionmotorsports.redmist`.
   **And the backend is reachable.** Both scenarios drive the live production API. If it is
   down, they fail on an empty archive and look exactly like an app regression. Check
   before reporting one.
4. **Know which build is on the phone.** The scenarios record `versionName` and
   `versionCode` automatically, but you must say in your report whether that is a Play
   build or a locally sideloaded one. Memory numbers from different builds are not
   comparable, and a locally built Release differs from a store build.
5. **Python 3.** No third-party packages; standard library only.
6. **Ask before installing, uninstalling, or reinstalling anything.** Uninstalling clears
   preferences and saved event access codes, which the user then has to re-enter.

The phone is someone's actual phone. Do not leave it in a broken state, and do not run a
long soak without saying how long it will take.

---

## Running

From the repo root:

```bash
python tools/device/smoke.py               # ~2 minutes, walks every screen
python tools/device/soak.py                # ~5 minutes, 6 expands, memory growth
python tools/device/soak.py --mode cycle   # expand and collapse, measures reclaim
python tools/device/soak.py --cycles 6 --event "Sebring" --session "14hr"
```

Exit codes: `0` pass, `1` fail, `2` could not start (locked phone, no device).

Both take `--set-baseline`. **Do not pass it unless the user explicitly asked you to move
the baseline.** See below.

Soak keeps a separate baseline per `--mode` (`baseline.hold.json`, `baseline.cycle.json`),
so recording one does not destroy the other. `--cycles`, `--event` and `--session` are *not*
part of that key — recording a baseline with different values for those replaces the
existing one for that mode, and the comparison will say "Ran differently" on every run
afterwards. The superseded file is kept as `previous-baseline.<mode>.json`.

`soak.py` options worth knowing:

| Option | Meaning |
| --- | --- |
| `--cycles N` | how many distinct cars to expand (default 6). Capped by the car rows present in one screen read — 18 on the reference phone, reported as `cycles_available` — because the list is gathered from a single dump and `ensure_visible` only scrolls downward. The run says so when it caps. Rows at the very bottom are partly covered by the bottom bar, so a high `--cycles` may fail on a swallowed tap; that now fails loudly rather than silently doing nothing. |
| `--mode hold` | expand successive cars without collapsing — measures accumulation (default) |
| `--mode cycle` | expand then collapse each car — measures whether anything is reclaimed |
| `--event TEXT` | substring of the completed event to open; default is the first one |
| `--session TEXT` | substring of the session to open; default is the first one |

Longer sessions stress it harder. Prefer a multi-hour enduro with hundreds of laps over a
qualifying session.

### Match the baseline's invocation

The stored baselines were recorded with these exact commands. **Run these when you want a
comparable number.** Anything else is a valid run but is measuring something different, and
the comparison will say so.

```bash
python tools/device/smoke.py --event "Hair of the Dawg" --session "Sat 6.5Hr"
python tools/device/soak.py  --event "Hair of the Dawg" --session "Sat 6.5Hr" --cycles 6
```

The event and session are pinned deliberately. Left on the defaults, both scenarios open
whatever is first in the archive — and that changes every time a race finishes, so the
counts and memory figures would drift for reasons having nothing to do with the app. If
that event ever disappears from the archive, pick another long one, record a new baseline,
and say in your report that the baseline was reset and why.

---

## Capturing results — required, every time

**Every run writes a JSON record automatically** to
`tools/device/results/<scenario>/<UTC timestamp>.json`. This happens even when the run
fails, because a failure is a data point about that build on that device.

Each record holds the device (model, Android version, RAM, screen size), the app
(`versionName`, `versionCode`), every step with its measurements, any faults from logcat,
and a summary block.

Your obligations:

1. **Never delete or hand-edit a result file.** They are the history. The only reason a
   run should not leave a record is that it never started.
2. **Always compare against the baseline.** Both scenarios print a `--- vs baseline ---`
   section automatically. Put it in your report. If it says no baseline is stored, say so —
   the first run on a new device or a new build has nothing to compare against, and that is
   information, not a problem.
3. **Report deltas, not just absolutes.** "peak PSS 503 MB" means little on its own.
   "peak PSS 503 MB, baseline 461 MB, +42 MB on the same device and build" is the finding.
4. **Only move the baseline when the user says to.** Pass `--set-baseline` when they ask,
   or when they have accepted a change as the new normal. Never move it to make a
   regression disappear. When you do move it, say in your report which run became the new
   baseline and what the previous one was.
5. **Commit the result files** when committing anything else from a session that ran them.
   Baseline tracking only works if the records are in the repo. They are small.
6. **A single run is not proof.** Memory on a real phone moves with whatever else is
   running on it. The comparison flags a number when it moves by more than 10% — or by more
   than 25 MB for a `_mb` key, whichever is *larger*, so on a 600 MB baseline the threshold
   is 60 MB, not 25. Timing keys get 30%, because a cold start swings far more between runs
   than memory does. If one number lands outside tolerance, run it again before reporting a
   regression, and report both runs.
7. **A short summary is not a clean one.** A run that failed part way through only measured
   part of what the baseline holds. The comparison ends with a `NOT MEASURED this run` line
   naming what it could not compare — quote it, or the truncated block reads as a complete
   comparison with nothing to report.

---

## Assessing what came back

**Hard failures — report as failures, no interpretation needed:**

- `status: fail` from either scenario.
- Anything in the `--- faults ---` section. These come from logcat and are grouped by
  buffer: `crash` is a managed or native crash, `events` is an ANR or a kill, `lmkd` is the
  low-memory killer.
- `died: true` in a soak summary — the process was killed mid-run.
- Smoke reporting raw exception text on a screen. Every screen is checked for text that
  reads like a stack trace; a hit means a driver is being shown a .NET message. The
  standing example is `The given key was not present in the dictionary`, which the driver
  settings screen used to show when the Keycloak configuration was blank.

**Memory, from a soak:**

- `net_growth_unknown_mb` is the number to look at first. `unknown` is where the managed
  heap lands, because the runtime is Mono. Growth concentrated there means the app is
  holding objects.
- **`net_*` and `peak_over_baseline_*` are not interchangeable.** The peak can only ever
  rise, so a change that makes memory come back leaves it untouched; only the `net_` figures
  can register an improvement. Report `net_` when judging a fix, `peak_` when judging how
  close the process came to being killed.
- Growth in `gfx_mb` or `native_mb` instead would point somewhere else — the chart, or
  Skia. Say which one grew; do not just report total PSS, and take each from the run's own
  baseline sample rather than from its first expand.
- `collapse_reclaimed_mb` (cycle mode only) is **weak evidence, not a verdict.** Each
  sample is a whole number of megabytes, so a four-cycle mean carries roughly half a
  megabyte of noise against 300–600 MB absolutes. The run asks the app to release memory
  (`am send-trim-memory`) and waits before sampling, but a managed runtime is free to
  ignore that. Read `collapse_deltas_mb` alongside the mean: values that disagree in sign
  are noise. A mean near zero is consistent with "nothing was reclaimed" but does not
  establish it — say so in those words rather than asserting the stronger claim.
- `mb_per_expand` is only produced in `hold` mode, for the same reason. With the phone's
  `total_ram_mb` you can estimate how many cars reach a kill; present it as an estimate,
  clearly labeled.

**What not to conclude:**

- A soak that finishes without dying does **not** mean there is no memory problem. It means
  that many cycles on that session did not cross the threshold on that phone.
- Do not call anything a fix unless a run demonstrates it. Record negative results —
  "changed X, memory unchanged" is worth as much as a success, and stops the same idea
  being tried again.

---

## What has already been established

Context for whoever is asked to continue the memory investigation. These came from this
harness on the Samsung SM-S135DL (2783 MB RAM), on a 155-lap session — a third the length
of the 487-lap event in the original crash report. Exact figures are in
`results/soak/baseline.hold.json`; the shape is what matters:

- **Expanding a car costs around 44 MB** and the cost is roughly linear. Six cars took the
  process from ~340 MB to ~605 MB, and repeated runs peaked within 5 MB of each other.
- **The growth is managed memory.** Of ~265 MB gained, ~239 MB was `unknown`. `native_mb`
  rose ~12 MB and `gfx_mb` ~2 MB across the whole run, which is why the chart is not the
  suspect — the non-virtualized lap list is.
- **Collapsing a car reclaims nothing measurable.** Measured with the memory-trim request
  (accepted on all four cycles) and a five second settle: `collapse_deltas_mb` came out
  `[-6, -2, 1, -4]`, mean −2.8 MB — three collapses of four measured *higher* than the
  expand before them. Individual values are only a few times the ±1 MB quantization floor,
  so this is consistent with nothing being released rather than proof of it, and is worth
  saying in those terms.
- **No process death was reached** at 600 MB on a 2783 MB phone in six cycles. The crash is
  a threshold, not a guaranteed outcome, which is why nobody could reproduce it on demand.

If you are testing a change meant to help, run the same pinned command before and after,
and report both. A change that does not move `net_growth_unknown_mb` did not help, and saying
so plainly is worth as much as a success.

## Report back like this

Say what you ran, on what, and what changed:

```
Ran smoke + soak (hold, 6 cycles) on Samsung SM-S135DL, Android 13, 2783 MB RAM,
versionName 10105 / versionCode 10105, installed 2026-08-30 (a locally sideloaded
Release build, not the Play one).

smoke: pass. First frame 10.6s (baseline 10.5s). All 10 screens clean, no raw
       exception text anywhere.
soak:  pass, no process death. Peak PSS 660 MB (baseline 600, +60 — outside tolerance).
       net_growth_unknown_mb 290 (baseline 235); gfx +2 MB, native +12 MB.
       Re-ran to confirm: second run peak 656 MB.

Results: tools/device/results/{smoke,soak}/<timestamps>.json. Baseline unchanged.
```

Then say what you think it means, and be explicit about what the run did **not** establish.

---

## Known quirks

These are all real, all hit during development, and all worked around in `devdrive.py`.
Do not "fix" them by removing the guards.

- **The bridge returns a null root during a cold start**, for several seconds. `poll()`,
  and everything built on it, counts a failed dump as "not yet". If you add a wait of your
  own, build it on `poll()` rather than calling `dump()` in a loop. (`ensure_visible()` is
  the one exception — it calls `dump()` directly, tolerating a single failure per scroll.)
- **`uiautomator` waits for window idle.** Only completed events have been exercised. A
  **live** event streams updates continuously and may never go idle, so a dump against one
  may fail. This is untested. If you need to drive a live event and dumps keep failing,
  report that rather than looping.
- **Back does not retrace the way in.** From a session's results it returns to the
  *completed events list*, skipping the event page that was used to reach it. From the
  completed list it then **leaves the app** — there is one activity, and that list is a
  root rather than a child of home, so the way back to the live list is the button at the
  bottom of the screen. `foreground()` checks for this and smoke calls it after every Back;
  without it, everything asserted afterwards would be against the launcher's UI.
- **Both list screens contain both list names.** Each shows its own name as the title and
  the other as the button that switches to it, so `contains="Live and Upcoming"` matches
  while sitting on the completed list. Tell them apart by where the text is (`wait_home()`
  in `devdrive.py` requires the title near the top) or by "Page 1", which only the completed
  list has.
- **Node count is not a signal that a car expanded.** The detail panel pushes about as many
  rows off the bottom as it adds, and once came back exactly unchanged. Assert on the
  panel's own content instead — smoke waits (with a timeout, not a fixed sleep) for its
  `Positions` tab.
- **Swipes can background the app.** A swipe that begins in the gesture navigation strip is
  taken as a home gesture. The process stays alive, so a liveness check sees nothing wrong
  while every later tap lands on the launcher. `scroll_down()` starts higher up to avoid it
  and soak calls `foreground()` around each cycle; keep both if you touch that code.
- **`launch()` does not clear logcat.** It returns a device timestamp instead, and
  `faults()` bounds its reads with `-T`. This runs on someone's own phone, and wiping their
  crash history to isolate a test is not a trade worth making. Note also that `-t` and `-s`
  cannot be combined — logcat applies the line cap before the tag filter, so the pair
  returns nothing at all.
- **`dump()` leaves `/sdcard/redmist-ui.xml` on the device.** Harmless, overwritten each
  read, but it is there.
- **Two different cold start numbers, and they measure different things.**
  `cold_start_ms` is the platform's own time to first frame, read from
  `ActivityTaskManager: Displayed`. `home_ready_s` is wall clock until the event list had
  content, so it includes the network fetch *and* several seconds of this harness's own
  screen reads. Quote `cold_start_ms` when talking about startup; do not present
  `home_ready_s` as the app's launch time.
- **The bottom bar is drawn over the end of the list.** The last row is clipped and its
  touches are swallowed by the bar. `list_rows()` drops clipped rows by comparing against
  the median row height. Do not select list rows by raw geometry without it.
- **Avalonia falls back to `ToString()`** where a control has no text and no
  `AutomationProperties.Name`, which is why tab headers read as
  `RedMist.Timing.UI.Views.ResultsView` and car rows as
  `...ViewModels.CarViewModel`. `texts()` filters those out. Adding
  `AutomationProperties.Name` in the XAML would improve both these selectors and screen
  reader support.
- **Each screen read costs ~3.5s.** A six-cycle soak takes around five minutes and smoke
  around two. Say so before starting one, and run it in the background rather than
  blocking.
