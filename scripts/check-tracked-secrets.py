"""Check tracked filenames and common secret formats, without printing matches."""
import pathlib
import re
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
files = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode("utf-8").split("\0")
signatures = re.compile(
    rb"-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----"
    rb"|\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b"
    rb"|\bgithub_pat_[A-Za-z0-9_]{40,}\b"
)
findings = []
for name in filter(None, files):
    path = root / name
    if path.suffix.lower() in {".pfx", ".p12", ".key", ".dpapi", ".secret"}:
        findings.append(name)
        continue
    if path.is_file() and not path.is_symlink() and signatures.search(path.read_bytes()):
        findings.append(name)
if findings:
    print("Review credential-like content in: " + ", ".join(findings))
    sys.exit(1)
print("Tracked secret material check passed")
