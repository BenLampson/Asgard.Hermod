# Asgard.Hermod.WinTerminal

A Windows-only WPF terminal library maintained inside Hermod. It builds into one
`net10.0-windows` assembly with no third-party runtime package dependencies.

The initial VT engine and glyph renderer are adapted from
[VirtualTerminal](https://github.com/Rikitav/VirtualTerminal/tree/4fb7d8a3039be945db5d5dd92ab434a4e23a85a6).
The WPF control, session boundary, and local ConPTY host are implemented in this
repository. Source attribution and modifications are recorded in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Responsibilities

```text
Engine/                 Incremental UTF-8 / VT parsing, screen, history, input encoding
Rendering/              WPF glyph lookup and row rendering
Local/                  Windows ConPTY process and pipe lifecycle
TerminalSession.cs      Feed output, send input, report grid size
TerminalControl*.cs     WPF display, input, selection, scrolling, resizing
```

This assembly contains the terminal component. Connection profiles, Docker,
Kubernetes, SSH and FTP SDKs, authentication, and persistence belong to Hermod's
application and connector layers. It imports neither Avalonia nor upstream
transport packages.

## Host in WPF

Reference this project and add the control to a window:

```xml
<terminal:TerminalControl
    xmlns:terminal="clr-namespace:Asgard.Hermod.WinTerminal;assembly=Asgard.Hermod.WinTerminal"
    x:Name="Terminal"
    FontFamily="Consolas"
    FontSize="14"
    Background="#111827"
    Foreground="#E5E7EB" />
```

Create a session and a local connection from the host:

```csharp
using Asgard.Hermod.WinTerminal;
using Asgard.Hermod.WinTerminal.Local;

var session = new TerminalSession();
Terminal.Session = session;
var connection = ConPtyConnection.Start(
    session, "powershell.exe -NoLogo -NoProfile", Environment.CurrentDirectory);

// Observe completion to report process exit or a communication failure.
int exitCode = await connection.Completion;
```

Release in this order when the host closes. A control leaving the visual tree
pauses rendering and retains its session; `Unloaded` does not dispose it.

```csharp
Terminal.Session = null;
try
{
    await connection.DisposeAsync();
}
finally
{
    Terminal.Dispose();
    session.Dispose();
}
```

## Connect another byte stream

- Call `session.FeedOutput(bytes)` with raw UTF-8/VT data. Do not decode each read
  independently into a string: Unicode and escape sequences may cross reads.
- Subscribe to `InputReceived` and queue the supplied bytes to your connection.
  This includes both user input and terminal replies such as cursor queries.
  Callbacks must not perform blocking network I/O.
- Forward `SizeChanged` to the remote PTY. Dimensions are character cells.
- Detach handlers and stop the connector before disposing the session.

The control never runs a command or opens a connection by itself. Local input is
echoed only when the backend sends it back.

## Initial behavior and verification

The baseline includes ANSI colors, cursor and erase commands, primary/alternate
screens, bounded scrollback, CJK cell widths, keyboard/Meta input, committed IME
text, selection/copy/paste, mouse reports, and resizing. `Ctrl+C` copies an active
selection; otherwise it reaches the shell. `Ctrl+Shift+C` always copies.
`Ctrl+V` pastes, `Ctrl+Shift+A` selects all, and `Ctrl+wheel` changes font size.
Shift bypasses application mouse reporting for local selection.

Number-row and numeric-keypad digits use the current Windows keyboard layout at
the key event boundary, so they work even when an input method does not commit a
normal text event. Handled keys suppress the subsequent text event, avoiding
duplicate input. During IME composition, digits, Enter and arrow keys stay with
the candidate chooser; committed Unicode text is sent once. Focus loss or Escape
ends the composition guard. Disconnected/read-only sessions never send input.

ConPTY requires Windows 10 version 1809 or later. Inputs, outputs, and control-plane
teardown use separate workers. Startup explicitly resets inherited standard
handles so terminals also work when launched from a test runner or an IDE with
redirected output. See Microsoft's
[ConPTY lifecycle documentation](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
and [handle inheritance discussion](https://github.com/microsoft/terminal/discussions/15814).

The WPF startup application references this assembly; its product UI remains
under design. The terminal's current checks run through the separate test project:

```powershell
dotnet build Asgard.Hermod.slnx -c Release
dotnet test --solution Asgard.Hermod.slnx --configuration Release
dotnet format Asgard.Hermod.slnx --verify-no-changes
```

Tests cover byte-boundary parsing, Unicode, colors, alternate screen, scrollback,
input direction, queries, resizing, WPF rendering and reload, plus real cmd.exe and
PowerShell processes, their working directory, final output, exit codes, and cleanup.
The solution uses xUnit v3 and the .NET 10 Microsoft Testing Platform runner.

This is the starting implementation. Complex grapheme/emoji shaping, IME candidate
placement, accessibility, common full-screen application compatibility, and
sustained high-output performance still need dedicated verification.
