# CLAUDE.md

Guidance for Claude Code when working in this Unity project.

## Project identity

- **Name:** panel_scene_unity
- **Engine:** Unity 6 (`6000.4.7f1`)
- **Render pipeline:** HDRP (High Definition Render Pipeline) — bootstrapped from the `3D HDRP` template (`com.unity.template.hdrp-blank` 17.0.7, ships HDRP 17.0.1)
- **Target build platform:** Windows Standalone (`StandaloneWindows64`), x86_64
- **Color space:** Linear (required by HDRP — set in PlayerSettings before first build)
- **Scripting backend:** Mono (switch to IL2CPP only when shipping; it slows iteration)

## Unity CLI

Unity is **not on PATH**. Use the full editor path. Start every PowerShell session at the project root with:

```powershell
$UNITY = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe"
$PROJ  = $PWD.Path  # examples below assume PowerShell is in the project root
```

(`$UNITY` is the default Unity Hub install path on Windows; adjust if you installed elsewhere.)

Common flags:

| Flag | Purpose |
|---|---|
| `-batchmode -nographics` | Headless, no GUI, no graphics device |
| `-quit` | Exit when `-executeMethod` returns |
| `-projectPath <abs>` | Required for everything except `-createProject` |
| `-executeMethod NS.Class.StaticMethod` | Runs a static C# method located under `Assets/**/Editor/` |
| `-logFile -` | Stream log to stdout (use when running via Bash/PowerShell tool) |
| `-logFile <abs>` | Write to file (use for long background runs) |
| `-buildTarget StandaloneWindows64` | Switch active build target up-front |

**Single-instance lock:** only one Unity process can hold the project lock. If the editor GUI is open, CLI commands targeting the same project fail. Close the editor before running CLI builds.

**License:** Unity needs an activated Personal/Pro license. CLI commands fail with cryptic errors if the license is invalid — grep the log for `No valid Unity Editor license found`. First-time activation must be done interactively via the Hub.

**Long-running commands:** Builds take 5–15 min. Run with `run_in_background: true`, write to `Builds/Logs/build.log`, and stream with `Get-Content Builds/Logs/build.log -Wait -Tail 50`.

## Folder structure

```
Assets/
├── _Project/                  # All first-party assets (underscore sorts to top of Project window)
│   ├── Art/
│   │   ├── Materials/
│   │   ├── Models/
│   │   ├── Shaders/
│   │   ├── Textures/
│   │   └── VFX/
│   ├── Audio/
│   │   ├── Music/
│   │   └── SFX/
│   ├── Prefabs/
│   ├── Scenes/
│   │   └── Main.unity         # Default entry scene
│   ├── Scripts/
│   │   ├── Runtime/           # Gameplay code → Project.Runtime.asmdef
│   │   └── Editor/            # Editor tooling → Project.Editor.asmdef
│   └── Settings/
│       ├── HDRP/              # HDRPAsset + default Volume Profile
│       └── Quality/           # Per-target quality assets
├── Plugins/                   # Third-party DLLs / SDKs only
└── StreamingAssets/           # Files copied verbatim into the build (configs, data)

Builds/                        # Build output, gitignored
├── Windows/
└── Logs/
```

**Rules:**

- First-party content **always** under `Assets/_Project/` — never loose under `Assets/`. Keeps third-party package imports cleanly isolated.
- All runtime/editor scripts live behind assembly definitions (`Project.Runtime.asmdef`, `Project.Editor.asmdef`). Faster incremental compiles and enforced dependency boundaries.
- Editor scripts **must** live inside a folder literally named `Editor` (Unity magic-folder rule).
- One scene per file. Lightmap and reflection-probe bakes go to a sibling folder Unity creates automatically — do not move them by hand.

## Build automation

Builds are driven by a static method invoked via `-executeMethod`. Source of truth: `Assets/_Project/Scripts/Editor/BuildScript.cs`.

Skeleton:

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    public static void BuildWindowsStandalone()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        Directory.CreateDirectory("Builds/Windows");

        var opts = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = "Builds/Windows/PanelScene.exe",
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
```

Invoke:

```powershell
& $UNITY -batchmode -nographics -projectPath $PROJ `
  -buildTarget StandaloneWindows64 `
  -executeMethod BuildScript.BuildWindowsStandalone `
  -logFile "$PROJ\Builds\Logs\build.log" -quit
```

**Always check the process exit code, not stdout.** Unity prints warnings on successful builds; only `EditorApplication.Exit(0)` proves success.

## Scene authoring via CLI

Programmatic scene creation follows the same `-executeMethod` pattern. Pattern (place in `Assets/_Project/Scripts/Editor/SceneTools.cs`):

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class SceneTools
{
    public static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Required for HDRP: a Global Volume with a profile, otherwise the scene renders pink.
        var volumeGO = new GameObject("Global Volume");
        var volume   = volumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/_Project/Settings/HDRP/DefaultVolumeProfile.asset");

        EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/Main.unity");
    }
}
```

```powershell
& $UNITY -batchmode -nographics -projectPath $PROJ `
  -executeMethod SceneTools.CreateMainScene -logFile - -quit
```

## HDRP gotchas

