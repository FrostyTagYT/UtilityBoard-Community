# Build-time leak check for the Community Edition. Scans EVERY file in the staged Community folder (DLLs, EXEs, json, txt...)
# for personal strings as ASCII/UTF-8 and UTF-16LE (how .NET stores string literals), case-insensitive.
# Also scans for secret-looking patterns (stream keys, tokens, passwords) and absolute user paths.
# Exit 0 = clean, 1 = leak found (the build/stage script stops).
import os, re, sys
root = sys.argv[1] if len(sys.argv) > 1 else sys.exit("usage: leakcheck.py <dir>")
# Private terms are read from tools/leakterms.txt (one per line, # comments). That file is git-ignored;
# see tools/leakterms.example.txt.
_tf = os.path.join(os.path.dirname(os.path.abspath(__file__)), "leakterms.txt")
if not os.path.exists(_tf): sys.exit("leakcheck: missing %s (copy leakterms.example.txt and add your own private terms)" % _tf)
PERSONAL = [l.strip() for l in open(_tf, encoding="utf-8") if l.strip() and not l.strip().startswith("#")]
SECRETS = [rb"live_[0-9]{6,}_[A-Za-z0-9]{10,}",          # Twitch/TikTok style stream keys
           rb"rtmps?://[^\s\"']+/[A-Za-z0-9_\-]{16,}",    # RTMP URL with key
           rb"sk_live_[A-Za-z0-9]{10,}", rb"sessionid=[A-Za-z0-9]{10,}", rb"\"server_password\"\s*:\s*\"[^\"]+\"",
           ]
# absolute user paths: flagged in OUR files; in third-party DLLs only if it is a path of THIS PC's user (their own build paths are not ours)
USERPATH = rb"[A-Za-z]:\\{1,2}Users\\{1,2}([A-Za-z0-9_.-]+)"
OURS = ("UtilityBoardCommunity.dll", "UtilityBoardCompanion.dll", "UtilityBoardCompanion.exe", "UtilityBoardCompanion.deps.json", "UtilityBoardCompanion.runtimeconfig.json")
THIRD_PARTY_OK = {b"markh"}   # NAudio's author build path embedded in the NuGet NAudio*.dll files
def variants(s):
    b = s.encode(); return [b, s.encode("utf-16-le")]
hits = []; files = 0; notes = set()
for dp, dn, fn in os.walk(root):
    for f in fn:
        p = os.path.join(dp, f); files += 1
        rel = os.path.relpath(p, root)
        for word in PERSONAL:
            if word.lower() in rel.lower(): hits.append((rel, "filename contains '%s'" % word))
        data = open(p, "rb").read(); low = data.lower()
        for word in PERSONAL:
            for v in variants(word.lower()):
                i = low.find(v)
                if i >= 0: hits.append((rel, "'%s' at byte %d (%s)" % (word, i, "utf16" if len(v) > len(word) else "ascii")))
        u16 = data.decode("utf-16-le", "ignore").encode("ascii", "ignore")
        for pat in SECRETS:
            for blob, kind in ((data, "ascii"), (u16, "utf16")):
                m = re.search(pat, blob)
                if m: hits.append((rel, "secret/path pattern %r -> %r (%s)" % (pat.decode(), m.group(0)[:40], kind)))
        for blob, kind in ((data, "ascii"), (u16, "utf16")):
            for m in re.finditer(USERPATH, blob):
                user = m.group(1)
                if f in OURS or not (user in THIRD_PARTY_OK): hits.append((rel, "user path %r (%s)" % (m.group(0)[:40], kind)))
                else: notes.add("%s: third-party build path %r (NAudio author, allowlisted)" % (rel, m.group(0)[:40].decode(errors="ignore")))
print("leakcheck: scanned %d files in %s" % (files, root))
print("leakcheck: %d personal terms + %d secret/path patterns, ascii + utf-16le, case-insensitive" % (len(PERSONAL), len(SECRETS)))
for n in sorted(notes): print("note", n)
for h in sorted(set(hits)): print("LEAK", h[0], "-", h[1])
print("LEAKCHECK " + ("FAIL (%d)" % len(set(hits)) if hits else "PASS (0 hits)"))
sys.exit(1 if hits else 0)
