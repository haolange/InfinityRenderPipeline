using UnityEditor.ShaderGraph;

namespace UnityEditor.Rendering.Infinity.ShaderGraph
{
    // Pass descriptions shared by the Infinity sub-targets. LightMode tags, keywords and render states mirror
    // Shaders/Surface/InfinityLit.shader, InfinityUnlit.shader and InfinityDecal.shader; vertex and fragment code lives in
    // Editor/ShaderGraph/Includes (SHADERPASS selects the stage, InfinitySGShaderPass.hlsl).
    static class InfinityPasses
    {
        // LightMode tags (InfinityPassIDs).
        public const string kShadowPass = "ShadowPass";
        public const string kShadowCaster = "ShadowCaster";
        public const string kDepthPass = "DepthPass";
        public const string kGBufferPass = "GBufferPass";
        public const string kForwardPass = "ForwardPass";
        public const string kMotionPass = "MotionPass";
        public const string kTranslucentDepthPass = "TranslucentDepthPass";
        public const string kTranslucentT0Pass = "TranslucentT0Pass";
        public const string kTranslucentT1Pass = "TranslucentT1Pass";
        public const string kTranslucentT2Pass = "TranslucentT2Pass";
        public const string kMeta = "Meta";
        public const string kDBufferPass = "DBufferPass";

        const string kCore = InfinityTarget.kIncludePath + "InfinitySGCore.hlsl";
        const string kGraphFunctions = InfinityTarget.kIncludePath + "InfinitySGGraphFunctions.hlsl";
        const string kBakedLighting = InfinityTarget.kIncludePath + "InfinitySGBakedLighting.hlsl";
        const string kSurfaceLighting = InfinityTarget.kIncludePath + "InfinitySGSurfaceLighting.hlsl";
        const string kTranslucent = InfinityTarget.kIncludePath + "InfinitySGTranslucent.hlsl";
        const string kVaryings = InfinityTarget.kIncludePath + "InfinitySGVaryings.hlsl";
        const string kDepthOnly = InfinityTarget.kIncludePath + "InfinitySGDepthOnlyPass.hlsl";
        const string kGBuffer = InfinityTarget.kIncludePath + "InfinitySGGBufferPass.hlsl";
        const string kForward = InfinityTarget.kIncludePath + "InfinitySGForwardPass.hlsl";
        const string kMotion = InfinityTarget.kIncludePath + "InfinitySGMotionPass.hlsl";
        const string kTranslucentPass = InfinityTarget.kIncludePath + "InfinitySGTranslucentPass.hlsl";
        const string kMetaPass = InfinityTarget.kIncludePath + "InfinitySGMetaPass.hlsl";
        const string kDecalPass = InfinityTarget.kIncludePath + "InfinitySGDecalPass.hlsl";

        // --------------------------------------------------------------------------------------------------------
        // Structs

        public static readonly StructDescriptor Varyings = new StructDescriptor()
        {
            name = "Varyings",
            packFields = true,
            populateWithCustomInterpolators = true,
            fields = new FieldDescriptor[]
            {
                StructFields.Varyings.positionCS,
                StructFields.Varyings.positionWS,
                StructFields.Varyings.positionPredisplacementWS,
                StructFields.Varyings.normalWS,
                StructFields.Varyings.tangentWS,
                StructFields.Varyings.texCoord0,
                StructFields.Varyings.texCoord1,
                StructFields.Varyings.texCoord2,
                StructFields.Varyings.texCoord3,
                StructFields.Varyings.texCoord4,
                StructFields.Varyings.texCoord5,
                StructFields.Varyings.texCoord6,
                StructFields.Varyings.texCoord7,
                StructFields.Varyings.color,
                StructFields.Varyings.screenPosition,
                StructFields.Varyings.vertexID,
                StructFields.Varyings.instanceID,
                StructFields.Varyings.cullFace,
            }
        };

        public static readonly StructCollection Structs = new StructCollection
        {
            { UnityEditor.ShaderGraph.Structs.Attributes },
            { Varyings },
            { UnityEditor.ShaderGraph.Structs.SurfaceDescriptionInputs },
            { UnityEditor.ShaderGraph.Structs.VertexDescriptionInputs },
        };

        public static readonly DependencyCollection FieldDependencies = new DependencyCollection
        {
            { UnityEditor.ShaderGraph.FieldDependencies.Default },
        };

        // Motion, lightmap UVs and translucency are reconstructed from the world position in the fragment stage.
        static readonly FieldCollection PositionFields = new FieldCollection
        {
            StructFields.Varyings.positionWS,
        };

        static readonly FieldCollection ShadowFields = new FieldCollection
        {
            StructFields.Attributes.normalOS,
            StructFields.Varyings.positionWS,
        };

