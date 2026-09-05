"""Walk every screen on a real device and assert nothing broke on the way.

Run:  python tools/device/smoke.py [--set-baseline]

What it is for: the failures that only exist on hardware. A cold start that renders
nothing, a screen that comes up empty because configuration is blank, a navigation stack
that drops out of the app, an exception message shown to a driver instead of something
they can act on. None of that is visible to the headless tests, which never start a
process on a phone.

Takes roughly two minutes. Leaves a JSON record under results/smoke/.
"""
import argparse
import sys
import time

import devdrive as d

# Device text can carry characters a Windows console will not encode, and losing the run
# to a UnicodeEncodeError while printing the failure would throw away the record of it.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# The screen text that means "this screen loaded". Deliberately the words a person would
# look for, not internal names, so these keep working when the layout is rearranged.
DRIVER = "In-Car Driver Settings"

# The list screens are not named by anything on them - see devdrive.wait_home. What is used
# below is devdrive.TO_COMPLETED and TO_HOME, which are the toggle at the bottom, and each
# of those names the screen you are NOT on. They are tapped, never treated as identity.


def clean(run, screen, nodes):
    """Every screen gets checked for text that reads like a stack trace rather than like
    something a driver could act on. This is the standing regression test for the blank
    Keycloak configuration that used to surface as "The given key was not present in the
    dictionary" on the driver settings screen.

    Reads raw_strings rather than texts: the latter strips Avalonia's ToString fallbacks,
    which share their namespace prefix with a real exception message, so the tidier list is
    the one an exception can hide in.
    """
    for t in d.raw_strings(nodes):
        if d.looks_like_raw_exception(t):
            raise AssertionError("%s is showing raw exception text: %r" % (screen, t))
    run.step("screen", screen=screen, nodes=len(nodes), sample=d.texts(nodes)[:6])


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    # Pinning the event and session is what makes a baseline mean anything over time. The
    # default is whatever is first in the archive, and that changes every time a race
    # finishes - so a run left on the defaults is comparing a different event's session
    # counts and lap counts against last month's.
    ap.add_argument("--event", help="substring of the completed event to open")
    ap.add_argument("--session", help="substring of the session to open")
    ap.add_argument("--set-baseline", action="store_true",
                    help="also store this run as the baseline for later comparison")
    args = ap.parse_args()

    try:
        if not d.awake():
            print("CANNOT START: the phone is locked or asleep. uiautomator cannot read a "
                  "lock screen; unlock it and run again.")
            return 2
        # Resolving the launcher activity here rather than at first use: it is what
        # raises when the app is not installed, and this is the block that turns that
        # into exit 2 instead of a traceback indistinguishable from a test failure.
        d.activity()
        run = d.Run("smoke", notes={"event": args.event, "session": args.session})
    except (RuntimeError, OSError) as ex:
        # No device, adb not on the machine, app not installed. Exit 2 keeps all of that
        # separate from exit 1, which has to mean the app did something wrong.
        print("CANNOT START: %s" % ex)
        return 2
    print("device: %(manufacturer)s %(model)s, Android %(android)s, %(total_ram_mb)s MB RAM"
          % run.data["device"])
    print("app:    %(version_name)s (code %(version_code)s)" % run.data["app"])

    summary = {}
    try:
        # 1. Cold start. Measured, because a regression here is the most user visible one
        #    there is and nothing else in the repo measures it. Two numbers, and they are
        #    not the same thing: cold_start_ms is the platform's own time to first frame,
        #    and home_ready_s is how long until the event list actually had content, which
        #    includes fetching it over the network.
        t0 = time.time()
        run.since = d.launch(cold=True)
        nodes = d.wait_home()
        summary["home_ready_s"] = round(time.time() - t0, 1)
        summary["cold_start_ms"] = d.displayed_ms()
        start_pid = d.pid()
        clean(run, "home", nodes)
        summary["live_event_count"] = len(d.list_rows(nodes))
        print("first frame %sms, home ready %.1fs, %d live events"
              % (summary["cold_start_ms"], summary["home_ready_s"],
                 summary["live_event_count"]))

        # 2. Completed events, including the paging that the retry work touched. A page
        #    that fails to load must not silently look like the end of the archive.
        d.tap_text(d.TO_COMPLETED)
        _, nodes = d.wait_for(contains="Page 1")
        clean(run, "completed-events", nodes)
        summary["completed_event_count"] = len(d.list_rows(nodes))

        d.tap_match(text="Next")
        _, nodes = d.wait_for(contains="Page 2")
        clean(run, "completed-events-page-2", nodes)
        summary["page_2_event_count"] = len(d.list_rows(nodes))

        # Asserted, not merely recorded. Every screen here renders its chrome - title,
        # pager, bottom bar - whether or not a single row arrived, so without this the run
        # reports "all screens ok" against an app that loaded nothing at all, which is the
        # blank-configuration failure this scenario exists to catch. The live list is left
        # unasserted on purpose: an empty one is legitimate between seasons, while an empty
        # archive never is.
        assert summary["completed_event_count"] > 0, \
            "the completed events list rendered but is empty - backend or configuration"
        assert summary["page_2_event_count"] > 0, "page 2 of the archive came back empty"
        d.tap_match(text="Previous")
        _, nodes = d.wait_for(contains="Page 1")

        # 3. Into an event, then into one of its sessions.
        if args.event:
            d.tap_text(args.event)
        else:
            rows = d.list_rows(nodes)
            assert rows, "no completed events to open"
            d.tap(rows[0])
        _, nodes = d.wait_for(contains="Provisional Results for")
        clean(run, "event", nodes)
        sessions = d.list_rows(nodes)
        summary["session_count"] = len(sessions)
        assert sessions, "event has no sessions"

        if args.session:
            d.tap_text(args.session)
        else:
            d.tap(sessions[0])
        _, nodes = d.wait_for(timeout=45, contains="Laps:")
        clean(run, "session-results", nodes)
        cars = d.find_all(nodes, cls="ToggleButton", min_h=40)
        summary["car_row_count"] = len(cars)
        summary["session_laps"] = next(
            (int(t.split(":")[1]) for t in d.texts(nodes)
             if t.startswith("Laps:") and t.split(":")[1].strip().isdigit()), None)
        assert cars, "session results have no car rows"

        # 4. Expand a car and collapse it again. This is the path behind the crash report
        #    that soak.py exists to chase, so the smoke run at least proves it opens.
        #    Checked by the panel's own tabs appearing, not by the tree getting bigger:
        #    the panel pushes as many rows off the bottom as it adds, so the node count
        #    barely moves and once came back exactly unchanged.
        before = len(nodes)
        d.tap(cars[0])
        # Waited for, not slept on. The panel renders a chart and every lap of the session,
        # so a fixed delay that is comfortable on this phone and this session is a coin
        # flip on a slower one or a longer race - and it fails as though the app were
        # broken. The node delta is recorded as a step only; it is not a signal, since the
        # panel pushes about as many rows off the bottom as it adds and once came back
        # exactly unchanged.
        _, nodes = d.wait_for(timeout=45, text="Positions")
        clean(run, "car-expanded", nodes)
        run.step("expand", delta_nodes=len(nodes) - before)

        d.tap(cars[0])
        # Exact match, deliberately: the scroll bar contributes a node whose text is
        # "Position", so a contains= here would never see the panel go away.
        _, _ = d.poll(lambda ns: d.find(ns, text="Positions") is None or None,
                      timeout=30, describe="the collapsed car panel")

        # 5. Back out, asserting the navigation the app actually has rather than the one
        #    it looks like it should have. Back from a session returns to the completed
        #    list, skipping the event page it was reached through. Back from the completed
        #    list then leaves the app entirely - there is one activity, and that list is a
        #    root rather than a child of home, so the way back to the live list is the
        #    button at the bottom. Both are pinned here so a change to either shows up.
        d.back()
        assert d.foreground(), "Back from a session left the app"
        _, nodes = d.wait_for(contains="Page 1")
        clean(run, "completed-events-after-back", nodes)

        d.tap_text(d.TO_HOME)
        nodes = d.wait_home()
        clean(run, "home-again", nodes)

        # 6. Driver mode. Worth its own step because it is the screen that reads live
        #    configuration and talks to Keycloak, and the one drivers see first.
        d.tap_text("Driver Mode")
        _, nodes = d.wait_for(timeout=45, contains=DRIVER)
        clean(run, "driver-mode", nodes)
        d.back()
        assert d.foreground(), "Back from driver mode left the app"
        clean(run, "home-after-driver-mode", d.wait_home())

        summary["final_pss_mb"] = d.meminfo().get("pss_mb")
        assert d.pid() == start_pid, "the process restarted during the run"
        status = "pass"
        print("all screens ok")
    except Exception as ex:
        # Everything, not just assertions. A jostled cable raises TimeoutExpired and a
        # stale adb path raises OSError; letting either escape would skip the save below
        # and leave no record - while AGENTS.md promises every run leaves one, so the
        # absence would be read as a run that never started.
        status = "fail"
        run.step("failure", detail="%s: %s" % (type(ex).__name__, ex))
        print("FAIL: %s: %s" % (type(ex).__name__, ex))

    run.finish(status, **summary)
    # Read the baseline before writing one. With --set-baseline the save would otherwise
    # overwrite what we are about to compare against, and the run would diff against
    # itself - showing no change at the exact moment the change matters most.
    previous = d.baseline(run.scenario)
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
