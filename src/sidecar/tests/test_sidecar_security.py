import ast
from pathlib import Path
from tradepulse_sidecar.mt5_readonly_gateway import handle_request

FORBIDDEN_TOKENS = {
    "order_send",
    "order_check",
    "order_modify",
    "order_delete",
    "subprocess",
    "eval",
    "exec",
}


def test_static_ast_forbids_trading_and_shell_apis():
    src_dir = Path(__file__).resolve().parents[1] / "tradepulse_sidecar"
    for py_file in src_dir.rglob("*.py"):
        tree = ast.parse(py_file.read_text(encoding="utf-8"))
        for node in ast.walk(tree):
            if isinstance(node, ast.Name):
                assert node.id not in FORBIDDEN_TOKENS, f"Forbidden token {node.id} in {py_file}"
            elif isinstance(node, ast.Attribute):
                assert node.attr not in FORBIDDEN_TOKENS, f"Forbidden attr {node.attr} in {py_file}"


def test_unknown_command_rejected():
    resp = handle_request('{"requestId":"1","command":"place_order"}')
    assert resp["success"] is False
    assert resp["error"] == "COMMAND_NOT_ALLOWED"


def test_invalid_json_rejected():
    resp = handle_request("{broken-json")
    assert resp["success"] is False
    assert resp["error"] == "INVALID_JSON"
