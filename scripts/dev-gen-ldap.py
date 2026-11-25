#!/usr/bin/env python3
"""Generate the OpenLDAP seed for the dev "customer directory" (doitac.test).

Deterministic (fixed seed) so that re-running BlueID Connect against it can be checked for duplicates.
Writes deploy/dev/openldap/generated/50-seed.ldif (git-ignored).

Usage: python scripts/dev-gen-ldap.py [user_count]   (default: BID_LDAP_USERS or 10000)
"""
import os
import random
import sys
import unicodedata

GIVEN = ["An", "Bình", "Chi", "Dũng", "Hà", "Khánh", "Lan", "Minh", "Ngọc", "Phúc", "Quân", "Thảo", "Trang", "Tuấn",
         "María", "José", "Lucía", "Søren", "Ingrid", "Björn", "Kenji", "Yuki", "Haruto", "Mei", "Wei", "Jia",
         "Aarav", "Priya", "Olusegun", "Amara", "Fatima", "Omar", "Zoë", "Chloé", "François", "Łukasz", "Agnieszka",
         "Dmitri", "Olga", "Emma", "Liam", "Noah", "Ava", "Mateo", "Valentina", "Ahmet", "Elif", "Nguyệt", "Sơn"]
FAMILY = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Võ", "Đặng", "Bùi", "García", "Martínez", "Jensen", "Lindqvist",
          "Sato", "Tanaka", "Wang", "Li", "Sharma", "Patel", "Okafor", "Haddad", "Müller", "Dubois", "Kowalski",
          "Ivanova", "Smith", "Johnson", "Rossi", "Yılmaz", "O'Brien", "Silva"]
DEPARTMENTS = ["Kế toán", "Sales", "Engineering", "Human Resources", "Marketing", "Operations", "Legal", "Support"]


def ascii_slug(text: str) -> str:
    text = text.replace("Đ", "D").replace("đ", "d").replace("Ł", "L").replace("ł", "l").replace("ø", "o").replace("Ø", "O")
    norm = unicodedata.normalize("NFKD", text)
    return "".join(c for c in norm if c.isascii() and (c.isalnum())).lower()


def ldif_value(attr: str, value: str) -> str:
    # Non-ASCII values must be base64-encoded in LDIF (RFC 2849).
    if value.isascii() and not value.startswith((" ", ":", "<")):
        return f"{attr}: {value}"
    import base64
    return f"{attr}:: {base64.b64encode(value.encode('utf-8')).decode('ascii')}"


def main() -> None:
    count = int(sys.argv[1]) if len(sys.argv) > 1 else int(os.environ.get("BID_LDAP_USERS", "10000"))
    rnd = random.Random(20261125)
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "deploy", "dev", "openldap", "generated")
    os.makedirs(out_dir, exist_ok=True)
    base = "dc=doitac,dc=test"
    lines = [f"dn: ou=people,{base}", "objectClass: organizationalUnit", "ou: people", "",
             f"dn: ou=groups,{base}", "objectClass: organizationalUnit", "ou: groups", ""]
    members: dict[str, list[str]] = {d: [] for d in DEPARTMENTS}
    used = set()
    for i in range(count):
        given = rnd.choice(GIVEN)
        family = rnd.choice(FAMILY)
        uid = f"{ascii_slug(given)}.{ascii_slug(family)}"
        if uid in used:
            uid = f"{uid}{i}"
        used.add(uid)
        dept = DEPARTMENTS[i % len(DEPARTMENTS)]
        dn = f"uid={uid},ou=people,{base}"
        members[dept].append(dn)
        lines += [f"dn: {dn}", "objectClass: inetOrgPerson", f"uid: {uid}",
                  ldif_value("cn", f"{given} {family}"), ldif_value("givenName", given), ldif_value("sn", family),
                  ldif_value("displayName", f"{given} {family}"), f"mail: {uid}@doitac.test",
                  f"employeeNumber: E{i + 1:06d}", ldif_value("departmentNumber", dept),
                  ldif_value("title", "Staff"), "userPassword: Ldap-Dev-Pass-1", ""]
    for idx, (dept, dns) in enumerate(members.items()):
        if not dns:
            continue
        cn = f"dept-{ascii_slug(dept)}"
        lines += [f"dn: cn={cn},ou=groups,{base}", "objectClass: groupOfNames", f"cn: {cn}", ldif_value("description", dept)]
        lines += [f"member: {m}" for m in dns]
        lines.append("")
    path = os.path.join(out_dir, "50-seed.ldif")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"wrote {path}: {count} users, {len(DEPARTMENTS)} groups")


if __name__ == "__main__":
    main()
