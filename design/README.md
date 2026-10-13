# Design source

`Hermod.pen` is the single editable Pencil source. Keep related screens in the
numbered regions and save edits to this file instead of creating alternate `.pen`
copies. Exported brand assets and the export script are separate project assets.

The canvas directory points to seven regions:

1. Design system and reusable components
2. Home and application shell
3. Account and sign-in
4. Instance connection wizard
5. Object storage browser and operations
6. Settings, tags, file-opening rules and temporary storage
7. Brand assets and alternative concepts

Region backgrounds and numbered screen names aid navigation. Screen contents,
including the object-search help added on `dev`, are preserved when arranging
the canvas. Because `.pen` is an encrypted document, resolve concurrent changes
through Pencil at the screen/node level; do not apply textual Git conflict edits.
