# Third-party source notices

## VirtualTerminal

- Project: [Rikitav/VirtualTerminal](https://github.com/Rikitav/VirtualTerminal)
- Upstream authors as listed in its project metadata: Rikitav Tim4ik.
- Imported revision: `4fb7d8a3039be945db5d5dd92ab434a4e23a85a6`.
- License: Apache License 2.0. The upstream license is reproduced verbatim in
  [licenses/VirtualTerminal.LICENSE.txt](licenses/VirtualTerminal.LICENSE.txt).
- Derived files: `Engine/**/*.cs`, `Rendering/GlyphCache.cs`, and
  `Rendering/TerminalRenderer.cs`.

The selected source comes from `VirtualTerminal/` (buffer, enums, input, model,
options, VT parser, selection, decoder interface, and terminal decoder) and
`VirtualTerminal.WPF/Rendering/` (glyph cache and row renderer). Other upstream UI
frameworks, session implementations, console redirection helpers, platform
transports, demos, build settings, and package dependencies are excluded.

Hermod modifications include namespace changes, Chinese API comments, splitting
types and large classes into separate files, WPF-only compilation, null-argument
validation, correcting hexadecimal OSC color parsing, correcting legacy mouse
release/coordinate encoding, exposing newline completion to the session, and
defaulting Enter to CR. Each derived source file
contains a modification notice.

The new terminal control, byte-oriented session, local ConPTY lifecycle and tests
are maintained by Hermod separately from the upstream implementation. The Unicode
width tables retain the upstream attribution to public-domain Markus Kuhn wcwidth
data, approximately Unicode 15.1.
