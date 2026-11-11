"""Serve Builds/Site locally the way Netlify does: /play and /privacy are play.html and privacy.html, and /play/
goes to /play.  F:/Blender/5.2/python/bin/python.exe -I Tools/serve_site.py [port] [folder]"""
import http.server, os, sys

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8020
DIR = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "Builds", "Site")


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=DIR, **k)

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/play/":
            self.send_response(301); self.send_header("Location", "/play"); self.end_headers(); return
        if path != "/" and "." not in os.path.basename(path) and os.path.isfile(os.path.join(DIR, path.strip("/") + ".html")):
            self.path = path + ".html" + self.path[len(path):]
        return super().do_GET()


http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
