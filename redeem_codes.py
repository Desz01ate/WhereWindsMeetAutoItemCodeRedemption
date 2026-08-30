from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import html
import json
import re
import subprocess
import time
import urllib.request
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Code:
    value: str
    rewards: tuple[str, ...] = ()


CODE_RE = re.compile(r"\b[A-Z0-9]{6,32}\b", re.IGNORECASE)


def fetch(url: str, timeout: float) -> str:
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "WhereWindsMeetCodeRedeemer/1.0", "Accept": "application/json, text/html"},
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read().decode(response.headers.get_content_charset() or "utf-8", errors="replace")


def scrape_api(url: str, timeout: float) -> list[Code]:
    try:
        payload = json.loads(fetch(url, timeout))
    except (OSError, ValueError) as exc:
        print(f"[warn] API unavailable: {url} ({exc})")
        return []
    return [Code(str(entry["code"]).strip().upper()) for entry in payload.get("active", [])
            if isinstance(entry, dict) and entry.get("code")]


def scrape(sources: list[str], api_sources: list[str], timeout: float) -> list[Code]:
    found: dict[str, Code] = {}
    for url in api_sources:
        for code in scrape_api(url, timeout):
            if CODE_RE.fullmatch(code.value):
                found.setdefault(code.value, code)
    for url in sources:
        try:
            body = html.unescape(fetch(url, timeout))
        except Exception as exc:
            print(f"[warn] source unavailable: {url} ({exc})")
            continue
        # Only accept explicit code fields or the first cell of a two-column code table.
        candidates = re.findall(r"(?:value|data-code)=[\"']([^\"']+)[\"']", body, re.IGNORECASE)
        for row in re.findall(r"<tr\b[^>]*>\s*<td\b[^>]*>(.*?)</td>\s*<td\b", body, re.IGNORECASE | re.DOTALL):
            candidates.extend(re.findall(r"\b[A-Z0-9]{6,32}\b", re.sub(r"<[^>]+>", " ", row)))
        for raw in candidates:
            value = raw.strip().upper()
            if not CODE_RE.fullmatch(value) or value in {"REWARDS", "ACTIVE", "EXPIRED", "WHEREWINDS"}:
                continue
            found.setdefault(value, Code(value))
    return list(found.values())


def load_state(path: Path) -> set[str]:
    if not path.exists():
        return set()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return {str(x).upper() for x in data.get("redeemed", [])}
    except (OSError, ValueError) as exc:
        raise RuntimeError(f"Cannot read state file {path}: {exc}") from exc


def save_state(path: Path, redeemed: set[str]) -> None:
    path.write_text(json.dumps({"redeemed": sorted(redeemed)}, indent=2) + "\n", encoding="utf-8")
def ensure_state(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        save_state(path, set())
        print(f"Initialized redemption state at {path}.")



def resolve_state_path(config_path: Path, configured_path: str) -> Path:
    state_path = Path(configured_path)
    return state_path if state_path.is_absolute() else config_path.parent / state_path



if hasattr(ctypes, "windll"):
    user32 = ctypes.windll.user32
    WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)


KEYEVENTF_KEYUP = 0x0002
KEYEVENTF_UNICODE = 0x0004
KEYEVENTF_SCANCODE = 0x0008
INPUT_KEYBOARD = 1
VK_CONTROL, VK_A, VK_ESCAPE, VK_SPACE = 0x11, 0x41, 0x1B, 0x20
SCANCODES = {VK_CONTROL: 0x1D, VK_A: 0x1E, VK_ESCAPE: 0x01, VK_SPACE: 0x39, 0x56: 0x2F}

class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD), ("time", wt.DWORD), ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("padding", wt.BYTE * 32)]


class INPUT(ctypes.Structure):
    _anonymous_ = ("u",)
    _fields_ = [("type", wt.DWORD), ("u", INPUTUNION)]


