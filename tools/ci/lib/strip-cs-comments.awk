# Shared C# comment stripper for the tools/ci guards (one copy; load with `awk -f`).
# strip(s) returns line s without comments: `//` and `///` to end of line, `/* ... */` blocks
# (also across lines, via the global `inblock`). `//` and `/*` inside plain "..." / @"..."
# strings (verbatim state carries over lines via the global `verbatim`) and '...' chars do not
# start a comment; string and char literals are copied through unchanged.
# The caller must reset `inblock = 0; verbatim = 0` at the start of every file (FNR == 1).
#
# Limitation (review-enforced, TD-103): raw """...""" strings and string literals inside $"{...}"
# interpolation holes are not lexed, so a `//` or `/*` in them can open a fake comment and hide
# real code (to the end of the line, or for `/*` until a later `*/`).

# Returns the line without comments. Block-comment and verbatim-string state carry over lines.
function strip(s,   out, i, n, c, nx, j) {
  out = ""; n = length(s); i = 1
  while (i <= n) {
    c = substr(s, i, 1); nx = substr(s, i + 1, 1)
    if (inblock) {
      if (c == "*" && nx == "/") { inblock = 0; out = out " "; i += 2 } else i++
    } else if (verbatim) {
      out = out c
      if (c == "\"" && nx == "\"") { out = out nx; i += 2 } else { if (c == "\"") verbatim = 0; i++ }
    } else if (c == "/" && nx == "/") {
      break
    } else if (c == "/" && nx == "*") {
      inblock = 1; i += 2
    } else if (c == "@" && nx == "\"") {
      verbatim = 1; out = out c nx; i += 2
    } else if (c == "\"" || c == "\047") {
      # Regular string or char literal: copy through the closing quote, honouring \ escapes.
      for (j = i + 1; j <= n; j++) {
        if (substr(s, j, 1) == "\\") j++
        else if (substr(s, j, 1) == c) break
      }
      out = out substr(s, i, j - i + 1); i = j + 1
    } else {
      out = out c; i++
    }
  }
  return out
}
