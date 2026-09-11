# CoreRP / HDRP 17.6 API research pack

Scope: Unity 6000.6 / CoreRP 17.6 public surfaces InfinityRP copies. HDRP-internal types are named only as references, not as dependencies.

## 1. RenderPipelineGlobalSettings

```csharp
namespace UnityEngine.Rendering
{
    // Official 17.6 docs: settings type first, pipeline type second.
    public abstract class RenderPipelineGlobalSettings<TGlobalRenderPipelineSettings, TRenderPipeline>
        : RenderPipelineGlobalSettings, ISerializationCallbackReceiver
        where TGlobalRenderPipelineSettings : RenderPipelineGlobalSettings
        where TRenderPipeline : RenderPipeline
    {
        public static TGlobalRenderPipelineSettings instance { get; }
    }
}
```

Editor asset path:

```csharp
#if UNITY_EDITOR
[UnityEditor.FilePath("ProjectSettings/InfinityRenderPipelineGlobalSettings.asset",
    UnityEditor.FilePathAttribute.Location.ProjectFolder)]
#endif
```

Create / bind:

- `GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>()`
- Editor: `RenderPipelineGlobalSettingsUtils.TryEnsure<TSettings, TPipeline>(ref instance, path, canCreateNewAsset)` in `UnityEditor.Rendering`.
- Bind: `GraphicsSettings.GetSettingsForRenderPipeline<InfinityRenderPipeline>()` and `EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<InfinityRenderPipeline>(asset)`.
- Own settings through `RenderPipelineGraphicsSettingsContainer m_Settings` and `protected override List<IRenderPipelineGraphicsSettings> settingsList`. Field name `m_Settings` is required for the Graphics Settings inspector.
- Player includes the assigned GlobalSettings automatically. Missing settings throw at pipeline construction.

First type argument is the **GlobalSettings class**, second is the **pipeline class**. URP: `RenderPipelineGlobalSettings<UniversalRenderPipelineGlobalSettings, UniversalRenderPipeline>`.

## 2. IRenderPipelineGraphicsSettings / IRenderPipelineResources

```csharp
public interface IRenderPipelineGraphicsSettings
{
    int version { get; }
    bool isAvailableInPlayerBuild { get; }
}

public interface IRenderPipelineResources : IRenderPipelineGraphicsSettings { }

[AttributeUsage(AttributeTargets.Field)]
public sealed class ResourcePathAttribute : Attribute
{
    public ResourcePathAttribute(string resourcePath);
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class SupportedOnRenderPipelineAttribute : Attribute
{
    public SupportedOnRenderPipelineAttribute(params Type[] renderPipeline);
}
```

Container pattern:

```csharp
[Serializable]
[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
public sealed class InfinityRenderPipelineRuntimeShaders : IRenderPipelineResources
{
    [SerializeField] int m_Version = 1;
    public int version => m_Version;
    bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

    [ResourcePath("Shaders/RenderingFeature/TemporalAntiAliasing/Compute_TemporalAntiAliasing.compute")]
    public ComputeShader taaShader;
}
```

Resolve: `GraphicsSettings.TryGetRenderPipelineSettings<T>(out T settings)`.

Editor reload of nulls: `UnityEditor.Rendering.ResourceReloader.ReloadAllNullIn(instance, "Packages/com.infinity.render-pipeline")`. Paths are package-relative, no `Packages/com.../` prefix inside `[ResourcePath]`.

17.5 vs 17.6: `TryGetRenderPipelineSettings` and `IRenderPipelineResources` are 17.x / Unity 6. Do not use the older HDRP `HDRenderPipelineGlobalSettings` serialized shader fields.

## 3. VolumeManager two-level profiles

```csharp
VolumeManager.instance.Initialize(VolumeProfile defaultProfile, VolumeProfile qualityProfile);
```

- `defaultProfile` is the Player type registry and the inherited default.
- `qualityProfile` is optional per RP Asset / Quality level.
- After `ReplaceData`, stack `overrideState` is false for inherited default values. Do not gate features on that flag.
- `VolumeComponent.IsActive()` defaults to `active`. Override it for enable/intensity/mode.

HDRP `DefaultVolumeProfileSettings` is an `IRenderPipelineGraphicsSettings` holding the default Profile. Infinity copies that idea; do not reference the HDRP type.

## 4. Volume editors

