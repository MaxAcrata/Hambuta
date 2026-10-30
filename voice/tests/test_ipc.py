import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest

from voice_control.commands import CommandKind
from voice_control.ipc import GameClient, IpcError


class _Handler(BaseHTTPRequestHandler):
    mode = "ok"  # ok | http400 | http500 | busy | garbage | refuse_command

    def log_message(self, fmt, *args):  # noqa: A003
        pass

    def do_GET(self):
        if self.path.startswith("/health"):
            body = json.dumps(
                {
                    "ok": True,
                    "version": "0.5.0",
                    "in_world": True,
                    "has_companion": True,
                    "move_status": "none",
                    "move_distance": 2.5,
                    "follow_target": "Player",
                    "distance_to_player": 2.5,
                    "last_error": "",
                }
            ).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_response(404)
        self.end_headers()

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length)
        try:
            data = json.loads(raw.decode() or "{}")
        except json.JSONDecodeError:
            data = {}
        cmd = data.get("cmd")

        if self.mode == "http500":
            self.send_response(500)
            self.end_headers()
            self.wfile.write(b'{"accepted":false,"status":"error","message":"boom"}')
            return
        if self.mode == "busy":
            body = json.dumps(
                {"accepted": False, "status": "rejected", "message": "another command is in flight"}
            ).encode()
            self.send_response(409)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if self.mode == "http400" or cmd not in ("FOLLOW_ME", "COME_HERE", "STOP", "STATUS"):
            body = json.dumps(
                {"accepted": False, "status": "rejected", "message": "bad cmd"}
            ).encode()
            self.send_response(400)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return

        body = json.dumps(
            {"accepted": True, "status": "succeeded", "message": "ok", "job_id": 7}
        ).encode()
        self.send_response(200)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


@pytest.fixture()
def server():
    httpd = HTTPServer(("127.0.0.1", 0), _Handler)
    port = httpd.server_address[1]
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    yield f"http://127.0.0.1:{port}", _Handler
    httpd.shutdown()


def test_send_command_ok(server):
    base, _ = server
    client = GameClient(base, timeout=2.0)
    reply = client.send_command(CommandKind.STATUS)
    assert reply.accepted is True
    assert reply.status == "succeeded"
    assert reply.job_id == 7


def test_send_only_enum_tokens(server):
    base, handler = server
    client = GameClient(base, timeout=2.0)
    for kind in CommandKind:
        reply = client.send_command(kind)
        assert reply.accepted is True


def test_http_400_rejected(server):
    base, handler = server
    handler.mode = "http400"
    client = GameClient(base, timeout=2.0)
    reply = client.send_command(CommandKind.STOP)
    assert reply.accepted is False
    assert reply.status == "rejected"


def test_http_409_busy(server):
    base, handler = server
    handler.mode = "busy"
    client = GameClient(base, timeout=2.0)
    reply = client.send_command(CommandKind.STOP)
    assert reply.accepted is False
    assert "flight" in reply.message or "rejected" in reply.status


def test_http_500_maps_to_reply(server):
    base, handler = server
    handler.mode = "http500"
    client = GameClient(base, timeout=2.0)
    reply = client.send_command(CommandKind.STOP)
    assert reply.accepted is False


def test_connection_refused():
    client = GameClient("http://127.0.0.1:1", timeout=0.5)
    with pytest.raises(IpcError) as exc:
        client.health()
    assert exc.value.kind in ("connection", "timeout")


def test_malformed_json(server):
    base, handler = server

    class _Bad(_Handler):
        def do_GET(self):
            self.send_response(200)
            self.send_header("Content-Length", "3")
            self.end_headers()
            self.wfile.write(b"{ab")

    # replace handler behaviour via monkeypatch on instance path is hard; use a raw call
    httpd = HTTPServer(("127.0.0.1", 0), _Bad)
    port = httpd.server_address[1]
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    try:
        client = GameClient(f"http://127.0.0.1:{port}", timeout=2.0)
        with pytest.raises(IpcError) as exc:
            client.health()
        assert exc.value.kind == "protocol"
    finally:
        httpd.shutdown()


def test_health_shape(server):
    base, _ = server
    client = GameClient(base, timeout=2.0)
    h = client.health()
    for key in (
        "ok",
        "in_world",
        "has_companion",
        "move_status",
        "move_distance",
        "follow_target",
        "distance_to_player",
    ):
        assert key in h