        static readonly FieldCollection SurfaceFields = new FieldCollection
        {
            StructFields.Attributes.normalOS,
            StructFields.Attributes.tangentOS,
            StructFields.Attributes.uv1,
            StructFields.Varyings.positionWS,
            StructFields.Varyings.normalWS,
            StructFields.Varyings.tangentWS,
            StructFields.Varyings.texCoord1,
        };

        static readonly FieldCollection MetaFields = new FieldCollection
        {
            StructFields.Attributes.uv1,
            StructFields.Attributes.uv2,
        };

        // --------------------------------------------------------------------------------------------------------
        // Block masks

        public static readonly BlockFieldDescriptor[] VertexBlocks =
        {
            BlockFields.VertexDescription.Position,
            BlockFields.VertexDescription.Normal,
            BlockFields.VertexDescription.Tangent,
        };

        public static readonly BlockFieldDescriptor[] AlphaBlocks =
        {
            BlockFields.SurfaceDescription.Alpha,
            BlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] LitBlocks =
        {
            BlockFields.SurfaceDescription.BaseColor,
            BlockFields.SurfaceDescription.NormalTS,
            BlockFields.SurfaceDescription.Metallic,
            InfinityBlockFields.SurfaceDescription.Roughness,
            InfinityBlockFields.SurfaceDescription.SpecularLevel,
            BlockFields.SurfaceDescription.Emission,
            BlockFields.SurfaceDescription.Alpha,
            BlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] MetaBlocks =
        {
            BlockFields.SurfaceDescription.BaseColor,
            BlockFields.SurfaceDescription.Metallic,
            BlockFields.SurfaceDescription.Emission,
            BlockFields.SurfaceDescription.Alpha,
            BlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] ColorBlocks =
        {
            BlockFields.SurfaceDescription.BaseColor,
            BlockFields.SurfaceDescription.Emission,
            BlockFields.SurfaceDescription.Alpha,
            BlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] DecalBlocks =
        {
            BlockFields.SurfaceDescription.BaseColor,
            BlockFields.SurfaceDescription.NormalTS,
            BlockFields.SurfaceDescription.Metallic,
            InfinityBlockFields.SurfaceDescription.Roughness,
            BlockFields.SurfaceDescription.Occlusion,
            BlockFields.SurfaceDescription.Alpha,
        };

        // --------------------------------------------------------------------------------------------------------
        // Pragmas and keywords (raw multi_compile lines, as InfinityLit declares them)

        static readonly PragmaCollection Instanced = new PragmaCollection
        {
            { Pragma.Target(ShaderModel.Target45) },
            { Pragma.MultiCompileInstancing },
            { Pragma.InstancingOptions(InstancingOptions.RenderingLayer) },
            { Pragma.Vertex("vert") },
            { Pragma.Fragment("frag") },
        };

        static readonly PragmaCollection NotInstanced = new PragmaCollection
        {
            { Pragma.Target(ShaderModel.Target45) },
            { Pragma.Vertex("vert") },
            { Pragma.Fragment("frag") },
        };

        static readonly PragmaCollection BakedLightingKeywords = new PragmaCollection
        {
            { new PragmaDescriptor { value = "multi_compile _ LIGHTMAP_ON" } },
            { new PragmaDescriptor { value = "multi_compile _ DIRLIGHTMAP_COMBINED" } },
            { new PragmaDescriptor { value = "multi_compile _ SHADOWS_SHADOWMASK" } },
            { new PragmaDescriptor { value = "multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2" } },
        };

        static PragmaCollection With(PragmaCollection pragmas, params string[] lines)
        {
            var result = new PragmaCollection { pragmas };
            foreach (string line in lines) result.Add(new PragmaDescriptor { value = line });
            return result;
        }

        // --------------------------------------------------------------------------------------------------------
        // Defines

        public static readonly KeywordDescriptor AlphaTestOn = new KeywordDescriptor
        {
            displayName = "Alpha Clipping",
            referenceName = "_ALPHATEST_ON",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.ShaderFeature,
            scope = KeywordScope.Local,
            stages = KeywordShaderStage.Fragment,
        };

        static DefineCollection Defines(InfinityTarget target)
        {
            var defines = new DefineCollection();
            if (target.alphaClip) defines.Add(AlphaTestOn, 1);
            return defines;
        }

        // --------------------------------------------------------------------------------------------------------
        // Render states

        static Cull FaceCull(InfinityTarget target)
        {
            switch (target.renderFace)
            {
                case InfinityRenderFace.Back: return Cull.Front;
                case InfinityRenderFace.Both: return Cull.Off;
                default: return Cull.Back;
            }
        }

