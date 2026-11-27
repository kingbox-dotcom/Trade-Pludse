"""Strict Read-Only MetaTrader 5 Gateway (JSON Lines over stdin/stdout)."""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone
from typing import Any

ALLOWED_MT5_FUNCTIONS = frozenset({
    "initialize",
    "terminal_info",
    "account_info",
    "positions_get",
    "orders_get",
    "history_orders_get",
    "history_deals_get",
    "version",
    "last_error",
    "shutdown",
})

ALLOWED_COMMANDS = frozenset({
    "ping",
    "connect_attach",
    "get_snapshot",
    "get_history_deals",
    "disconnect",
})

MAX_FRAME_BYTES = 1024 * 1024  # 1 MB


def log_stderr(message: str) -> None:
    ts = datetime.now(timezone.utc).isoformat()
    sys.stderr.write(f"[{ts}] {message}\n")
    sys.stderr.flush()


def make_response(request_id: str, command: str, success: bool, data: Any = None, error: str | None = None) -> dict[str, Any]:
    return {
        "requestId": request_id,
        "command": command,
        "success": success,
        "data": data,
        "error": error,
        "tsUtc": datetime.now(timezone.utc).isoformat(),
    }


def to_plain(value: Any) -> Any:
    if hasattr(value, "_asdict"):
        return {key: to_plain(item) for key, item in value._asdict().items()}
    if isinstance(value, (list, tuple)):
        return [to_plain(item) for item in value]
    if isinstance(value, dict):
        return {str(key): to_plain(item) for key, item in value.items()}
    return value


def mt5_last_error_text(mt5: Any) -> str:
    try:
        code, description = mt5.last_error()
        return f"{description} (code {code})"
    except Exception:
        return "Không thể đọc lỗi từ MT5"


def connect_attach(mt5: Any, terminal_path: str) -> dict[str, Any]:
    if not terminal_path:
        return {"success": False, "error": "TERMINAL_PATH_REQUIRED"}
    if not os.path.isabs(terminal_path):
        return {"success": False, "error": "TERMINAL_PATH_NOT_ABSOLUTE"}
    if os.path.basename(terminal_path).lower() != "terminal64.exe":
        return {"success": False, "error": "INVALID_TERMINAL_FILE"}
    if not os.path.isfile(terminal_path):
        return {"success": False, "error": "TERMINAL_NOT_FOUND"}

    if not mt5.initialize(path=terminal_path):
        return {"success": False, "error": "MT5_ATTACH_FAILED", "detail": mt5_last_error_text(mt5)}

    account = mt5.account_info()
    terminal = mt5.terminal_info()
    if account is None:
        mt5.shutdown()
        return {"success": False, "error": "ACCOUNT_NOT_LOGGED_IN"}

    return {
        "success": True,
        "data": {
            "account": to_plain(account),
            "terminal": to_plain(terminal),
        },
    }


def get_snapshot(mt5: Any) -> dict[str, Any]:
    account = mt5.account_info()
    if account is None:
        return {"success": False, "error": "ACCOUNT_NOT_LOGGED_IN"}
    positions = mt5.positions_get()
    orders = mt5.orders_get()
    if positions is None:
        positions = []
    if orders is None:
        orders = []
    return {
        "success": True,
        "data": {
            "account": to_plain(account),
            "terminal": to_plain(mt5.terminal_info()),
            "positions": to_plain(positions),
            "orders": to_plain(orders),
        },
    }


def handle_request(raw_line: str) -> dict[str, Any]:
    if len(raw_line.encode("utf-8")) > MAX_FRAME_BYTES:
        return make_response("", "unknown", False, error="FRAME_TOO_LARGE")
    try:
        req = json.loads(raw_line)
    except json.JSONDecodeError:
        return make_response("", "unknown", False, error="INVALID_JSON")

    if not isinstance(req, dict):
        return make_response("", "unknown", False, error="INVALID_SCHEMA")

    request_id = str(req.get("requestId", ""))
    command = str(req.get("command", ""))
    if command not in ALLOWED_COMMANDS:
        return make_response(request_id, command, False, error="COMMAND_NOT_ALLOWED")

    if command == "ping":
        return make_response(request_id, command, True, data={"status": "ok", "mode": "read-only"})

    if command == "disconnect":
        try:
            import MetaTrader5 as mt5  # type: ignore
            mt5.shutdown()
        except Exception:
            pass
        return make_response(request_id, command, True, data={"disconnected": True})

    try:
        import MetaTrader5 as mt5  # type: ignore
    except ImportError:
        return make_response(request_id, command, False, error="MT5_PYTHON_MODULE_NOT_INSTALLED")

    if command == "connect_attach":
        terminal_path = str(req.get("terminalPath", "")).strip()
        result = connect_attach(mt5, terminal_path)
        if not result["success"]:
            return make_response(
                request_id,
                command,
                False,
                error=result["error"],
                data={"detail": result.get("detail")},
            )
        return make_response(request_id, command, True, data=result["data"])

    if command == "get_snapshot":
        result = get_snapshot(mt5)
        if not result["success"]:
            return make_response(request_id, command, False, error=result["error"])
        return make_response(request_id, command, True, data=result["data"])

    if command == "get_history_deals":
        account = mt5.account_info()
        if account is None:
            return make_response(request_id, command, False, error="ACCOUNT_NOT_LOGGED_IN")
        from datetime import datetime, timedelta
        now = datetime.now()
        deals = mt5.history_deals_get(now - timedelta(days=30), now)
        if deals is None:
            deals = []
        return make_response(request_id, command, True, data={"deals": to_plain(deals)})


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        resp = handle_request(line)
        sys.stdout.write(json.dumps(resp, ensure_ascii=False) + "\n")
        sys.stdout.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
