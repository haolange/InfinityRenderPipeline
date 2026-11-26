using System.Collections.Generic;
using Unity.Profiling;

namespace InfinityTech.Rendering.Pipeline
{
    // CPU record and ProcessLight stages. Pass ProfilingSamplers stay on the GPU and execute track.
    internal static class PipelineCpuMarkers
    {
        static readonly List<string> s_Names = new List<string>();
        static readonly List<ProfilerMarker> s_Markers = new List<ProfilerMarker>();

        static ProfilerMarker Define(string name)
        {
            ProfilerMarker marker = new ProfilerMarker(name);
            s_Names.Add(name);
            s_Markers.Add(marker);
            return marker;
        }

        internal static IReadOnlyList<string> Names => s_Names;
        internal static IReadOnlyList<ProfilerMarker> Markers => s_Markers;

        internal static readonly ProfilerMarker s_RecordUploadCameraUniforms = Define("Record.UploadCameraUniforms");
        internal static readonly ProfilerMarker s_RecordUploadLightData = Define("Record.UploadLightData");
        internal static readonly ProfilerMarker s_RecordUploadAnalyticShadow = Define("Record.UploadAnalyticShadow");
        internal static readonly ProfilerMarker s_RecordComputeLightCookies = Define("Record.ComputeLightCookies");
        internal static readonly ProfilerMarker s_RecordComputeReflectionProbes = Define("Record.ComputeReflectionProbes");
        internal static readonly ProfilerMarker s_RecordComputeCombineLuts = Define("Record.ComputeCombineLuts");
        internal static readonly ProfilerMarker s_RecordComputeAtmosphericLUT = Define("Record.ComputeAtmosphericLUT");
        internal static readonly ProfilerMarker s_RecordComputeTemporalColorPyramid = Define("Record.ComputeTemporalColorPyramid");
        internal static readonly ProfilerMarker s_RecordPrepareMeshMotion = Define("Record.PrepareMeshMotion");
        internal static readonly ProfilerMarker s_RecordCullMainMeshViews = Define("Record.CullMainMeshViews");
        internal static readonly ProfilerMarker s_RecordRasterDepth = Define("Record.RasterDepth");
        internal static readonly ProfilerMarker s_RecordCreateMeshEarlyOccluder = Define("Record.CreateMeshEarlyOccluder");
        internal static readonly ProfilerMarker s_RecordCullEarlyMeshViews = Define("Record.CullEarlyMeshViews");
        internal static readonly ProfilerMarker s_RecordRasterDepthRecovery = Define("Record.RasterDepthRecovery");
        internal static readonly ProfilerMarker s_RecordRasterDBuffer = Define("Record.RasterDBuffer");
        internal static readonly ProfilerMarker s_RecordRasterDirectDecals = Define("Record.RasterDirectDecals");
        internal static readonly ProfilerMarker s_RecordRasterGBuffer = Define("Record.RasterGBuffer");
        internal static readonly ProfilerMarker s_RecordRasterReflections = Define("Record.RasterReflections");
        internal static readonly ProfilerMarker s_RecordRasterMotion = Define("Record.RasterMotion");
        internal static readonly ProfilerMarker s_RecordComputeHiZ = Define("Record.ComputeHiZ");
        internal static readonly ProfilerMarker s_RecordComputeHalfResDownsample = Define("Record.ComputeHalfResDownsample");
        internal static readonly ProfilerMarker s_RecordComputeLightTileMask = Define("Record.ComputeLightTileMask");
        internal static readonly ProfilerMarker s_RecordTraceOcclusion = Define("Record.TraceOcclusion");
        internal static readonly ProfilerMarker s_RecordComputeGroundTruthOcclusion = Define("Record.ComputeGroundTruthOcclusion");
        internal static readonly ProfilerMarker s_RecordComputeContactShadow = Define("Record.ComputeContactShadow");
        internal static readonly ProfilerMarker s_RecordComputeScreenSpaceReflection = Define("Record.ComputeScreenSpaceReflection");
        internal static readonly ProfilerMarker s_RecordComputeScreenSpaceIndirect = Define("Record.ComputeScreenSpaceIndirect");
        internal static readonly ProfilerMarker s_RecordCullShadowCasterSplits = Define("Record.CullShadowCasterSplits");
        internal static readonly ProfilerMarker s_RecordRasterCascadeShadow = Define("Record.RasterCascadeShadow");
        internal static readonly ProfilerMarker s_RecordRasterLocalShadow = Define("Record.RasterLocalShadow");
        internal static readonly ProfilerMarker s_RecordComputeShadowMoments = Define("Record.ComputeShadowMoments");
        internal static readonly ProfilerMarker s_RecordRasterDeferredShading = Define("Record.RasterDeferredShading");
        internal static readonly ProfilerMarker s_RecordRasterEmissive = Define("Record.RasterEmissive");
        internal static readonly ProfilerMarker s_RecordRecordDeferredLightingCapture = Define("Record.RecordDeferredLightingCapture");
        internal static readonly ProfilerMarker s_RecordRasterForward = Define("Record.RasterForward");
        internal static readonly ProfilerMarker s_RecordCopyHistorySSR = Define("Record.CopyHistorySSR");
        internal static readonly ProfilerMarker s_RecordCopyHistorySSGI = Define("Record.CopyHistorySSGI");
        internal static readonly ProfilerMarker s_RecordRasterSubsurfaceScattering = Define("Record.RasterSubsurfaceScattering");
        internal static readonly ProfilerMarker s_RecordComputeAtmosphericSkyAndFog = Define("Record.ComputeAtmosphericSkyAndFog");
        internal static readonly ProfilerMarker s_RecordRecordLightingCapture = Define("Record.RecordLightingCapture");
        internal static readonly ProfilerMarker s_RecordRasterTranslucentDepth = Define("Record.RasterTranslucentDepth");
        internal static readonly ProfilerMarker s_RecordComputeVolumetricCloud = Define("Record.ComputeVolumetricCloud");
        internal static readonly ProfilerMarker s_RecordCopyHistoryVolumetricCloud = Define("Record.CopyHistoryVolumetricCloud");
        internal static readonly ProfilerMarker s_RecordComputeVolumetricFog = Define("Record.ComputeVolumetricFog");
        internal static readonly ProfilerMarker s_RecordCopyHistoryVolumetricFog = Define("Record.CopyHistoryVolumetricFog");
        internal static readonly ProfilerMarker s_RecordComputeFogComposite = Define("Record.ComputeFogComposite");
        internal static readonly ProfilerMarker s_RecordRasterTranslucentT0 = Define("Record.RasterTranslucentT0");
        internal static readonly ProfilerMarker s_RecordComputeColorPyramid = Define("Record.ComputeColorPyramid");
        internal static readonly ProfilerMarker s_RecordRasterTranslucentT1 = Define("Record.RasterTranslucentT1");
        internal static readonly ProfilerMarker s_RecordRasterTranslucentT2 = Define("Record.RasterTranslucentT2");
        internal static readonly ProfilerMarker s_RecordComputeSuperResolution = Define("Record.ComputeSuperResolution");
        internal static readonly ProfilerMarker s_RecordComputeAntiAliasing = Define("Record.ComputeAntiAliasing");
        internal static readonly ProfilerMarker s_RecordRecordCameraOutput = Define("Record.RecordCameraOutput");
        internal static readonly ProfilerMarker s_RecordEnsureRequiredProducers = Define("Record.EnsureRequiredProducers");

        internal static readonly ProfilerMarker s_ProcessLightShadowCache = Define("ProcessLight.ShadowCache");
        internal static readonly ProfilerMarker s_ProcessLightResolveCookies = Define("ProcessLight.ResolveCookies");
        internal static readonly ProfilerMarker s_ProcessLightResolveProbes = Define("ProcessLight.ResolveProbes");
        internal static readonly ProfilerMarker s_ProcessLightContentFeatures = Define("ProcessLight.ContentFeatures");
        internal static readonly ProfilerMarker s_ProcessLightExecuteCommandBuffer = Define("ProcessLight.ExecuteCommandBuffer");
    }
}
