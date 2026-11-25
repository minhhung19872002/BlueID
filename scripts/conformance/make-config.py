#!/usr/bin/env python3
"""Writes the OpenID conformance-suite configuration for BlueID (static clients, browser automation of the sign-in page).

Secrets come from deploy/.env.dev; the output goes to tests/interop/out/ (git-ignored).
Usage: python scripts/conformance/make-config.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def env():
    values = {}
    with open(os.path.join(ROOT, "deploy", ".env.dev"), encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                values[k] = v
    return values


def login_task(issuer, user, password, screenshot=False):
    commands = []
    if screenshot:
        # prompt=login / max_age tests ask for proof that the user was asked to sign in again.
        commands.append(["wait", "xpath", "//*", 10, "Sign in", "update-image-placeholder-optional"])
    commands += [
        ["text", "id", "UserName", user, "optional"],
        ["click", "id", "login-next", "optional"],
        ["wait", "id", "Password", 10],
        ["text", "id", "Password", password],
        ["click", "id", "login-submit"],
    ]
    return {"task": "Login", "optional": True, "match": issuer + "/login*", "commands": commands}


def authorize_flow(issuer, user, password, screenshot=False):
    return {
        "match": issuer + "/connect/authorize*",
        "tasks": [
            login_task(issuer, user, password, screenshot),
            {"task": "Verify Complete", "match": "*/test/*/callback*", "commands": [["wait", "id", "submission_complete", 10]]},
        ],
    }


def main():
    e = env()
    issuer = e["BID_ISSUER"].rstrip("/")
    user = "oidc.tester@conformance.test"
    password = e["BID_CONFORMANCE_USER_PASSWORD"]
    logout = {
        "match": issuer + "/connect/logout*",
        "tasks": [
            # No valid id_token_hint: BlueID shows its signed-out page instead of redirecting (screenshot for review).
            {"task": "Signed out page", "optional": True, "match": issuer + "/logout*",
             "commands": [["wait", "id", "signed-out", 10, "signed out", "update-image-placeholder-optional"]]},
            # Unregistered / modified post_logout_redirect_uri: BlueID's error page (screenshot for review).
            {"task": "Error page", "optional": True, "match": issuer + "/oauth-error*",
             "commands": [["wait", "id", "oauth-error-code", 10, "invalid_request", "update-image-placeholder-optional"]]},
            {"task": "Verify Complete", "optional": True, "match": "*/test/*/post*"},
        ],
    }
    config = {
        "alias": "blueid",
        "description": "BlueID (dev) - static clients, client_secret_basic",
        "server": {"discoveryUrl": issuer + "/.well-known/openid-configuration"},
        "client": {"client_id": "conformance-1", "client_secret": e["BID_CONFORMANCE_CLIENT1_SECRET"]},
        "client2": {"client_id": "conformance-2", "client_secret": e["BID_CONFORMANCE_CLIENT2_SECRET"]},
        "client_secret_post": {"client_id": "conformance-post", "client_secret": e["BID_CONFORMANCE_CLIENT1_SECRET"]},
        "browser": [authorize_flow(issuer, user, password), logout],
        "override": {
            **{
                name: {"browser": [authorize_flow(issuer, user, password, screenshot=True), logout]}
                for name in ("oidcc-prompt-login", "oidcc-max-age-1")
            },
            # Requests BlueID cannot answer to the client: it shows its error page (the suite takes a screenshot).
            **{
                name: {"browser": [{
                    "match": issuer + "/connect/authorize*",
                    "tasks": [{"task": "Error page", "match": issuer + "/oauth-error*",
                               "commands": [["wait", "id", "oauth-error-code", 10, "invalid_request", "update-image-placeholder"]]}],
                }]}
                for name in ("oidcc-response-type-missing", "oidcc-ensure-registered-redirect-uri", "oidcc-ensure-request-object-with-redirect-uri")
            },
        },
    }
    out = os.path.join(ROOT, "tests", "interop", "out")
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, "blueid-oidcc.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(config, f, indent=2)
    # Back-channel logout plan: the clients that register a back-channel logout URI.
    bcl = dict(config)
    bcl["client"] = {"client_id": "conformance-3", "client_secret": e["BID_CONFORMANCE_CLIENT1_SECRET"]}
    bcl["client2"] = {"client_id": "conformance-4", "client_secret": e["BID_CONFORMANCE_CLIENT2_SECRET"]}
    with open(os.path.join(out, "blueid-oidcc-bcl.json"), "w", encoding="utf-8") as f:
        json.dump(bcl, f, indent=2)
    print(path)


if __name__ == "__main__":
    main()