        static RenderStateCollection DepthOnlyStates(InfinityTarget target) => new RenderStateCollection
        {
            { RenderState.ZTest(ZTest.LEqual) },
            { RenderState.ZWrite(ZWrite.On) },
            { RenderState.Cull(FaceCull(target)) },
            { RenderState.ColorMask("ColorMask 0") },
        };

        static RenderStateCollection GBufferStates(InfinityTarget target) => new RenderStateCollection
        {
            { RenderState.ZTest(ZTest.LEqual) },
            { RenderState.ZWrite(ZWrite.On) },
            { RenderState.Cull(FaceCull(target)) },
        };

        static RenderStateCollection EqualDepthStates(InfinityTarget target) => new RenderStateCollection
        {
            { RenderState.ZTest(ZTest.Equal) },
            { RenderState.ZWrite(ZWrite.Off) },
            { RenderState.Cull(FaceCull(target)) },
        };

        static RenderStateCollection MotionStates(InfinityTarget target)
        {
            RenderStateCollection states = EqualDepthStates(target);
            states.Add(RenderState.Stencil(new StencilDescriptor { Ref = "5", Comp = "Always", Pass = "Replace" }));
            return states;
        }

        // Translucent stages blend color in target 0 and overwrite reactive, motion and motion metadata.
        static RenderStateCollection TranslucentStates(InfinityTarget target)
        {
            string color;
            switch (target.blendMode)
            {
                case InfinityBlendMode.Premultiply: color = "Blend 0 One OneMinusSrcAlpha"; break;
                case InfinityBlendMode.Additive: color = "Blend 0 SrcAlpha One"; break;
                default: color = "Blend 0 SrcAlpha OneMinusSrcAlpha"; break;
            }
            return new RenderStateCollection
            {
                { RenderState.ZTest(ZTest.LEqual) },
                { RenderState.ZWrite(ZWrite.Off) },
                { RenderState.Cull(FaceCull(target)) },
                { RenderState.Blend(color) },
                { RenderState.Blend("Blend 1 One Zero") },
                { RenderState.Blend("Blend 2 One Zero") },
                { RenderState.Blend("Blend 3 One Zero") },
            };
        }

        static readonly RenderStateCollection MetaStates = new RenderStateCollection
        {
            { RenderState.Cull(Cull.Off) },
        };

        static readonly RenderStateCollection DecalStates = new RenderStateCollection
        {
            { RenderState.ZTest(ZTest.Always) },
            { RenderState.ZWrite(ZWrite.Off) },
            { RenderState.Cull(Cull.Front) },
            { RenderState.Blend("Blend 0 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha") },
            { RenderState.Blend("Blend 1 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha") },
            { RenderState.Blend("Blend 2 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha") },
        };

        // --------------------------------------------------------------------------------------------------------
        // Includes

        static IncludeCollection Includes(string pregraph, string pass)
        {
            var includes = new IncludeCollection
            {
                { kCore, IncludeLocation.Pregraph },
            };
            if (pregraph != null) includes.Add(pregraph, IncludeLocation.Pregraph);
            // Last pre-graph include: the Shader Graph hooks see every resource the pass declared.
            includes.Add(kGraphFunctions, IncludeLocation.Pregraph);
            includes.Add(kVaryings, IncludeLocation.Postgraph);
            includes.Add(pass, IncludeLocation.Postgraph);
            return includes;
        }

        // Custom interpolators: copied into SurfaceDescriptionInputs, and from the vertex description in BuildVaryings
        // (InfinitySGVaryings.hlsl calls CustomInterpolatorPassThroughFunc).
        static readonly CustomInterpSubGen.Collection CustomInterpolators = new CustomInterpSubGen.Collection
        {
            CustomInterpSubGen.Descriptor.MakeBlock(CustomInterpSubGen.Splice.k_spliceCopyToSDI, "output", "input"),
            CustomInterpSubGen.Descriptor.MakeFunc(CustomInterpSubGen.Splice.k_splicePreSurface, "CustomInterpolatorPassThroughFunc", "Varyings", "VertexDescription", "CUSTOMINTERPOLATOR_VARYPASSTHROUGH_FUNC", "FEATURES_GRAPH_VERTEX"),
        };

        // --------------------------------------------------------------------------------------------------------
        // Passes