def require_windows() -> None:
    if not hasattr(ctypes, "windll"):
        raise RuntimeError("--execute requires Windows for external game-window input.")
    # Geometry must be reported in physical pixels, matching the game screenshot.
    set_dpi_context = getattr(user32, "SetProcessDpiAwarenessContext", None)
    if set_dpi_context is not None and set_dpi_context(ctypes.c_void_p(-4)):
        return
    if not user32.SetProcessDPIAware():
        raise ctypes.WinError()

def send_keyboard(w_vk: int, w_scan: int = 0, flags: int = 0) -> None:
    event = INPUT(type=INPUT_KEYBOARD, u=INPUTUNION(ki=KEYBDINPUT(w_vk, w_scan, flags, 0, None)))
    sent = user32.SendInput(1, ctypes.byref(event), ctypes.sizeof(INPUT))
    if sent != 1:
        raise ctypes.WinError()


def send_unicode(w_scan: int, flags: int = KEYEVENTF_UNICODE) -> None:
    event = INPUT(type=INPUT_KEYBOARD, u=INPUTUNION(ki=KEYBDINPUT(0, w_scan, flags, 0, None)))
    sent = user32.SendInput(1, ctypes.byref(event), ctypes.sizeof(INPUT))
    if sent != 1:
        raise ctypes.WinError()


def _process_name(pid: int) -> str | None:
    process = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)
    if not process:
        return None
    try:
        buffer = ctypes.create_unicode_buffer(1024)
        size = wt.DWORD(len(buffer))
        if ctypes.windll.kernel32.QueryFullProcessImageNameW(process, 0, buffer, ctypes.byref(size)):
            return Path(buffer.value).name.casefold()
        return None
    finally:
        ctypes.windll.kernel32.CloseHandle(process)


def find_game_window(pid: int | None, process_name: str | None) -> tuple[int, int, str]:
    matches: list[tuple[int, int, str]] = []

    @WNDENUMPROC
    def callback(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        process_id = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        candidate_pid = int(process_id.value)
        candidate_name = _process_name(candidate_pid)
        if candidate_name and ((pid is not None and candidate_pid == pid) or
                               (process_name and candidate_name == process_name.casefold())):
            matches.append((int(hwnd), candidate_pid, candidate_name))
            return False
        return True

    user32.EnumWindows(callback, 0)
    if not matches:
        target = f"PID {pid}" if pid is not None else f"process {process_name!r}"
        raise RuntimeError(f"No visible top-level window found for {target}.")
    return matches[0]


def client_size(hwnd: int) -> tuple[int, int]:
    rect = wt.RECT()
    if not user32.GetClientRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError()
    return rect.right - rect.left, rect.bottom - rect.top





def key_down(vk: int) -> None:
    scan = SCANCODES.get(vk)
    if scan is None:
        send_keyboard(vk)
    else:
        send_keyboard(0, scan, KEYEVENTF_SCANCODE)


def key_up(vk: int) -> None:
    scan = SCANCODES.get(vk)
    if scan is None:
        send_keyboard(vk, KEYEVENTF_KEYUP)
    else:
        send_keyboard(0, scan, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP)


def key(vk: int, hold_seconds: float = 0.08) -> None:
    key_down(vk)
    time.sleep(hold_seconds)
    key_up(vk)


def chord(modifier: int, vk: int) -> None:
    key_down(modifier)
    try:
        key(vk)
    finally:
        key_up(modifier)


def type_text(text: str) -> None:
    # Keep Ctrl held while sending V; Unicode fallback uses wScan, not wVk.
    try:
        subprocess.run(["clip"], input=text, text=True, check=True, capture_output=True)
        chord(VK_CONTROL, 0x56)
    except (OSError, subprocess.CalledProcessError):
        for char in text:
            send_unicode(ord(char))
            send_unicode(ord(char), KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)

def click_normalized(hwnd: int, point: list[float]) -> None:
    width, height = client_size(hwnd)
    x, y = int(width * point[0]), int(height * point[1])
    point_struct = wt.POINT(x, y)
    user32.ClientToScreen(hwnd, ctypes.byref(point_struct))
    print(f"[input] client={width}x{height} target=({x},{y}) screen=({point_struct.x},{point_struct.y})")
    user32.SetCursorPos(point_struct.x, point_struct.y)
    user32.mouse_event(0x0002, 0, 0, 0, 0)
    user32.mouse_event(0x0004, 0, 0, 0, 0)


def redeem_one(hwnd: int, code: str, ui: dict, delay: float, result_wait: float, space_fallback: bool = False) -> None:
    focus_game(hwnd)
    click_normalized(hwnd, ui["exchange_button"])
    time.sleep(delay)
    click_normalized(hwnd, ui["code_input"])
    chord(VK_CONTROL, VK_A)
    type_text(code)
    submit_point = ui.get("submit_button")
    cancel_point = ui.get("cancel_button")
    if submit_point:
        click_normalized(hwnd, submit_point)
        time.sleep(delay)
        click_normalized(hwnd, cancel_point)    
    elif space_fallback:
        key(VK_SPACE, hold_seconds=0.12)
    else:
        raise RuntimeError("Missing ui.submit_button; run --calibrate or explicitly pass --space-fallback.")
    time.sleep(result_wait)


def focus_game(hwnd: int) -> None:
    user32.ShowWindow(hwnd, 9)  # SW_RESTORE
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.75)
    if user32.GetForegroundWindow() != hwnd:
        raise RuntimeError("Could not focus the game window; refusing to send input elsewhere.")


