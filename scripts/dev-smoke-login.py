#!/usr/bin/env python3
"""Smoke check of password sign-in against host-run dev processes (Web 5090, Api 5091).

Signs in a sample user through the Razor pages (anti-forgery token, two steps), calls /me/v1 with the session cookie,
checks a wrong password gets the generic message, and that another tenant's user id answers 404.
Usage: python scripts/dev-smoke-login.py [base-url]
  no argument: host-run dev processes; with a base URL (e.g. https://localhost:58443): one origin through Caddy,
  the way a deployment is checked (lesson 13). TLS is not verified (internal CA).
"""
import http.cookiejar
import json
import os
import re
import ssl
import sys
import urllib.parse
import urllib.request

WEB = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://localhost:5090"
API = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://localhost:5091"
TLS = ssl._create_unverified_context()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def session():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect(), urllib.request.HTTPSHandler(context=TLS)), jar


class Answer:
    def __init__(self, status, location, body):
        self.status, self.location, self.body = status, location, body

    def geturl(self):
        return self.location

    def read(self):
        return self.body


def form_post(opener, url, fields):
    data = urllib.parse.urlencode(fields).encode()
    try:
        r = opener.open(urllib.request.Request(url, data=data, method="POST"), timeout=20)
        return Answer(r.status, None, r.read())
    except urllib.error.HTTPError as e:
        if e.code in (301, 302, 303):
            return Answer(e.code, e.headers["Location"], b"")
        raise


def token(html):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html).group(1)


def sign_in(user, password):
    op, jar = session()
    html = op.open(WEB + "/login?ui_locales=en", timeout=20).read().decode()
    html = form_post(op, WEB + "/login?handler=Name&ui_locales=en", {"UserName": user, "__RequestVerificationToken": token(html)}).read().decode()
    resp = form_post(op, WEB + "/login?handler=Password&ui_locales=en",
                     {"UserName": user, "Password": password, "__RequestVerificationToken": token(html)})
    return op, jar, resp.geturl(), resp.read().decode()


def api(op, path, method="GET", body=None):
    req = urllib.request.Request(API + path, method=method, data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", "X-BlueID-CSRF": "1"})
    try:
        r = op.open(req, timeout=20)
        return r.status, json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"null")


fail = False
def check(ok, label):
    global fail
    print(("ok   " if ok else "FAIL ") + label)
    fail |= not ok


pw = "Sample-User-Pass-2026"
op, jar, url, html = sign_in("admin@acme.test", pw)
check(url == "/portal/", f"sign-in redirects to the portal ({url})")
check(any(c.name.endswith("bid_session") for c in jar), "acme admin gets a session cookie")
st, me = api(op, "/me/v1")
check(st == 200 and me["data"]["user"]["userPrincipalName"] == "admin@acme.test", f"/me/v1 answers for the session ({st})")
check(me["message"] is None and me["success"] is True, "envelope {success,data,message,errors}")
st, users = api(op, "/admin/v1/users?top=5&count=true")
check(st == 200 and users["data"]["count"] == 45 and len(users["data"]["items"]) == 5 and users["data"]["nextCursor"], "admin lists own tenant only (45), paged")

op2, _, _, _ = sign_in("admin@bluemail.test", pw)
st, other = api(op2, "/admin/v1/users?top=1")
other_id = other["data"]["items"][0]["id"]
st, _ = api(op, f"/admin/v1/users/{other_id}")
check(st == 404, f"user of another tenant is invisible ({st})")
st, body = api(op, "/admin/v1/platform/tenants")
check(st == 403, f"customer admin cannot list platform tenants ({st})")

_, jar3, _, html3 = sign_in("admin@acme.test", "wrong-password-123")
check("incorrect" in html3 and not any(c.name.endswith("bid_session") for c in jar3), "wrong password: generic message, no cookie")
_, _, _, html4 = sign_in("nobody@acme.test", "wrong-password-123")
check("incorrect" in html4, "unknown user: same generic message")

req = urllib.request.Request(API + "/me/v1/settings", method="PATCH", data=b'{"hourCycle":"h12"}', headers={"Content-Type": "application/json"})
try:
    op.open(req, timeout=20)
    check(False, "PATCH without CSRF header refused")
except urllib.error.HTTPError as e:
    check(e.code == 403, f"PATCH without CSRF header refused ({e.code})")

print("SMOKE: " + ("FAIL" if fail else "PASS"))
sys.exit(1 if fail else 0)
