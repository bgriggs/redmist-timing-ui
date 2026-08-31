"""Expand car rows on a completed session, over and over, watching memory.

Run:  python tools/device/soak.py [--cycles N] [--mode hold|cycle] [--event TEXT]
                                  [--session TEXT] [--set-baseline]

Why this exists: a crash report of the shape "expanded a car on a completed event, it said
loading, then it dropped back to the home screen" that nobody could reproduce on demand.
That is what running out of memory looks like from the outside - the process is killed, so
there is no exception and no Sentry event, just the launcher coming back.

A threshold like that cannot be found by hand. This drives the same path repeatedly and
records PSS at every step, so the shape of the growth is visible even when no single run
crosses the line.

Two modes:
  hold  (default)  expand successive cars without collapsing - measures accumulation.
  cycle            expand and collapse the same car - measures whether anything is given
                   back, which is the more damning question.

Reading the numbers: ``unknown_mb`` is where the managed heap lands, because the runtime is
Mono. Growth concentrated there is the app holding objects. Growth in ``gfx_mb`` or
``native_mb`` would point somewhere else entirely, such as the chart.
"""
import argparse
import re
import sys
import time

import devdrive as d

# Device text can carry characters a Windows console will not encode, and losing the run to
# a UnicodeEncodeError while printing a failure would throw away the record of it.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def check_foreground(where):
    assert d.foreground(), (
        "the app is no longer in front (%s). A swipe that starts too low is taken as a home "
        "gesture; the process stays alive, so nothing else here would notice and every "
        "remaining tap would land on the launcher." % where)


def car_row(nodes, number):
    """The ToggleButton for one car number, or None if it is not on screen."""
    label = d.find(nodes, text=number)
    return d.clickable_for(nodes, label) if label else None


def assert_toggled(number, expected, action):
    """Check the row actually changed state, rather than assuming the tap landed.

    Without this the whole scenario can pass while doing nothing. A tap that misses its
    control is silent, memory then stays flat because nothing was ever expanded, and the
    run reports no growth - which is indistinguishable from the memory problem being
    fixed, and is precisely the conclusion the summary invites.
    """
    _, nodes = d.ensure_visible(text=number)
    row = car_row(nodes, number)
    assert row is not None, "%s vanished from the list after %s" % (number, action)
    assert row.checked == expected, (
        "%s of %s did not take: the row reads checked=%s, expected %s"
        % (action, number, row.checked, expected))


def summarize(run, samples, args, died_at):
    """The run's numbers, from whatever was collected before it ended."""
    pss = [s.get("pss_mb") for s in samples if s.get("pss_mb") is not None]
    unknown = [s.get("unknown_mb") for s in samples if s.get("unknown_mb") is not None]
    expands = [s for s in run.data["steps"] if s["name"] == "expand"]
    collapses = [s for s in run.data["steps"] if s["name"] == "collapse"]

    out = {"cycles_completed": len(expands), "died": died_at is not None}
    if not pss:
        return out

    out.update({
        "peak_pss_mb": max(pss),
        "final_pss_mb": pss[-1],
        # Both the high-water mark and where it ended, because they answer different
        # questions and only the second can ever fall. A change that makes memory come back
        # leaves the peak during an expand exactly where it was, so a report reading only
        # the peak would call a genuine improvement no change at all.
        "peak_over_baseline_pss_mb": max(pss) - pss[0],
        "net_growth_pss_mb": pss[-1] - pss[0],
    })
    if unknown:
        out["peak_over_baseline_unknown_mb"] = max(unknown) - unknown[0]
        out["net_growth_unknown_mb"] = unknown[-1] - unknown[0]
    if expands and args.mode == "hold":
        # Only meaningful when nothing is collapsed in between. Under cycle the peak is
        # roughly the cost of one expand no matter how many cycles ran, so dividing by the
        # count would understate it by that factor - and this is the figure used to
        # estimate how many cars it takes to reach a kill.
        out["mb_per_expand"] = round((max(pss) - pss[0]) / len(expands), 1)
    if collapses:
        deltas = [e.get("pss_mb", 0) - c.get("pss_mb", 0)
                  for e, c in zip(expands, collapses)
                  if e.get("pss_mb") is not None and c.get("pss_mb") is not None]
        if deltas:
            # Each sample is a whole number of megabytes, so one delta carries about a
            # megabyte of noise. The individual values are kept alongside the mean: a mean
            # near zero built from values that disagree in sign is measurement noise, and
            # reporting it as "nothing was reclaimed" would be reading a result into it.
            out["collapse_reclaimed_mb"] = round(sum(deltas) / len(deltas), 1)
            out["collapse_deltas_mb"] = deltas
    return out


