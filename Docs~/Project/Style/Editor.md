# Editor

Editor code uses Page, Type, Flow, and Data. Read this page in addition to those when the file is under `Editor/`.

An inspector may use a `#region` for a long block. The body is indented one level, and `#endregion` repeats the region name. An inspector redraw does not apply derived pass state or mark a `MeshComponent` dirty. That rule is in `ARCHITECTURE.md` §8 and is not relaxed by layout.

Do not save assets, migrate scenes, or rewrite a user's open editor session to change layout.
