"""
Virtual Controller Test Tool for Playnite Overlay debugging.

Creates a virtual Xbox 360 / PS4 controller via ViGEmBus and logs
every button state change with timestamps. Use this to verify which
button events Playnite actually receives through SDL2, and whether
Steam Input or other middleware is intercepting specific buttons.

Requirements:
    pip install vgamepad keyboard

Usage:
    python tools/test_virtual_controller.py

Controls:
    W/A/S/D       - D-Pad
    K/J/L/I       - A/X/B/Y
    Q/E           - LB/RB
    Z/C           - LT/RT
    Enter/Tab/G   - Start/Back/Guide
    F1/F2         - Switch Xbox/PS4 mode
    T             - Run automated combo test sequence
    ESC           - Quit
"""

import sys
import time
import traceback
from datetime import datetime
from enum import IntFlag

try:
    import vgamepad as vg
except ImportError:
    print("[FATAL] 'vgamepad' not installed. Run: pip install vgamepad")
    sys.exit(1)

try:
    import keyboard
except ImportError:
    print("[FATAL] 'keyboard' not installed. Run: pip install keyboard")
    sys.exit(1)


class XboxButtons(IntFlag):
    DPAD_UP = 0x0001
    DPAD_DOWN = 0x0002
    DPAD_LEFT = 0x0004
    DPAD_RIGHT = 0x0008
    START = 0x0010
    BACK = 0x0020
    L1 = 0x0100
    R1 = 0x0200
    GUIDE = 0x0400
    A = 0x1000
    B = 0x2000
    X = 0x4000
    Y = 0x8000


class PS4Buttons(IntFlag):
    CROSS = 0x0010
    CIRCLE = 0x0020
    SQUARE = 0x0040
    TRIANGLE = 0x0080
    L1 = 0x0100
    R1 = 0x0200
    SHARE = 0x1000  # Back
    OPTIONS = 0x2000  # Start
    GUIDE = 0x10000  # PS Home


XBOX_NAMES = {
    XboxButtons.DPAD_UP: "DPAD_UP",
    XboxButtons.DPAD_DOWN: "DPAD_DOWN",
    XboxButtons.DPAD_LEFT: "DPAD_LEFT",
    XboxButtons.DPAD_RIGHT: "DPAD_RIGHT",
    XboxButtons.START: "START",
    XboxButtons.BACK: "BACK",
    XboxButtons.L1: "LB",
    XboxButtons.R1: "RB",
    XboxButtons.GUIDE: "GUIDE",
    XboxButtons.A: "A",
    XboxButtons.B: "B",
    XboxButtons.X: "X",
    XboxButtons.Y: "Y",
}

PS4_NAMES = {
    PS4Buttons.CROSS: "CROSS",
    PS4Buttons.CIRCLE: "CIRCLE",
    PS4Buttons.SQUARE: "SQUARE",
    PS4Buttons.TRIANGLE: "TRIANGLE",
    PS4Buttons.L1: "L1",
    PS4Buttons.R1: "R1",
    PS4Buttons.SHARE: "SHARE",
    PS4Buttons.OPTIONS: "OPTIONS",
    PS4Buttons.GUIDE: "GUIDE",
}

COMBOS = {
    "LB+RB": {
        "xbox": (XboxButtons.L1, XboxButtons.R1),
        "ps4": (PS4Buttons.L1, PS4Buttons.R1),
    },
    "START+BACK": {
        "xbox": (XboxButtons.START, XboxButtons.BACK),
        "ps4": (PS4Buttons.OPTIONS, PS4Buttons.SHARE),
    },
    "GUIDE": {"xbox": (XboxButtons.GUIDE,), "ps4": (PS4Buttons.GUIDE,)},
}


def timestamp():
    return datetime.now().strftime("%H:%M:%S.%f")[:-3]


def log(msg):
    print(f"[{timestamp()}] {msg}", flush=True)


def format_buttons(pressed, name_map):
    names = []
    for btn in sorted(pressed):
        names.append(name_map.get(btn, f"0x{btn:04X}"))
    return ", ".join(names) if names else "(none)"


