"""Copies the patch notes from the website's downloads.html into patchnotes.txt.

Usage: python tools/import_patchnotes.py [path to downloads.html]

Only reads the website, never changes it. After this one-time import you can
just edit patchnotes.txt directly (newest version at the top).
"""
import html
import os
import re
import sys
from html.parser import HTMLParser

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEFAULT_HTML = os.path.join(os.path.dirname(ROOT), "PokemonCopper", "downloads.html")


class NotesParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.lines = []
        self.details_depth = 0      # nesting of <details> while inside the patch notes one
        self.in_summary = False
        self.summary_text = ""
        self.active = False
        self.buf = None             # text being collected
        self.kind = None            # "h3", "h4", "li", "p"
        self.ul_depth = 0
        self.li_stack = []          # for each open <li>: has its line been written yet?

    def handle_starttag(self, tag, attrs):
        if tag == "details":
            if self.active:
                self.details_depth += 1
            return
        if tag == "summary":
            self.in_summary = True
            self.summary_text = ""
            return
        if not self.active:
            return
        if tag in ("h3", "h4", "p"):
            self.buf, self.kind = "", tag
        elif tag == "ul":
            if self.li_stack and not self.li_stack[-1]:
                self.flush_li()
            self.ul_depth += 1
        elif tag == "li":
            self.li_stack.append(False)
            self.buf, self.kind = "", "li"
        elif tag == "br" and self.buf is not None:
            self.buf += " "

    def handle_endtag(self, tag):
        if tag == "summary":
            self.in_summary = False
            if self.summary_text.strip().lower() == "patch notes":
                self.active = True
                self.details_depth = 1
            return
        if not self.active:
            return
        if tag == "details":
            self.details_depth -= 1
            if self.details_depth == 0:
                self.active = False
        elif tag in ("h3", "h4", "p") and self.kind == tag:
            text = clean(self.buf)
            if text:
                if tag == "h3":
                    if self.lines:
                        self.lines.append("")
                    self.lines.append("# " + text)
                elif tag == "h4":
                    self.lines.append("## " + text)
                else:
                    self.lines.append(text)
            self.buf = self.kind = None
        elif tag == "ul":
            self.ul_depth -= 1
        elif tag == "li":
            if self.li_stack and not self.li_stack[-1]:
                self.flush_li()
            if self.li_stack:
                self.li_stack.pop()

    def flush_li(self):
        text = clean(self.buf or "")
        if text:
            self.lines.append("  " * max(0, self.ul_depth - 1) + "- " + text)
        self.li_stack[-1] = True
        self.buf = self.kind = None

    def handle_data(self, data):
        if self.in_summary:
            self.summary_text += data
        elif self.active and self.buf is not None:
            self.buf += data


def clean(s):
    return re.sub(r"\s+", " ", html.unescape(s)).strip()


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_HTML
    p = NotesParser()
    p.feed(open(src, encoding="utf-8").read())
    if not p.lines:
        sys.exit("Couldn't find a <details> section titled 'Patch Notes' in " + src)
    out = os.path.join(ROOT, "patchnotes.txt")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(p.lines) + "\n")
    versions = sum(1 for l in p.lines if l.startswith("# "))
    print("Wrote %s (%d versions, %d lines)" % (out, versions, len(p.lines)))


if __name__ == "__main__":
    main()