        static PassDescriptor Pass(string lightMode, string referenceName, BlockFieldDescriptor[] pixelBlocks, FieldCollection requiredFields,
            RenderStateCollection renderStates, PragmaCollection pragmas, DefineCollection defines, IncludeCollection includes, bool useInPreview)
        {
            return new PassDescriptor
            {
                displayName = lightMode,
                referenceName = referenceName,
                lightMode = lightMode,
                useInPreview = useInPreview,
                passTemplatePath = InfinityTarget.kTemplatePath,
                sharedTemplateDirectories = InfinityTarget.kSharedTemplateDirectories,
                validVertexBlocks = VertexBlocks,
                validPixelBlocks = pixelBlocks,
                structs = Structs,
                requiredFields = requiredFields,
                fieldDependencies = FieldDependencies,
                renderStates = renderStates,
                pragmas = pragmas,
                defines = defines,
                keywords = new KeywordCollection(),
                includes = includes,
                customInterpolators = CustomInterpolators,
            };
        }

        public static PassDescriptor ShadowPass(InfinityTarget target) =>
            Pass(kShadowPass, "SHADERPASS_SHADOWS", AlphaBlocks, ShadowFields, DepthOnlyStates(target), Instanced, Defines(target), Includes(null, kDepthOnly), false);

        public static PassDescriptor ShadowCaster(InfinityTarget target) =>
            Pass(kShadowCaster, "SHADERPASS_SHADOWS", AlphaBlocks, ShadowFields, DepthOnlyStates(target), Instanced, Defines(target), Includes(null, kDepthOnly), false);

        public static PassDescriptor DepthPass(InfinityTarget target) =>
            Pass(kDepthPass, "SHADERPASS_DEPTH", AlphaBlocks, PositionFields, DepthOnlyStates(target), Instanced, Defines(target), Includes(null, kDepthOnly), true);

        public static PassDescriptor GBufferPass(InfinityTarget target) =>
            Pass(kGBufferPass, "SHADERPASS_GBUFFER", LitBlocks, SurfaceFields, GBufferStates(target),
                With(new PragmaCollection { Instanced, BakedLightingKeywords }, "multi_compile _ _DBUFFER"),
                Defines(target), Includes(kBakedLighting, kGBuffer), true);

        public static PassDescriptor ForwardPass(InfinityTarget target) =>
            Pass(kForwardPass, "SHADERPASS_FORWARD", LitBlocks, SurfaceFields, EqualDepthStates(target),
                With(new PragmaCollection { Instanced, BakedLightingKeywords }, "multi_compile _ INFINITY_FORWARD_AO", "multi_compile _ INFINITY_FORWARD_CONTACT_SHADOW"),
                Defines(target), Includes(kSurfaceLighting, kForward), true);

        public static PassDescriptor MotionPass(InfinityTarget target) =>
            Pass(kMotionPass, "SHADERPASS_MOTION", AlphaBlocks, PositionFields, MotionStates(target), Instanced, Defines(target), Includes(null, kMotion), false);

        public static PassDescriptor TranslucentDepthPass(InfinityTarget target) =>
            Pass(kTranslucentDepthPass, "SHADERPASS_TRANSLUCENT_DEPTH", AlphaBlocks, PositionFields, DepthOnlyStates(target), Instanced, Defines(target), Includes(null, kDepthOnly), false);

        public static PassDescriptor TranslucentT0Pass(InfinityTarget target) =>
            Pass(kTranslucentT0Pass, "SHADERPASS_TRANSLUCENT_T0", LitBlocks, SurfaceFields, TranslucentStates(target),
                With(Instanced, "multi_compile _ _VOLUMETRIC_FOG", "multi_compile _ _AERIAL_PERSPECTIVE"),
                Defines(target), Includes(kTranslucent, kTranslucentPass), false);

        public static PassDescriptor TranslucentT1Pass(InfinityTarget target) =>
            Pass(kTranslucentT1Pass, "SHADERPASS_TRANSLUCENT_T1", LitBlocks, SurfaceFields, TranslucentStates(target),
                With(Instanced, "multi_compile _ _REFRACTION_PYRAMID"),
                Defines(target), Includes(kTranslucent, kTranslucentPass), false);

        public static PassDescriptor TranslucentT2Pass(InfinityTarget target, BlockFieldDescriptor[] pixelBlocks) =>
            Pass(kTranslucentT2Pass, "SHADERPASS_TRANSLUCENT_T2", pixelBlocks, PositionFields, TranslucentStates(target), Instanced,
                Defines(target), Includes(kTranslucent, kTranslucentPass), true);

        public static PassDescriptor Meta(InfinityTarget target) =>
            Pass(kMeta, "SHADERPASS_META", MetaBlocks, MetaFields, MetaStates, NotInstanced, Defines(target), Includes(null, kMetaPass), false);

        public static PassDescriptor DBufferPass(InfinityTarget target) =>
            Pass(kDBufferPass, "SHADERPASS_DBUFFER", DecalBlocks, PositionFields, DecalStates, NotInstanced, new DefineCollection(), Includes(null, kDecalPass), false);
    }
}
