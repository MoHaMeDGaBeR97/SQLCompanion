# SQL Companion for SSMS 20

A Visual Studio extension (VSIX) that adds productivity tools to **SQL Server Management Studio 20**:

1. **Column search across all tables/views** — a dockable tool window; type a full or partial
   column name and see every matching table/view with schema, name, data type, nullability, and
   PK/FK badges. Case-insensitive, partial matching, sortable, filterable by schema.
2. **Declare-variable command** — a toolbar button **and** a query-editor right-click command that
   scans the active batch, finds `@variables` used without a `DECLARE`, infers a type from usage, and
   inserts the `DECLARE` statements at the top of the batch.
3. **Table relationships panel** — a dockable tool window that shows all FK relationships for a table
   (both directions) with the joining columns, and inserts a ready-made `INNER`/`LEFT JOIN` at the
   cursor when you activate a row.

Plus: CSV/clipboard export, per-row copy/insert actions, recent-search history, "Generate SELECT",
and a current-DB vs all-databases search scope toggle.

---

## Platform & compatibility

- **SSMS 20 is the Visual Studio 2017 (15.0) *isolated shell*** — a shell app whose product id is
  **`ssms`**. It is **not** the VS 2019 (16.x) shell and **not** VS 2022 (17.x). This project targets
  the 15.0 isolated shell so SSMS will load it:
  - `Microsoft.VisualStudio.SDK` **15.9.x** (not 16.x/17.x).
  - `source.extension.vsixmanifest` uses the **legacy 2010 vsx-schema** with
    `<SupportedProducts><IsolatedShell Version="1.0">ssms</IsolatedShell></SupportedProducts>` —
    exactly what SSMS's own extensions declare. The modern v3 `<InstallationTarget>` mechanism can
    **never** match SSMS (it resolves targets through the VS setup catalog, which only lists full
    Visual Studio), so every attempt fails with `NoApplicableSKUsException`.
- **.NET Framework 4.7.2** (meets the SSMS 20 requirement).
- Modern **AsyncPackage** model with background loading.

> **Verified on-machine:** `HKLM\SOFTWARE\WOW6432Node\Microsoft\AppEnv\15.0\Apps\ssms_20.0`
> (AppName=`ssms`); SSMS's bundled `VSIXInstaller.exe` is v15.9; SSMS's core manifest at
> `Common7\IDE\Extensions\Application\extension.vsixmanifest` declares the `IsolatedShell` `ssms`
> target above.

> ⚠️ **Version-fragile areas are flagged in code comments** with `>>> VERSION-FRAGILE <<<`. The most
> important is `Db/ConnectionContext.cs`, which reads the active connection from SSMS's *undocumented*
> `ScriptFactory` via reflection. Test it against your exact SSMS 20 build and adjust member names if
> the connection isn't detected.

---

## Required SDK / NuGet references