class VirtualController:
    def __init__(self):
        self.gamepad = None
        self.mode = None
        self.pressed = set()
        self.log_file = None

    def connect(self, mode: str):
        if mode == self.mode and self.gamepad is not None:
            return

        if self.gamepad is not None:
            try:
                self.gamepad.reset()
                del self.gamepad
            except Exception:
                pass
            self.gamepad = None
            self.pressed.clear()

        try:
            if mode == "xbox":
                self.gamepad = vg.VX360Gamepad()
                self.mode = "xbox"
                label = "Xbox 360"
            elif mode == "ps4":
                self.gamepad = vg.VDS4Gamepad()
                self.mode = "ps4"
                label = "PS4 (DS4)"
            else:
                log(f"[ERROR] Unknown mode: {mode}")
                return

            log(f"CONNECTED: {label} virtual controller")
        except Exception as e:
            log(f"[ERROR] Failed to create {mode} controller: {e}")
            log(
                "[INFO] Ensure ViGEmBus driver is installed (https://github.com/ViGEm/ViGEmBus)"
            )

    @property
    def name_map(self):
        return XBOX_NAMES if self.mode == "xbox" else PS4_NAMES

    def press(self, btn):
        if not self.gamepad:
            return
        if btn not in self.pressed:
            self.pressed.add(btn)
            name = self.name_map.get(btn, f"0x{btn:04X}")
            log(
                f"  PRESS  {name:<10s} | now held: {format_buttons(self.pressed, self.name_map)}"
            )
            self._write_log(f"PRESS {name}")
        self.gamepad.press_button(button=btn)

    def release(self, btn):
        if not self.gamepad:
            return
        if btn in self.pressed:
            self.pressed.discard(btn)
            name = self.name_map.get(btn, f"0x{btn:04X}")
            log(
                f"  RELEASE {name:<10s} | now held: {format_buttons(self.pressed, self.name_map)}"
            )
            self._write_log(f"RELEASE {name}")
        self.gamepad.release_button(button=btn)

    def release_all(self):
        if not self.gamepad:
            return
        if self.pressed:
            log(f"  RELEASE ALL (was: {format_buttons(self.pressed, self.name_map)})")
            self._write_log("RELEASE_ALL")
        self.gamepad.reset()
        self.pressed.clear()

    def set_trigger(self, left: float = 0.0, right: float = 0.0):
        if not self.gamepad:
            return
        self.gamepad.left_trigger(value=left)
        self.gamepad.right_trigger(value=right)

    def update(self):
        if self.gamepad:
            self.gamepad.update()

    def open_log(self, path: str):
        try:
            self.log_file = open(path, "w")
            self._write_log(f"=== Virtual Controller Test - {timestamp()} ===")
            self._write_log(f"Mode: {self.mode}")
        except Exception as e:
            log(f"[WARN] Could not open log file: {e}")

    def close_log(self):
        if self.log_file:
            self._write_log(f"=== Session End - {timestamp()} ===")
            self.log_file.close()
            self.log_file = None

    def _write_log(self, msg):
        if self.log_file:
            self.log_file.write(f"[{timestamp()}] {msg}\n")
            self.log_file.flush()


def run_combo_test(ctrl: VirtualController):
    """Send each combo as a clean press-hold-release sequence with logging."""
    mode = ctrl.mode
    log("=" * 60)
    log("AUTOMATED COMBO TEST SEQUENCE")
    log("=" * 60)

    for combo_name, buttons in COMBOS.items():
        btns = buttons[mode]
        btn_names = [ctrl.name_map.get(b, f"0x{b:04X}") for b in btns]
        log(f"\n--- Testing: {combo_name} ({' + '.join(btn_names)}) ---")

        for btn in btns:
            ctrl.press(btn)
        ctrl.update()

        time.sleep(0.2)

        ctrl.release_all()
        ctrl.update()

        log(f"--- {combo_name}: done ---")
        time.sleep(0.5)

    log("\n" + "=" * 60)
    log("COMBO TEST COMPLETE")
    log("=" * 60)
    log("Compare timestamps above with Playnite's log to see which events arrived.")


def read_keyboard_input():
    bindings = {
        "w": "dpad_up",
        "s": "dpad_down",
        "a": "dpad_left",
        "d": "dpad_right",
        "k": "a",
        "j": "x",
        "l": "b",
        "i": "y",
        "q": "l1",
        "e": "r1",
        "enter": "start",
        "tab": "back",
        "g": "guide",
    }
    triggers = {"z": "lt", "c": "rt"}

    pressed_buttons = {}
    pressed_triggers = {}

    for key, action in bindings.items():
        if keyboard.is_pressed(key):
            pressed_buttons[action] = key

    for key, trigger in triggers.items():
        if keyboard.is_pressed(key):
            pressed_triggers[trigger] = key

    return pressed_buttons, pressed_triggers