def open_session(run, event_text, session_text):
    """Navigate to a completed session's results and return its screen."""
    run.since = d.launch(cold=True)
    d.wait_home()
    d.tap_text("Completed Events")
    _, nodes = d.wait_for(contains="Page 1")

    if event_text:
        d.tap_text(event_text)
    else:
        rows = d.list_rows(nodes)
        assert rows, "no completed events to open"
        d.tap(rows[0])
    _, nodes = d.wait_for(contains="Provisional Results for")
    event_name = next((t for t in d.texts(nodes) if t.startswith("Provisional Results for")),
                      "?").replace("Provisional Results for ", "")

    if session_text:
        d.tap_text(session_text)
    else:
        sessions = d.list_rows(nodes)
        assert sessions, "event has no sessions"
        d.tap(sessions[0])
    _, nodes = d.wait_for(timeout=60, contains="Laps:")

    laps = next((int(m.group(1)) for m in
                 (re.match(r"Laps:\s*(\d+)", t) for t in d.texts(nodes)) if m), None)
    run.step("opened", event=event_name, laps=laps)
    print("event %r, session laps %s" % (event_name, laps))
    return nodes, event_name, laps


def car_numbers(nodes):
    """Every car number on the results screen, in order.

    Used as stable handles: after one row expands, everything below it moves, so an index
    into the row list means nothing on the next pass while "#197" still finds the same car.
    """
    out = []
    for n in d.find_all(nodes, min_h=10):
        if re.fullmatch(r"#\S+", n.text) and n.text not in out:
            out.append(n.text)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cycles", type=int, default=6, help="how many cars to expand")
    ap.add_argument("--mode", choices=("hold", "cycle"), default="hold")
    ap.add_argument("--event", help="substring of the completed event to open")
    ap.add_argument("--session", help="substring of the session to open")
    ap.add_argument("--set-baseline", action="store_true")
    args = ap.parse_args()

    try:
        if not d.awake():
            print("CANNOT START: the phone is locked or asleep. Unlock it and run again.")
            return 2
        # Resolving the launcher activity here rather than at first use: it is what
        # raises when the app is not installed, and this is the block that turns that
        # into exit 2 instead of a traceback indistinguishable from a test failure.
        d.activity()
        run = d.Run("soak", notes={"mode": args.mode, "cycles_requested": args.cycles,
                                   "event": args.event, "session": args.session})
    except (RuntimeError, OSError) as ex:
        # No device, adb not on the machine, app not installed. Exit 2 keeps all of that
        # separate from exit 1, which has to mean the app did something wrong.
        print("CANNOT START: %s" % ex)
        return 2
    print("device: %(manufacturer)s %(model)s, Android %(android)s, %(total_ram_mb)s MB RAM"
          % run.data["device"])
    print("app:    %(version_name)s (code %(version_code)s)" % run.data["app"])

    summary = {"mode": args.mode}
    samples = []
    died_at = None
    status = "fail"
    try:
        nodes, event_name, laps = open_session(run, args.event, args.session)
        summary["session_laps"] = laps

        # Every visible car first, then sliced - so cycles_available reports the real
        # ceiling rather than whatever was asked for.
        visible = car_numbers(nodes)
        assert visible, "no car numbers found on the results screen"
        summary["cycles_available"] = len(visible)
        cars = visible[:args.cycles]
        if args.mode == "hold":
            # Bottom up, and not for tidiness. Expanding a row pushes everything below it
            # down and leaves everything above it exactly where it was, so working upward
            # from the last visible car means none of the remaining targets ever move.
            # Top down needs scrolling instead, and an expanded panel on a 155 lap session
            # is some ten thousand pixels tall - far enough that the search for the next
            # car gives up before reaching it.
            cars = list(reversed(cars))
        print("cars, in order: %s" % ", ".join(cars))
        if len(cars) < args.cycles:
            # The car list is read from a single screen, so what is visible is the ceiling.
            # Said out loud because a run that quietly does four cycles when six were asked
            # for is a smaller test than the one being reported.
            print("only %d cars fit on screen, so --cycles %d cannot be reached"
                  % (len(visible), args.cycles))
            run.step("cycles-capped", requested=args.cycles, available=len(visible))

        start_pid = d.pid()
        base = d.meminfo()
        assert base.get("pss_mb") is not None, (
            "dumpsys meminfo did not parse - every memory figure below would be missing "
            "and the run would otherwise still report pass")
        samples.append(base)
        summary["baseline_pss_mb"] = base.get("pss_mb")
        run.step("baseline", **base)
        print("baseline pss=%sMB unknown=%sMB" % (base.get("pss_mb"), base.get("unknown_mb")))

        for i, number in enumerate(cars):
            hit, nodes = d.ensure_visible(text=number)
            check_foreground("looking for %s" % number)
            row = d.clickable_for(nodes, hit)
            before = len(nodes)
            d.tap(row)
            time.sleep(3)

            if d.pid() != start_pid:
                died_at = i
                run.step("process-died", cycle=i, car=number)
                print("PROCESS DIED expanding %s on cycle %d" % (number, i))
                break

            assert_toggled(number, True, "expand")
            after = d.dump()
            m = d.meminfo()
            samples.append(m)
            run.step("expand", cycle=i, car=number, nodes_delta=len(after) - before, **m)
            print("  %-6s expand   pss=%sMB unknown=%sMB native=%sMB gfx=%sMB (%+d nodes)"
                  % (number, m.get("pss_mb"), m.get("unknown_mb"), m.get("native_mb"),
                     m.get("gfx_mb"), len(after) - before))

            if args.mode == "cycle":
                hit, nodes = d.ensure_visible(text=number)
                check_foreground("collapsing %s" % number)
                d.tap(d.clickable_for(nodes, hit))
                time.sleep(2)
                # Ask for the memory back before measuring whether any came back. Sampling
                # a few seconds after the tap otherwise reads flat whether or not anything
                # became collectable, and a flat line proves nothing either way.
                trimmed = d.trim_memory()
                time.sleep(5)
                if d.pid() != start_pid:
                    died_at = i
                    run.step("process-died", cycle=i, car=number, during="collapse")
                    print("PROCESS DIED collapsing %s" % number)
                    break
                assert_toggled(number, False, "collapse")
                m = d.meminfo()
                samples.append(m)
                run.step("collapse", cycle=i, car=number, trim_accepted=trimmed, **m)
                print("  %-6s collapse pss=%sMB unknown=%sMB" % (number, m.get("pss_mb"),
                                                                m.get("unknown_mb")))

        status = "fail" if died_at is not None else "pass"
    except Exception as ex:
        # Everything, not just assertions. A jostled cable raises TimeoutExpired and a
        # stale adb path raises OSError; letting either escape would skip the save below
        # and leave no record at all - while AGENTS.md promises every run leaves one, so
        # the absence would be read as a run that never started.
        status = "fail"
        run.step("failure", detail="%s: %s" % (type(ex).__name__, ex))
        print("FAIL: %s: %s" % (type(ex).__name__, ex))
    finally:
        # Summarized here so a run that dies on cycle four still reports the three that
        # worked. Computed inside the try, a failure would throw away measurements already
        # taken and hand back an empty summary.
        summary.update(summarize(run, samples, args, died_at))

    run.finish(status, **summary)
    # Read the baseline before writing one. With --set-baseline the save would otherwise
    # overwrite what we are about to compare against, and the run would diff against
    # itself - showing no change at the exact moment the change matters most.
    previous = d.baseline(run.scenario, args.mode)
    path = run.save(set_baseline=args.set_baseline)

    print("\n--- summary ---")
    for k, v in sorted(run.data["summary"].items()):
        print("  %-24s %s" % (k, v))
    if run.data["faults"]:
        print("\n--- faults ---")
        for f in run.data["faults"][:10]:
            print("  [%s] %s" % (f["buffer"], f["line"][:160]))

    print("\n--- vs baseline ---")
    for line in d.compare(run.data, previous):
        print("  " + line)
    print("\nsaved %s" % path)
    print("status: %s" % run.data["status"])
    return 0 if run.data["status"] == "pass" else 1


if __name__ == "__main__":
    sys.exit(main())