These are restored automatically from `SQLCompanion.csproj` (PackageReference):

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.VisualStudio.SDK` | `15.9.3` | VS 2017 (15.0) / SSMS 20-compatible shell assemblies (EnvDTE, Shell, Interop). `ExcludeAssets=runtime` keeps them out of the VSIX. Only `15.0.0`, `15.0.1`, and `15.9.3` exist on nuget.org — do **not** bump to 16.x/17.x. |
| `Microsoft.VSSDK.BuildTools` | `16.11.2` | VSCT compiler, `.pkgdef` generation, VSIX packaging. **Not** 15.9.x: the 15.9 build tasks (`Microsoft.VisualStudio.Sdk.BuildTasks.15.0.dll`) are **x86-only** and fail to load under the 64-bit MSBuild the VS2022 IDE uses (*"could not be loaded … an attempt was made to load a program with an incorrect format"*). The 16.11 tasks are AnyCPU. BuildTools does **not** affect the shell target — only the SDK version + manifest do — so pairing 16.11 tooling with the 15.9 SDK is correct. |

Framework references used directly (no NuGet): `PresentationCore`, `PresentationFramework`,
`WindowsBase`, `System.Xaml`, `System.Data`. (WPF UI is built **in C# code**, so there is no XAML
markup compilation to configure.)

### Prerequisites to build
- **Visual Studio 2022** with the **"Visual Studio extension development"** workload.
- **.NET Framework 4.7.2 targeting pack** (installed with that workload / via the individual component).

---

## How to build (Visual Studio 2022)

1. Open `SQLCompanion.sln`.
2. Let NuGet restore (Build ▸ Restore NuGet Packages if needed).
3. Build ▸ Build Solution (Ctrl+Shift+B).
4. The VSIX is produced at:
   `SQLCompanion\bin\Debug\SQLCompanion.vsix` (or `bin\Release\...`).

### Command-line build
```powershell
# From a "Developer PowerShell for VS 2022" prompt, in the solution folder:
msbuild SQLCompanion.sln /t:Restore
msbuild SQLCompanion.sln /p:Configuration=Release /p:DeployExtension=false
```
`DeployExtension=false` is important: the default VSSDK post-build step deploys into a Visual Studio
*experimental* hive (the wrong product for us) and can fail with `VSSDK1081` (file locked). We deploy
to SSMS ourselves — see **How to install** below. The repo's `deploy.ps1` already passes this flag.

---

## How to debug against SSMS 20 (F5)

VSIX projects default to launching `devenv.exe` and auto-deploying into its experimental hive.
Neither applies to SSMS (it's an isolated shell with no Exp hive, and it won't accept the VSSDK
deploy). So the flow is: **deploy with `deploy.ps1`, then attach the debugger to SSMS.**

1. Build, then run `.\deploy.ps1` (see **How to install** below) to copy the extension into SSMS.
2. Start SSMS 20 normally.
3. In Visual Studio: **Debug ▸ Attach to Process…**, pick **Ssms.exe**, and choose the
   **Managed (.NET Framework 4.x)** code type. Set breakpoints in the SQLCompanion source.
4. In SSMS: connect to a server, open a query window, then use **Tools ▸ SQL Companion: Column
   Search / Relationships**, the **SQL Companion toolbar**, or right-click in the editor ▸
   **Declare Variables** — your breakpoints hit.

> Iteration loop: edit → `.\deploy.ps1` → restart SSMS → re-attach. (Because SSMS locks the loaded
> DLL, you must close SSMS before re-deploying a new build.)
>
> The `.csproj` `StartProgram`/`StartArguments` and F5 auto-deploy are intentionally **not** used for
> SSMS; `<DeployExtension>false</DeployExtension>` is set so no accidental Exp-hive deploy happens.

---

## How to install the built .vsix into SSMS 20

> ⚠️ **Do not double-click the `.vsix` / use `VSIXInstaller.exe`.** The standalone installer resolves
> install targets through the modern VS *setup catalog*, which never contains the isolated shell, so it
> rejects the extension with `NoApplicableSKUsException` for *every* manifest target (verified:
> `Community`, `Pro`, `Enterprise`, `IsolatedShell`, `IsolatedShell.ssms`, at versions
> `1.0`/`15.0`/`20.0`) — not even SSMS's own bundled `VSIXInstaller.exe` works. The extension is
> installed by **copying its files into SSMS and re-merging the menus**, which `deploy.ps1` does.

> ⚠️ **Machine-wide install is required for the menus to appear.** Copying into the *per-user*
> Extensions folder makes SSMS *load* the extension, but it does **not** merge the command table — the
> Tools-menu items and toolbar never show. The extension must go in the **machine-wide**
> `…\Common7\IDE\Extensions\SQLCompanion` folder **and** you must run **`ssms.exe /setup`** once to
> rebuild the merged menus. (Verified: after that, **Column Search** and **Relationships** appear under
> the **Tools** menu.) This needs **Administrator rights** (writes under Program Files + runs /setup).

**Install / update — run `deploy.ps1` from the repo root (elevated):**
```powershell
.\deploy.ps1                              # build Debug + deploy machine-wide + /setup
.\deploy.ps1 -Configuration Release       # build Release + deploy
.\deploy.ps1 -NoBuild                     # deploy the existing bin\Debug\SQLCompanion.vsix as-is
.\deploy.ps1 -Uninstall                   # remove it (+ /setup)
```
Run it from an **Administrator** PowerShell (it self-elevates via UAC if not). The script builds with
`DeployExtension=false`, closes SSMS, extracts the `.vsix` payload (`extension.vsixmanifest`,
`SQLCompanion.dll`, `SQLCompanion.pkgdef`) into
```
<SSMS>\Common7\IDE\Extensions\SQLCompanion\
```
clears the per-user extension cache, and runs `ssms.exe /setup`. Then start SSMS.

**Manual equivalent:** extract `SQLCompanion.vsix` (it's a zip), copy those three files into the folder
above, then run `"<SSMS>\Common7\IDE\ssms.exe" /setup` (SSMS closed).

**To uninstall:** `.\deploy.ps1 -Uninstall`, or delete `…\Common7\IDE\Extensions\SQLCompanion\` and run
`ssms.exe /setup`.

**Verify it loaded:** the **Tools ▸ SQL Companion** submenu appears, containing **Column Search**,
**Relationships**, and **Declare Variables**. For load diagnostics,
`%APPDATA%\Microsoft\AppEnv\15.0\ActivityLog.xml` (via `ssms.exe /log`) shows
*"Found … SQLCompanion\extension.vsixmanifest"* → *"Successfully loaded extension"*.

> Note: the package itself is **background/on-demand loaded** — it loads the first time you invoke a
> command (SSMS defers autoload while the *Connect to Server* dialog is open). The menu/toolbar entries
> come from the merged command table and appear before the package loads.

---

## Project structure

```
SQLCompanion/
├─ SQLCompanion.csproj                 # VSIX project (targets VS2017/15.0 isolated shell, .NET 4.7.2)
├─ source.extension.vsixmanifest       # Legacy 2010 schema; <IsolatedShell>ssms</IsolatedShell>
├─ SQLCompanionPackage.cs              # AsyncPackage: registers tool windows + commands
├─ SQLCompanionPackage.vsct           # Toolbar, Tools-menu, editor context-menu commands
├─ PackageGuids.cs                     # GUIDs + command IDs (kept in sync with the .vsct)
├─ Strings.cs                          # (e) ALL tooltips / captions / messages — localize here
├─ Db/
│  ├─ Models.cs                        # ColumnResult, RelationshipInfo, ActiveConnectionInfo
│  ├─ ConnectionContext.cs            # (a) reads active SSMS connection (reflection; FRAGILE)
│  └─ DatabaseService.cs              # (a) all SQL queries (INFORMATION_SCHEMA + sys.foreign_keys)
├─ Editor/
│  ├─ EditorService.cs                # DTE-based read/insert into the active query editor
│  └─ JoinClauseBuilder.cs            # (d) builds INNER/LEFT JOIN clauses from an FK
├─ Features/
│  ├─ ColumnSearch/                    # (b) tool window + WPF control
│  ├─ DeclareVariables/                # (c) generator (pure logic) + command
│  └─ Relationships/                   # (d) tool window + WPF control
└─ Commands/                           # Show-tool-window commands
```

Modules map to the requested split: **(a)** DB access, **(b)** column search, **(c)** declare
variables, **(d)** relationships + JOIN, **(e)** shared strings/tooltips.

---

## Error handling & friendly messages

- **No active connection** → tool windows and commands show
  *"No active SQL connection was found…"* (see `Strings.Messages.NoActiveConnection`).
- **Query failures** → the tool window status line shows *"The query failed: …"*.
- **Empty results** → *"No matching columns were found."* / *"No foreign-key relationships…"*.
- **No active editor** for insert/declare actions → *"No active query editor was found…"*.

## Tooltips

Every clickable element (toolbar buttons, menu items, panel buttons, grid rows, context-menu
items) has a one-sentence, action-oriented tooltip. All tooltip text lives in **one place**:
`Strings.Tooltips` in `Strings.cs`. The `.vsct` mirrors a few of these in `<ToolTipText>` (noted
with comments) because command tooltips are defined in the command table, not in code.

## Toggling the optional features

The "suggested extras" are isolated so you can disable them without touching core logic:
- CSV export / Copy All / Refresh buttons: the `MakeButton(...)` calls in
  `Features/ColumnSearch/ColumnSearchControl.cs` (remove a line to hide a button).
- Recent searches: the `_recentCombo` row in the same file.
- Row copy / Generate SELECT: `BuildRowContextMenu()` in the same file.
- All-databases scope: the `_scopeCombo` row (defaults to current database).
