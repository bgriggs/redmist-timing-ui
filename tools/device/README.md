# Device exercise harness

Drives the Android app on a phone plugged in over USB, by element rather than by
coordinate: Avalonia publishes a real automation tree to Android's accessibility bridge, so
`uiautomator` reports Avalonia's own control names, the text in `TextBlock`s, and
`AutomationProperties.Name` as `content-desc`.

Needs Python 3 (standard library only), `adb`, and an unlocked phone with the app
installed. Nothing here runs in CI — there is no device on the runner.

```bash
python tools/device/smoke.py     # ~2 min. Walks every screen, asserts nothing broke.
python tools/device/soak.py      # ~5 min. Expands car rows, watches memory grow.
```

Every run writes a JSON record under `results/<scenario>/` and prints a comparison against
`results/<scenario>/baseline*.json` (soak keeps one per `--mode`). That is the point of the harness: a single memory
number means nothing, a change against a stored one means something. Pass `--set-baseline`
to move the baseline, and only do that deliberately.

| File | |
| --- | --- |
| `devdrive.py` | the driver: find, tap, wait, scroll, memory, crash and low-memory-kill detection |
| `smoke.py` | walks every screen; asserts no screen shows a driver raw exception text |
| `soak.py` | expands cars repeatedly on a completed session, recording PSS at each step |
| `AGENTS.md` | how to run these and assess the output — **read this before using them** |

`AGENTS.md` is written for an AI agent asked to run these and report back, and it carries
the things that are easy to get wrong: which numbers mean what, when not to move the
baseline, what a passing soak does *not* prove, and the device quirks the driver works
around.
