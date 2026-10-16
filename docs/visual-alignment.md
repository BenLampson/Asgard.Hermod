# Visual alignment checks

Checked on 2026-10-16 against the current Pencil sources. Measurements below are
logical device-independent pixels (DIP). Runtime-generated content is taken from
explicit test fixtures; clocks, source names, counts and server results are not
hard-coded into the application to reproduce a screenshot.

## Reference hierarchy

The current shared SQL workspace (`DBWindow.pen`, `oMvco`, `iNOq7`) uses a 44-DIP
caption, 36-DIP session strip, 36-DIP document strip and a 232-DIP explorer with a
6-DIP resize divider. Older object frames (`I0WqLy`, `bwAiE`) use a 72-DIP caption
and 240-DIP explorer. The implementation follows the current shared chrome while
using those object frames' body measurements. This keeps chrome stable when
switching between scripts and objects. The legacy frames cannot also be matched
at the same absolute body Y-coordinate.

The editable `.pen` files remain owned by the design workflow. Their pending
changes, including `AIChat.pen` and `RabbitMQ.pen`, were not staged or rewritten.
RabbitMQ is not part of this implementation's acceptance scope.

## Verified surfaces

| Surface | Reference measurements and behavior | Verification |
| --- | --- | --- |
| Workspace caption | 44 high; engine icon 22; name 13; endpoint 11; environment badge 18; controls 36x44 with 16/13/17 icons | `WorkspaceCaptionHeadlessTests`, `WorkspaceHeaderModulesHeadlessTests`, `FourKReadingHeadlessTests` |
| Main connection feedback | 520-wide centered card; radius 8; padding 26; title 18; actions 34; cancellation and retry bound to the original request | `MainConnectionFeedbackHeadlessTests`, `WorkspaceStartupHeadlessTests` |
| SQL workspace | 48 toolbar; 268 initial editor; 24 code line; 6 resize handle; 40 output tabs; single output slot | `DatabaseDesignAlignmentHeadlessTests`, module interaction tests |
| SQL errors | Message table 36 header / 40 rows; time/status/position/message; safe errors and explicit editor location; old locations disabled after editing | `ErrorLocationTests`, both modules' `SqlMessagesHeadlessTests` |
| Execution history | 42 context bar; 360 list; 62 rows; 38 detail tabs; restoring SQL preserves confirmation boundaries | `DatabaseHistoryHeadlessTests` |
| Object data | 58 identity; 34 section navigation; 52 toolbar; 40 footer; data header 36 / rows 42; scroll-synchronized headers and cells | Both modules' `ObjectPresentationHeadlessTests` |
| Object structure | 36 header / 44 rows; columns 210/210/110/250, trailing column at least 140; native inner horizontal scroll; selected-field detail card | `DatabaseColumnReaderHeadlessTests`, both modules' `ColumnMetadataLayoutHeadlessTests` |
| Object state | Failed paging retains successful range, size and row numbers; retries do not skip pages; metadata refresh keeps data and drafts | Both modules' `ObjectPresentationHeadlessTests` |
| SSH console and tools | 52 tool rail; 38 console strip; files 320, forwards 480, transfers 360; resizable tool area; independent consoles | `SshPencilAlignmentHeadlessTests`, `SshConsoleLifetimeHeadlessTests` |
| SSH monitor | Bottom monitor uses the shared split; overview, processes, Docker, network and disks retain terminal state | `SshPencilAlignmentHeadlessTests`, `SshTroubleshootingWorkspaceTests` |
| Log reader | 48 caption / 40 toolbar / 24 footer; search 280; gutter 52 + gap 16; line 23; code 13 / gutter 11; snapshot and failure states | `LogReaderHeadlessTests`, `SshContainerLogTests`, `FourKReadingHeadlessTests` |
| Log selection | Native original range; visible inactive search match; complete-row tint includes gutter; semantic warning/error glyphs remain readable | Pixel assertions in `FourKReadingHeadlessTests`, native glyph overlap checks in `LogReaderHeadlessTests` |
| Agent replies | Shared reading width; user 15 / assistant 16 / tools 12; complete-answer copy; restore accepted question; local mutually exclusive feedback | `AgentPencilHeadlessTests`, `AgentMessageCopyHeadlessTests`, `AgentPanelTests` |
| Agent tools | 32-high compact summary; consecutive ordinary tools grouped; approval, error and recovery messages stay independent; archive preserves expansion | `AgentActivityGroupHeadlessTests`, `AgentApprovalHeadlessTests` |
| Agent Markdown | Paragraph wrapping; independent horizontal code/table scrolling; actual read-only selection; no active model HTML, navigation or executable UI | `MarkdownViewTests`, `MarkdownOverflowHeadlessTests` |
| Model profiles | Named profiles, per-source selection, settings editor, unsaved input and missing-profile states | `AgentModelPencilHeadlessTests`, `AgentModelEditorHeadlessTests`, `AgentModelCatalogHeadlessTests` |

## Scaling and evidence

The matrix covers English and Simplified Chinese, light and dark themes, and
physical 3840x2160 at 100% and 200%. A 200% case is laid out at 1920x1080 DIP and
propagates `VisualTreeHelper.SetRootDpi(..., new(2, 2))` through actual visual
subtrees before rendering; it is not just an upscaled 96-DPI image. Narrow
object pages are also checked at 520 DIP and Agent pages at 640 DIP.

Set `HERMOD_OFFSCREEN_OUTPUT` to an ignored local directory to reproduce images.
The current output is under `artifacts/visual-fourk/`, with additional focused
captures under `artifacts/visual-sql-messages/` and `artifacts/visual-agent-groups/`.
Generated captures and design exports are not committed.

## Final scoped checks

The final selected suites passed 187 shared UI cases, 73 MySQL UI cases and 47
PostgreSQL UI cases. The unchanged protocol suites passed 83 MySQL and 58
PostgreSQL cases, excluding live integration. These are 448 cases across the
explicitly named suites, not a claim about all repository tests. The Release
solution build completed with zero errors and zero warnings. Full formatting
verification completed successfully.

## Validation boundaries

Release solution builds and full `dotnet format --verify-no-changes --no-restore`
are required alongside the explicitly selected headless and protocol test classes.
These checks do not certify DWM window frames, actual keyboard focus, physical
monitor movement or a complete interactive Windows acceptance run. No Window or
PresentationSource is created by the final offscreen suite.

The MySQL and PostgreSQL live-test ports are not configured. Protocol position
mapping, metadata mapping and UI behavior are tested with deterministic inputs;
this run does not claim real-server integration or a deployed cloud model-profile
schema update.

Three legacy `ConnectionFeedbackTests` were accidentally matched by an earlier
broad test-name filter and failed assertions about removed popups/status badges.
They are not counted as passing. Their current behavior is covered by
`MainConnectionFeedbackHeadlessTests`, `WorkspaceStartupHeadlessTests` and
`ConnectionLifecycleHeadlessTests`; old native-window tests were excluded from
the final explicitly named suite. The whole repository test suite is not claimed
to be green.
