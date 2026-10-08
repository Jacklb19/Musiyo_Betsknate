"""Drive a served museum Web build in a visible browser window and record canvas screenshots per step.

Unity stops rendering in hidden windows, and keys sent as an instant down/up pair can miss held-axis
input, so this script uses its own visible window and holds each key like a person would. It does not
judge results: review the screenshots and steps.txt in the output directory.

Requires Python Playwright and a local Chrome. Example:
    python tools/Development/verify_web_visit.py --url http://127.0.0.1:8090/index.html --out <directory> --scenario menu
"""
import argparse
import os
import time

from playwright.sync_api import sync_playwright

SCENARIOS = ("menu", "pointer", "content")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--url", required=True, help="Page that hosts the build; deep-link parameters are allowed.")
    parser.add_argument("--out", required=True, help="New or empty directory for screenshots and the step log.")
    parser.add_argument("--scenario", choices=SCENARIOS, default="menu")
    parser.add_argument("--canvas", default="#unity-canvas", help="CSS selector of the Unity canvas.")
    parser.add_argument("--load-seconds", type=float, default=18, help="Wait for the build and tour to load.")
    parser.add_argument("--model-seconds", type=float, default=25, help="Wait for a remote model in 'content'.")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    log = open(os.path.join(args.out, "steps.txt"), "w", encoding="utf-8")

    def note(text):
        print(text, flush=True)
        log.write(text + "\n")
        log.flush()

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(channel="chrome", headless=False, args=["--window-size=1400,1000"])
        page = browser.new_page(viewport={"width": 1360, "height": 900})
        page.on("console", lambda message: message.type == "error" and note("console error: " + message.text[:200]))
        page.goto(args.url)
        canvas = page.locator(args.canvas)
        canvas.wait_for()
        time.sleep(args.load_seconds)
        box = canvas.bounding_box()
        count = [0]

        def shot(name, wait=.6):
            time.sleep(wait)
            count[0] += 1
            canvas.screenshot(path=os.path.join(args.out, f"{count[0]:02d}_{name}.png"))
            visible, locked = page.evaluate("[document.visibilityState, !!document.pointerLockElement]")
            note(f"{count[0]:02d} {name} visibility={visible} pointerLocked={locked}")

        def tap(key, hold=.12):
            page.keyboard.down(key)
            time.sleep(hold)
            page.keyboard.up(key)
            time.sleep(.15)

        def hold(key, seconds):
            page.keyboard.down(key)
            time.sleep(seconds)
            page.keyboard.up(key)

        def point(fx, fy):
            return box["x"] + box["width"] * fx, box["y"] + box["height"] * fy

        shot("loaded")
        if args.scenario == "menu":
            page.mouse.click(*point(.08, .85)); shot("backdrop_click_clears_focus")
            tap("ArrowDown"); shot("arrow_restores_focus")
            tap("ArrowDown"); shot("arrow_down")
            tap("Tab"); shot("tab_next")
            page.keyboard.down("Shift"); tap("Tab"); page.keyboard.up("Shift"); shot("shift_tab_previous")
            tap("s"); shot("s_next")
            tap("w"); shot("w_previous")
            tap("Enter"); shot("enter_opens_settings")
            for _ in range(3):
                tap("ArrowRight")
            shot("sensitivity_right")
            tap("ArrowDown"); tap("ArrowLeft"); tap("ArrowLeft"); shot("volume_left")
            tap("Backspace"); shot("backspace_home_focus_explore")
            tap("Enter"); shot("entering", .45); shot("welcome_after_entry", 1.6)
            tap("Enter"); shot("welcome_dismissed")
            tap("Escape"); shot("pause")
            tap("Tab"); tap("e"); tap("h"); shot("pause_ignores_point_keys")
            hold("w", 1.0); shot("pause_w_held_without_walking")
            tap("Escape"); shot("escape_resumes")
            hold("w", 1.0); shot("walking_after_resume")
            tap("Escape")
            for _ in range(3):
                tap("ArrowDown")
            shot("pause_main_menu_focus")
            tap("Enter"); shot("back_to_entry_menu")
            tap("Enter"); shot("continue_visit", 1.8)
        elif args.scenario == "pointer":
            page.mouse.click(*point(.08, .85)); tap("Enter"); tap("Enter"); shot("entered", 2.5)
            tap("Enter"); shot("welcome_dismissed")
            x, y = point(.5, .62)
            page.mouse.move(x, y); time.sleep(.3)
            page.mouse.down(); time.sleep(.1); page.mouse.up(); shot("pointer_captured", 1)
            page.mouse.move(x + 40, y); shot("mouse_look")
            page.evaluate("document.exitPointerLock()"); shot("browser_release_pauses")
            tap("Escape"); shot("escape_resumes")
            page.mouse.move(x, y); time.sleep(.2)
            page.mouse.down(); time.sleep(.1); page.mouse.up(); shot("pointer_captured_again", 1)
            page.evaluate("document.exitPointerLock()"); time.sleep(.03); tap("Escape", .05)
            shot("release_plus_escape_stays_paused")
        else:
            page.evaluate(f"document.querySelector({args.canvas!r}).focus()")
            shot("deep_link_point", 2)
            tap("ArrowDown"); tap("Enter"); shot("element_selected", 1)
            shot("model_loaded", args.model_seconds)
            tap("x"); shot("examination", 1.5)
            hold("ArrowLeft", .8); shot("rotated")
            tap("Home"); shot("pose_reset")
            tap("PageDown"); shot("panel_scrolled")
            tap("PageUp"); tap("PageUp"); shot("panel_top")
            tap("f"); shot("reading")
            tap("Backspace"); shot("back_to_examination")
            tap("Escape"); shot("pause_during_examination")
            tap("Escape"); shot("resumed_examination")
            tap("Backspace"); tap("Backspace"); shot("point_closed_without_dwell_progress", 1.5)
            tap("Tab"); tap("Tab"); shot("tab_focuses_point")
            tap("Enter"); shot("enter_activates_point", 1)
        browser.close()


if __name__ == "__main__":
    main()