def map_to_mode(pressed_buttons, mode):
    if mode == "xbox":
        mapping = {
            "dpad_up": XboxButtons.DPAD_UP,
            "dpad_down": XboxButtons.DPAD_DOWN,
            "dpad_left": XboxButtons.DPAD_LEFT,
            "dpad_right": XboxButtons.DPAD_RIGHT,
            "a": XboxButtons.A,
            "b": XboxButtons.B,
            "x": XboxButtons.X,
            "y": XboxButtons.Y,
            "l1": XboxButtons.L1,
            "r1": XboxButtons.R1,
            "start": XboxButtons.START,
            "back": XboxButtons.BACK,
            "guide": XboxButtons.GUIDE,
        }
    else:
        mapping = {
            "a": PS4Buttons.CROSS,
            "b": PS4Buttons.CIRCLE,
            "x": PS4Buttons.SQUARE,
            "y": PS4Buttons.TRIANGLE,
            "l1": PS4Buttons.L1,
            "r1": PS4Buttons.R1,
            "start": PS4Buttons.OPTIONS,
            "back": PS4Buttons.SHARE,
            "guide": PS4Buttons.GUIDE,
        }

    return {action: mapping[action] for action in pressed_buttons if action in mapping}


def main():
    ctrl = VirtualController()
    ctrl.connect("xbox")

    if not ctrl.gamepad:
        log("[FATAL] Failed to initialize controller. Exiting.")
        sys.exit(1)

    log_path = f"controller_test_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
    ctrl.open_log(log_path)
    log(f"Log file: {log_path}")

    print("""
============================================
  VIRTUAL CONTROLLER TEST TOOL
============================================
  MOVEMENT:    W/A/S/D (D-Pad)
  FACE:        K(A)  J(X)  L(B)  I(Y)
  BUMPERS:     Q(LB)  E(RB)
  TRIGGERS:    Z(LT)  C(RT)
  SYSTEM:      Enter(Start)  Tab(Back)  G(Guide)
  COMBO TEST:  T  (automated sequence)
  SWITCH:      F1(Xbox)  F2(PS4)
  QUIT:        ESC
============================================
Watch the PLAYNITE LOG alongside this output.
Compare timestamps to see which events arrive.
============================================
""")

    prev_buttons = {}

    try:
        while True:
            if keyboard.is_pressed("f1"):
                ctrl.release_all()
                ctrl.update()
                ctrl.close_log()
                ctrl.connect("xbox")
                if ctrl.gamepad:
                    ctrl.open_log(
                        f"controller_test_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
                    )
                time.sleep(0.3)
                prev_buttons.clear()
                continue
            elif keyboard.is_pressed("f2"):
                ctrl.release_all()
                ctrl.update()
                ctrl.close_log()
                ctrl.connect("ps4")
                if ctrl.gamepad:
                    ctrl.open_log(
                        f"controller_test_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
                    )
                time.sleep(0.3)
                prev_buttons.clear()
                continue

            if keyboard.is_pressed("t"):
                ctrl.release_all()
                ctrl.update()
                time.sleep(0.1)
                run_combo_test(ctrl)
                time.sleep(0.5)
                prev_buttons.clear()
                continue

            if keyboard.is_pressed("esc"):
                log("Exiting...")
                break

            if not ctrl.gamepad:
                time.sleep(0.1)
                continue

            pressed_actions, pressed_triggers = read_keyboard_input()
            current_buttons = map_to_mode(pressed_actions, ctrl.mode)

            all_actions = set(prev_buttons.keys()) | set(current_buttons.keys())

            for action in all_actions:
                was_pressed = action in prev_buttons
                is_pressed = action in current_buttons
                if is_pressed and not was_pressed:
                    ctrl.press(current_buttons[action])
                elif was_pressed and not is_pressed:
                    ctrl.release(prev_buttons[action])

            prev_buttons = current_buttons.copy()

            lt = 255 if "lt" in pressed_triggers else 0
            rt = 255 if "rt" in pressed_triggers else 0
            ctrl.set_trigger(lt, rt)

            ctrl.update()
            time.sleep(0.01)

    except KeyboardInterrupt:
        log("\nStopped by user.")

    finally:
        ctrl.release_all()
        ctrl.update()
        ctrl.close_log()
        if ctrl.gamepad:
            try:
                del ctrl.gamepad
            except Exception:
                pass


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"\n{'=' * 50}\nCRITICAL ERROR:\n{'=' * 50}")
        traceback.print_exc()
        print(f"\nTroubleshooting:")
        print("  1. Ensure ViGEmBus driver is installed")
        print("  2. Try running as Administrator")
        input()