def cursor_client_normalized(hwnd: int) -> list[float]:
    cursor = wt.POINT()
    if not user32.GetCursorPos(ctypes.byref(cursor)):
        raise ctypes.WinError()
    if not user32.ScreenToClient(hwnd, ctypes.byref(cursor)):
        raise ctypes.WinError()
    width, height = client_size(hwnd)
    if width <= 0 or height <= 0:
        raise RuntimeError("Game window has no usable client area.")
    return [round(cursor.x / width, 6), round(cursor.y / height, 6)]

def wait_calibration_key() -> None:
    confirm = ord("C")
    abort = ord("Q")
    while user32.GetAsyncKeyState(confirm) & 0x8000 or user32.GetAsyncKeyState(abort) & 0x8000:
        time.sleep(0.05)
    while True:
        if user32.GetAsyncKeyState(abort) & 0x8000:
            raise RuntimeError("Calibration cancelled.")
        if user32.GetAsyncKeyState(confirm) & 0x8000:
            while user32.GetAsyncKeyState(confirm) & 0x8000:
                time.sleep(0.05)
            return
        time.sleep(0.05)


def calibrate(config_path: Path, config: dict, hwnd: int) -> None:
    focus_game(hwnd)
    width, height = client_size(hwnd)
    print(f"Calibrating against client area {width}x{height}.")
    points = {}
    for label in ("exchange_button", "code_input", "submit_button", "cancel_button"):
        instruction = "Hover the control"
        if label == "code_input":
            instruction = "Open the redemption dialog, then hover the code input"
        elif label == "submit_button":
            instruction = "With the redemption dialog open, hover the Submit button"
        elif label == "cancel_button":
            instruction = "With the redemption dialog open, hover the Cancel button"
        print(f"{instruction}, then press C. Press Q to abort.")
        wait_calibration_key()
        points[label] = cursor_client_normalized(hwnd)
        print(f"{label} saved as {points[label]}.")
    config.setdefault("ui", {}).update(points)
    config["ui"]["coordinate_space"] = "client_normalized"
    config["ui"]["calibrated_client_size"] = [width, height]
    config_path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    print(f"Saved calibrated coordinates to {config_path}.")

