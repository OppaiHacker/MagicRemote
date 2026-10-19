#!/bin/sh
# Release notes for tag $1: every commit since the previous tag, subject as a heading, body unwrapped into paragraphs.
set -e
prev=$(git describe --tags --abbrev=0 "$1^" 2>/dev/null || true)
git log --reverse --format='### %s%n%n%b%n' "${prev:+$prev..}$1" |
  grep -v '^Co-Authored-By:' |
  awk 'NF == 0 { if (p != "") print p; print ""; p = ""; next }
       /^(#|- |\* )/ { if (p != "") print p; p = $0; next }
       { p = p == "" ? $0 : p " " $0 }
       END { if (p != "") print p }' |
  cat -s