- **Pink everywhere = missing Volume.** Every scene needs a Global Volume with a profile that includes at minimum: Visual Environment, HDRI Sky (or Physically Based Sky), Exposure.
- **Quality settings reference an HDRP Asset.** `Edit > Project Settings > Quality > Rendering` must point at `Assets/_Project/Settings/HDRP/HDRPAsset.asset` for *every* quality level you intend to ship.
- **Linear color space + DX11+ are mandatory.** Set both in PlayerSettings before first build.
- **Reflection probes & sky.** The HDRP template wires these into the default scene. When scaffolding from blank, copy the default Volume Profile out of the template package or recreate the overrides manually.
- **HDRP does not support deferred + forward mixing arbitrarily.** Pick Deferred unless you have a reason; document the reason here if you switch.

## Bootstrap recipe (run once)

Historical record of how this project was scaffolded — left here so future contributors can re-create the environment from scratch if needed. Run from an empty directory:

```powershell
$UNITY    = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe"
$PROJ     = (Resolve-Path .).Path   # or any absolute path to an empty directory
$TEMPLATE = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Data\Resources\PackageManager\ProjectTemplates\com.unity.template.3d-high-end-17.0.7.tgz"

# 1. Create the project from the HDRP template (first run can take 5–10 min: package resolution + import).
& $UNITY -batchmode -nographics `
  -createProject $PROJ `
  -cloneFromTemplate $TEMPLATE `
  -logFile "$env:TEMP\unity-bootstrap.log" -quit
```

Then apply the folder scaffold + assembly definitions + `BuildScript.cs` via a one-shot Editor method (see `Assets/_Project/Scripts/Editor/Bootstrap.cs` once it exists). The bootstrap method is responsible for:

1. Creating the `_Project/...` folder tree.
2. Generating `Project.Runtime.asmdef` and `Project.Editor.asmdef`.
3. Moving the template's HDRP asset + default volume profile into `_Project/Settings/HDRP/`.
4. Replacing the template's `SampleScene` with `_Project/Scenes/Main.unity` and updating `EditorBuildSettings.scenes`.
5. Setting PlayerSettings: color space = Linear, target = StandaloneWindows64, company/product name.

After bootstrap, validate with a build:

```powershell
& $UNITY -batchmode -nographics -projectPath $PROJ `
  -buildTarget StandaloneWindows64 `
  -executeMethod BuildScript.BuildWindowsStandalone `
  -logFile "$PROJ\Builds\Logs\build.log" -quit
echo "Exit: $LASTEXITCODE"
```

## Scene authoring tools

The current scene (`Main.unity`) is a pearl-blue car paint panel showcase. Rebuild it from scratch via:

```powershell
& $UNITY -batchmode -nographics -projectPath . -executeMethod Project.Editor.SceneBuilder.BuildPanelScene -logFile - -quit
```

`SceneBuilder` is idempotent — wipes all root GameObjects in `Main.unity` and rebuilds: Panel (22×30×0.3 cm) + Pedestal + Backdrop + Floor + Camera + Sun + 2 Area Lights + Global Volume. Material `PearlBlueCarPaint.mat` is cloned from the imported HDRP `CoatedCarPaint_Stacklit.mat` sample and tinted.

If `Assets/Samples/High Definition Render Pipeline/` is missing (fresh clone), run the import first:

```powershell
& $UNITY -batchmode -nographics -projectPath . -executeMethod Project.Editor.SampleImporter.ImportMaterialSamples -logFile - -quit
```

Other one-shot Editor methods:
- `Project.Editor.HdrpQualityTuner.EnableSsr` — enables Screen Space Reflections on the active HDRP quality asset
- `Project.Editor.VolumeProfileTuner.AddHdriSky` — adds an HDRI sky override to `SkyandFogSettingsProfile.asset`

Runtime interaction (in the built player or Play mode):

| Input | Effect |
|---|---|
| Left-drag | Rotate panel (yaw on world Y, pitch on local X, clamped ±85°) |
| Right-drag | Orbit camera around the panel |
| Mouse wheel | Dolly camera (radius 0.15–2.5 m) |
| Middle-drag | Pan camera focus |

Scripts:
- `Assets/_Project/Scripts/Runtime/PanelRotator.cs` — left-drag panel rotation
- `Assets/_Project/Scripts/Runtime/OrbitCamera.cs` — right/middle-drag + wheel camera control
- Both use the new Input System directly via `Mouse.current`.

## Working agreements for Claude in this repo

- Before any CLI command, set `$UNITY` and `$PROJ` if they're not already set in the current shell — do not assume.
- For builds and bootstraps, prefer `run_in_background: true` + `-logFile <file>`, and stream the tail. Don't block the user on a 10-minute foreground command.
- Before running a CLI command, check that no editor GUI instance is holding the project lock — look for a `Temp/UnityLockfile` inside the project root.
- Do not commit `Library/`, `Temp/`, `Logs/`, `obj/`, `Builds/`, or `*.csproj` / `*.sln` (Unity regenerates them). Set up `.gitignore` from Unity's official template when initialising git.
- When the user says "build", default to Windows Standalone unless they specify otherwise.
- When the user says "open the scene", they mean opening `Assets/_Project/Scenes/Main.unity` in the editor — not via batch mode.