def show_targets(config: dict, hwnd: int) -> None:
    focus_game(hwnd)
    width, height = client_size(hwnd)
    for label in ("exchange_button", "code_input", "submit_button"):
        point = config.get("ui", {}).get(label)
        if not point:
            raise RuntimeError(f"Missing UI target {label!r}.")
        client_point = wt.POINT(int(width * point[0]), int(height * point[1]))
        user32.ClientToScreen(hwnd, ctypes.byref(client_point))
        user32.SetCursorPos(client_point.x, client_point.y)
        print(f"[preview] {label}: client=({int(width * point[0])},{int(height * point[1])}) screen=({client_point.x},{client_point.y})")
        time.sleep(1.0)


def confirm_submission() -> bool:
    print("Inspect the game result. Press C only if redemption succeeded; press Q to leave the code pending.")
    while user32.GetAsyncKeyState(ord("C")) & 0x8000 or user32.GetAsyncKeyState(ord("Q")) & 0x8000:
        time.sleep(0.05)
    while True:
        if user32.GetAsyncKeyState(ord("Q")) & 0x8000:
            return False
        if user32.GetAsyncKeyState(ord("C")) & 0x8000:
            while user32.GetAsyncKeyState(ord("C")) & 0x8000:
                time.sleep(0.05)
            return True
        time.sleep(0.05)


def main() -> int:
    parser = argparse.ArgumentParser(description="Retrieve Where Winds Meet codes and redeem them through the visible game UI.")
    parser.add_argument("--config", default="config.json")
    parser.add_argument("--execute", action="store_true", help="Actually send input; without this flag the run is preview-only.")
    parser.add_argument("--once", action="store_true", help="Stop after one newly found code.")
    parser.add_argument("--pid", type=int, help="Optional one-run PID override.")
    parser.add_argument("--calibrate", action="store_true", help="Hover targets and press C to save client-normalized coordinates.")
    parser.add_argument("--show-targets", action="store_true", help="Move the cursor to configured targets without clicking.")
    parser.add_argument("--space-fallback", action="store_true", help="Use the Space confirm binding only when submit_button is not calibrated.")
    args = parser.parse_args()
    if args.calibrate and args.show_targets:
        parser.error("--calibrate and --show-targets are mutually exclusive")
    config_path = Path(args.config)
    config = json.loads(config_path.read_text(encoding="utf-8"))
    state_path = resolve_state_path(config_path, str(config.get("state_file", "redeemed_codes.json")))
    ensure_state(state_path)
    if args.calibrate or args.show_targets:
        require_windows()
        hwnd, resolved_pid, resolved_name = find_game_window(args.pid, config.get("process_name"))
        print(f"Using visible {resolved_name} window, PID {resolved_pid}.")
        if args.calibrate:
            calibrate(config_path, config, hwnd)
        else:
            show_targets(config, hwnd)
        return 0
    timing = config.get("timing", {})
    codes = scrape(config.get("sources", []), config.get("api_sources", []), float(timing.get("page_timeout_seconds", 30)))
    redeemed = load_state(state_path)
    pending = [code for code in codes if code.value not in redeemed]
    print(f"Found {len(codes)} unique codes; {len(pending)} pending redemption.")

    if args.execute:
        require_windows()
    hwnd = None
    if args.execute:
        hwnd, resolved_pid, resolved_name = find_game_window(args.pid, config.get("process_name"))
        print(f"Using visible {resolved_name} window, PID {resolved_pid}; UI input is external only.")
    for code in pending:
        print(f"{'Redeeming' if args.execute else 'Would redeem'} {code.value}")
        if not args.execute:
            continue
        redeem_one(hwnd, code.value, config["ui"], float(timing.get("ui_delay_seconds", .35)), float(timing.get("result_wait_seconds", 2)), args.space_fallback)
        redeemed.add(code.value)
        save_state(state_path, redeemed)
        if args.once:
            break
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
