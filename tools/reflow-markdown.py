"""Unwraps hard-wrapped prose in a markdown file, leaving the editor to break lines.

Everything whose line breaks mean something is left exactly as it is: fenced code, tables,
headings, rules, and the start of each list item. Only continuation lines - the ones that exist
solely because a paragraph was wrapped at a column - are joined back onto what they continue.
"""
import io
import re
import sys

LIST = re.compile(r"^\s*(?:[-*+]|\d+\.)\s")
RULE = re.compile(r"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$")

path = sys.argv[1]
text = io.open(path, encoding="utf-8", newline="").read()
nl = "\r\n" if text.count("\r\n") > text.count("\n") / 2 else "\n"
lines = text.replace("\r\n", "\n").split("\n")

out = []
held = None
fenced = False


def flush():
    global held
    if held is not None:
        out.append(held)
        held = None


for line in lines:
    stripped = line.strip()

    if stripped.startswith("```"):
        flush()
        out.append(line)
        fenced = not fenced
        continue

    if fenced:
        out.append(line)
        continue

    if stripped == "":
        flush()
        out.append("")
        continue

    # A table row, a heading or a rule is a line because it is a line.
    if stripped.startswith("|") or stripped.startswith("#") or RULE.match(line):
        flush()
        out.append(line)
        continue

    if LIST.match(line):
        flush()
        held = line.rstrip()
        continue

    if held is None:
        held = line.rstrip()
    else:
        held = held + " " + stripped

flush()

while out and out[-1] == "":
    out.pop()

io.open(path, "w", encoding="utf-8", newline="").write(nl.join(out) + nl)
print("reflowed", path.rsplit("\\", 1)[-1])
