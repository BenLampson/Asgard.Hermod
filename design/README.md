# Design source

`Hermod.pen` is the single editable Pencil source. Keep related screens in the
numbered regions and save edits to this file instead of creating alternate `.pen`
copies. Screenshots, previews, and rendered exports are local review artifacts;
Git ignores image files under `design/`, including `previews/` and `exports/`.
Attach screenshots to pull requests instead of committing them. Runtime brand
and provider assets remain under `src/Asgard.Hermod.UI/Assets/`; the brand export
script is versioned separately.

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
