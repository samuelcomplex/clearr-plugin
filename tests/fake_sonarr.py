import json, http.server, sys
SERIES={"id":42,"title":"X","monitored":True,"tvdbId":123,"seasons":[{"seasonNumber":0,"monitored":False},{"seasonNumber":1,"monitored":True},{"seasonNumber":2,"monitored":True}]}
class H(http.server.BaseHTTPRequestHandler):
    def log_message(self,*a): pass
    def _ok(self,obj):
        b=json.dumps(obj).encode(); self.send_response(200); self.send_header("Content-Type","application/json"); self.end_headers(); self.wfile.write(b)
    def do_GET(self):
        if self.headers.get("X-Api-Key")!="k": self.send_response(401); self.end_headers(); return
        if self.path.startswith("/api/v3/series?tvdbId=123"): self._ok([SERIES])
        elif self.path.startswith("/api/v3/series?tvdbId="): self._ok([])
        elif self.path=="/api/v3/system/status": self._ok({"version":"4.0.15"})
        else: self.send_response(404); self.end_headers()
    def do_PUT(self):
        body=json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        open("/tmp/sonarr_put.json","a").write(self.path+" "+json.dumps(body)+"\n"); self._ok(body)
http.server.HTTPServer(("127.0.0.1",18989),H).serve_forever()
