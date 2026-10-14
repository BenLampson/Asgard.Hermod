# Design source

`Hermod.pen` is the single editable Pencil source. Keep related screens in the
numbered regions and save edits to this file instead of creating alternate `.pen`
copies. Screenshots, previews, and rendered exports are local review artifacts;
Git ignores image files under `design/`, including `previews/` and `exports/`.
Attach screenshots to pull requests instead of committing them. Runtime brand
and provider assets remain under `src/Asgard.Hermod.UI/Assets/`; the brand export
script is versioned separately.

The canvas directory groups the designs into these regions:

1. Design system and reusable components
2. Home and application shell
3. Account and sign-in
4. Instance connection wizard
5. Object storage browser and operations
6. Settings, tags, file-opening rules and temporary storage
7. Brand assets and alternative concepts
8. SSH connection flows, workspace states and refinements (08 / 08B / 08C)
9. Redis connection flows and workspace states (09 / 09B)
10. MySQL connection flows, TLS and SSH routing, test/save states and bilingual
    handoff contracts (10), plus database/table browsing and SQL workspaces (10B)

Region 05A contains storage connection flows. Region 04B contains shared layout
components and their implementation contract. Storage, SSH and Redis reference
the same `Instance Wizard`, `Wizard Body`, `Wizard Navigation` and `Wizard Actions`
components. Step labels, counts, progress and enabled actions belong to the type;
the protocol form belongs in the content slot. The five displayed steps are
examples, not a fixed requirement of the common container.

Region 10 reuses the same connection components for MySQL. It covers the type
entry, direct TCP and SSH routes, optional default database, verified TLS and
client certificates, connection tests, local/account save choices and final tags.
Its light, dark and English examples include recoverable failures and guarded
saving. SQL browsing and editing workspaces are outside this connection design.

Region 10B contains the connected MySQL workspace. Its reusable object tree
groups databases, object categories, tables and their columns, indexes, foreign
keys and triggers. Object tabs preserve per-object view state, while SQL tabs
retain their own execution database, draft and result sets. The workspace reuses
App Shell, the shared instance list, field/action controls, session tools and
child windows. Screens cover table data and metadata, SQL execution and result
limits, lazy tree loading/search, recoverable errors, unsaved drafts, English dark
mode and a compact object-tree drawer. Interaction and localization contracts
accompany the editable designs.

SSH and Redis workspaces inherit `App Shell` title bars, instance navigation,
document tabs, theme controls and collapsed rails. Their instance lists reference
the shared navigation list and `HfFtU` rows. Features own only their content slot.
Do not replace the whole sidebar, tab strip, wizard navigation or action bar with
a new frame in each state. A nominal `ref` with complete layout replacements is
still a separate layout and prevents changes from propagating.

When overriding an already replaced slot inside a nested instance, target the
actual replacement node ID returned by Pencil. Inspect the resolved instance and
verify its screen after changing references; stale override paths can be discarded.

Region 08C covers SSH configuration editing, terminal numeric input and the
extensible session tools entry. Editing reuses the shared wizard, preserves the
current session and applies saved changes to the next connection.
Configuration editing has one entry in the Content toolbar. Connection details,
session menus and the shared instance list do not add duplicate edit entries. The tools
popover has a shared list slot, filtering, light/dark examples and a compact
toolbar state. Terminal focus and keyboard rules are design acceptance criteria;
the matching runtime behavior and regression tests live in WinTerminal and Ssh.UI.

Region backgrounds and numbered screen names aid navigation. Screen contents,
including the object-search help added on `dev`, are preserved when arranging
the canvas. Because `.pen` is an encrypted document, resolve concurrent changes
through Pencil at the screen/node level; do not apply textual Git conflict edits.
