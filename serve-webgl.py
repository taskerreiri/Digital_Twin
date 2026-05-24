import http.server
import os
import mimetypes

BUILD_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "webgl-build")

GZIP_CONTENT_TYPES = {
    ".js.gz": "application/javascript",
    ".wasm.gz": "application/wasm",
    ".data.gz": "application/octet-stream",
}

class WebGLHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/":
            path = "/index.html"

        file_path = os.path.normpath(os.path.join(BUILD_DIR, path.lstrip("/")))
        if not file_path.startswith(BUILD_DIR):
            self.send_error(403)
            return

        if not os.path.isfile(file_path):
            self.send_error(404)
            return

        try:
            with open(file_path, "rb") as f:
                data = f.read()
        except IOError:
            self.send_error(500)
            return

        self.send_response(200)

        is_gzip = False
        for suffix, ctype in GZIP_CONTENT_TYPES.items():
            if file_path.endswith(suffix):
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Encoding", "gzip")
                is_gzip = True
                break

        if not is_gzip:
            ctype, _ = mimetypes.guess_type(file_path)
            self.send_header("Content-Type", ctype or "application/octet-stream")

        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        pass

if __name__ == "__main__":
    print(f"Serving WebGL from {BUILD_DIR} on http://localhost:8765")
    server = http.server.HTTPServer(("", 8765), WebGLHandler)
    server.serve_forever()