```csharp
[VolumeComponentEditor(typeof(Bloom))]
sealed class BloomEditor : VolumeComponentEditor { }

// CoreRP Editor
UnityEditor.Rendering.TrackballUIDrawer
VolumeParameterDrawer
VolumeComponentMenuForRenderPipelineAttribute // alternative to VolumeComponentMenu + SupportedOnRenderPipeline
```

Prefer `[VolumeComponentMenu("Post-processing/Bloom")]` + `[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]`.

`TrackballUIDrawer` lives in `UnityEditor.Rendering` and draws a color wheel for `Vector4` lift/gamma/gain. If the type is missing in a given 17.6 build, fall back to `EditorGUILayout.ColorField` + slider for W.

## 5. Rendering Debugger

```csharp
public interface IDebugDisplaySettings
{
    void Reset();
}

public abstract class DebugDisplaySettings<T> : IDebugDisplaySettings
    where T : DebugDisplaySettings<T>, new()
{
    public static T Instance { get; }
}

public sealed class DebugDisplaySettingsVolume : IDebugDisplaySettingsData { }

DebugManager.instance.RegisterData(settings);
```

HDRP `HDDebugDisplaySettings` (internal) registers `DebugDisplaySettingsVolume`. Infinity registers its own `InfinityDebugDisplaySettings` plus the Core volume panel. Works in Editor and Development Player. Do not use obsolete `HDVolumeDebugSettings`.

## 6. Additional Camera / Light data

```csharp
[CustomEditor(typeof(Camera))]
[SupportedOnRenderPipeline(typeof(InfinityRenderPipelineAsset))]
sealed class InfinityCameraEditor : Editor // not CameraEditor
```

Inheriting `CameraEditor` pulls Built-in command-buffer UI and `Camera.GetCommandBuffers` warnings. Draw supported native fields through `SerializedObject.FindProperty` (`m_Name`, `m_BackGroundColor`, `orthographic`, `field of view`, `m_NormalizedViewPortRect`, `targetTexture`, …).

Auto-add:

```csharp
Undo.AddComponent<InfinityAdditionalCameraData>(camera.gameObject);
```

After Undo, rebuild the additional `SerializedObject`. `CameraEditorUtils` remains valid for Scene GUI handles.

## 7. SupportedRenderingFeatures and UI overlay

```csharp
SupportedRenderingFeatures.active = new SupportedRenderingFeatures { rendersUIOverlay = true, motionVectors = true, /* ... */ };
context.DrawUIOverlay(camera);
```

Some 17.x builds expose `DrawUIOverlay` on `ScriptableRenderContext` only (no CommandBuffer overload). Record a Raster pass, then call `context.DrawUIOverlay` from execute via a captured context — Infinity's RG raster execute already has `ScriptableRenderContext` on the encoder path. If the encoder cannot reach context, execute the overlay as a graph-external call immediately after graph execute and before Present, still after OutputTransform.

Dispose must restore `SupportedRenderingFeatures.active` and `Shader.globalRenderPipeline` captured at construction.

## 8. UnifiedRayTracing

Namespace: `UnityEngine.Rendering.UnifiedRayTracing` (CoreRP, 6000.3+).

Public types: `RayTracingContext`, `RayTracingResources`, `IRayTracingAccelStruct`, `AccelStructInstances`, `RayTracingBackend` (`Hardware`, `Compute`, `CPU`).

`.urtshader` is a scriptable importer that emits hardware + compute variants. RTAO needs visibility rays only — no ShaderLab hit shaders.

Metal: `SystemInfo.supportsRayTracing == false`. Use `RayTracingBackend.Compute`. D3D12 hardware remains `UNVERIFIED (external)`.

Do not call HDRP `HDRaytracingManager`.

## 9. Build processors

```csharp
IPreprocessBuildWithReport
IPreprocessShaders
IPreprocessComputeShaders
```

HDRP `HDRPPreprocessBuild` / `HDRPShaderVariantStripper` are HDRP-internal. Infinity implements the same interfaces and strips non-Infinity `RenderPipeline` tags plus unused Infinity kernels when the corresponding Volume/asset feature is off.

## 10. QualitySettings

`QualitySettings.renderPipeline` holds a per-level `RenderPipelineAsset`. Switching Quality recreates the pipeline. GlobalSettings stay project-wide. Optional `qualityVolumeProfile` on each asset is the second `VolumeManager.Initialize` argument.
