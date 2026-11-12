# 闸门 B5：本地验收清单（B3–B5）

分支 `claude/kind-knuth-d599st`，HEAD `082a41d`，上一个闸门 `8da744f`（B2，已 PASS）。通用流程、判定标准和回报模板见
`InfinityRP-Local-Verification-Handoff.md`；本文只列这一闸门特有的内容。用户已授权本闸门运行 `BuildScriptsOnly`。

## 提交

| 提交 | 内容 |
|---|---|
| 6342923、b4f6e10 | B3a：compute kernel / local keyword / blit pass 索引在资源加载时解析成表（PipelineKernels、BlitShaderPasses、CaptureKernels）；FeatureRegistry 资源支持只算一次；Mesh 计算 shader 缺 kernel 时构造失败 |
| 0ebf426 | B3b：357 个从不被重新赋值的 static 字段改为 static readonly |
| d654da3 | B3c-1：profiling sampler 改为静态字段与固定表，删除 CachedSampler |
| a00b193、9153c62 | B3c-2：record/execute 中的 PropertyToID 全部提为静态 ID；同名属性只留一个 C# ID（PassShaderIDs）|
| 37c61dd、6f24f30 | B4：资源池 handle 带获取代数；graphics fence 为构造前置条件；HistoryCache 改为向 ResourcePool 借还 |
| b56c695 | B5a：RG handle 有效 ⇔ generation≠0；单一校验型 Resolve；资源身份一种判定 |
| 4211dd7 | B5b+B5e：encoder 共用 RGCommandCore、经图自己的 factory 解析；删除全局 factory、隐式转换、string/params 重载；compute keyword 录入 command buffer |
| d99c7e7 | B5c：CameraRendererLists（RendererListParams + 资产 batching 开关）；renderer list 提交只认本图创建的列表 |
| 67b2828 | B5d：删除 alias slot 计划、环/重复校验；segment 拆分由池的释放事件推导；字节估算一份 |
| d644956 | B5e：pass handle 按数据类型泛型化；未设 execute 在 Dispose 时报错 |
| bdc52a0 | B5 复审修正（删除只被测试读取的成员；GetProducer 校验代际）|
| 13df179 | B5f：scoper 归图构建器；ResolvedSceneColor 角色 key |
| bdbf8c6 | B5g：Buffer 统一为 GraphicsBuffer（BufferDescriptor.target；RG 内 buffer 只有一种表示）|
| 69844ac | B5 复审修正：compute encoder 补回 SetBufferData；缺 execute 改在编译期报错；segment 规划按组提升后的创建位置；去掉 m_RGScoper 别名 |
| c3ba454 | 上一轮 RETURN 修正：Unity 6000.6 中 `DrawingSettings.enableDynamicBatching` 为错误级废弃，不再写它 |
| 58af7e4 | 删除资产字段 `enableDynamicBatch` 与 Inspector 勾选框（用户批准的 schema 变更）|
| 5ba5039 | Docs~/Handoff：本目录（只有文档）|
| 082a41d | 第二次 RETURN 修正：ResourcePoolLifecycleTests（提交必带完成 fence）、HistoryCacheCommitTests（丢弃的获取归还给池）、RenderGraphFailureTests.TargetMrtClear（在图中编译后再绑定目标）三处测试改测新契约 |

## 上一轮的本地改动

第二轮验收时在本地对 `ResourcePoolLifecycleTests` 做的机械修复（去掉 `Submitted` 的 `false` 参数）已被 `082a41d` 取代。拉取前先丢弃该本地改动。

## 编译（三种配置都必须做）

1. 默认 Editor 编译（Runtime、Editor、Tests、Validation）。
2. 定义 `INFINITY_UNITY_CLI` 后再编译（`Editor/Cli`）。
3. release：`BuildPipeline.BuildPlayer` + `BuildOptions.BuildScriptsOnly`（已授权），不定义 `DEVELOPMENT_BUILD`，目标为当前平台，输出到
   intermediate 任务目录。没有独立任务镜像时，在持有项目的这个 Editor 进程内调用（经 Unity CLI 或菜单），不要另起 batch 进程或第二个 Editor；
   不改 Player 设置、不切平台、不保存资产。记录输出路径与大小，验收后删除构建产物。B2 闸门里 AssemblyBuilder 的 CS0012（GraphicsUtility.cs:15 引用 Editor 版 Core RP）是
   harness 问题，这次以 BuildScriptsOnly 的结果为准；若仍报 CS0012，附完整错误与引用链回报。

允许本地机械修复：漏 `using`、批次内改名漏改、`.meta` 缺失、设计意图唯一的签名不匹配。每处写进报告并附 diff。
编译失败时，回报该程序集的**全部**编译错误（不只第一条），以便一次修完。

## 序列化 schema 变更（本闸门唯一获批项）

`InfinityRenderPipelineAsset.enableDynamicBatch` 已删除。不得保存资产来"验证"；若要验证保存行为，先做持久备份，保存后 diff 只允许少
`enableDynamicBatch` 一行，再保存一次字节相同；其余字段、GUID、local ID 必须不变。Inspector 不再显示 "Dynamic Batcher"。

## 重点怀疑（编译）

- B5g 引入的 GraphicsBuffer 重载：`GraphicsBuffer.SetData(Array,int,int,int)`、`CommandBuffer.SetBufferData(GraphicsBuffer, Array, int, int, int)`、
  `AsyncGPUReadback.Request(GraphicsBuffer)`、`ComputeShader.SetBuffer(int, string, GraphicsBuffer)`、`MaterialPropertyBlock.SetBuffer(int, GraphicsBuffer)`；
  indirect args 用 `GraphicsBuffer.Target.IndirectArguments | Structured`，确认 Metal 接受该组合并能同时作为 RWStructuredBuffer 绑定。
- Unity API 签名：`ScriptableRenderContext.CreateRendererList(ref RendererListParams)`、`RendererListParams(CullingResults, DrawingSettings, FilteringSettings)`、
  `CommandBuffer.SetKeyword(ComputeShader, in LocalKeyword, bool)`、`CommandBuffer.SetRayTracingAccelerationStructure(ComputeShader, int, int, RayTracingAccelerationStructure)`、
  `IRayTracingShader.SetAccelerationStructure(CommandBuffer, string, IRayTracingAccelStruct)`、GraphicsBuffer 相关重载（DrawMeshInstancedIndirect、
  MaterialPropertyBlock.SetBuffer、AsyncGPUReadback.Request）。
- 泛型 pass handle 的类型推断（`using (var pass = ...)`、`ShadowInputs.Read(passRef)` 等泛型辅助方法）。
- `readonly struct` encoder / handle 与 `in` 参数；`Array.Fill(T[], T, int, int)`。
- `CameraUniform` 的 ID 改为 internal 后的访问；`PassShaderIDs` 中 `Capture_SSRTileClass` 位于 `#if`。

## 运行期新增的异常（出现即定位第一个 owner 回报，不要压掉）

| 消息片段 | 含义 |
|---|---|
| `received a resource from a previous graph` | 有调用方持有上一帧/另一图的 RG handle |
| `is a texture-view property; bind a graph texture` | 某绑定点把非图纹理绑到登记了 view 的属性且未显式给 view |
| `was not created for this pass` / `uses a renderer list from another graph` | renderer list 句柄失效或未被该 pass 使用 |
| `was recorded without an execute function` | 某个存活 pass 录制后没设 execute（编译期检查）|
| `returned through a handle of an earlier acquisition` | 过期的池 handle 被归还 |
| `GlobalSettings RuntimeShaders.X is not assigned` / `has no kernel` / `blitMaterial has no pass` | 资源解析阶段发现缺失（应在管线构造时抛出一次） |
| `InfinityRP requires graphics fences` | 设备不支持 graphics fence（Metal 应支持） |

## 预期变化（允许，需逐项确认）

- Frame Debugger 的 segment 数可能略有不同（segment 拆分改为由池的释放事件推导）；native render pass 分组应与基线相近。
- RenderGraph Viewer 显示 "peak live"，不再显示 physical slot。
- 关闭 SRP Batcher 时，资产上的 GPU Instancing 开关真正改变 Frame Debugger 中的批次（Unity 6000.6 已移除动态合批）。
- compute keyword 改为录进 command buffer：DeferredShading、ScreenSpaceComposite、FogComposite 的画面应一致或更正确（原先同帧多次 dispatch 可能读到错误的 keyword 状态）。
- Profiler marker 名称不变。
- history 现在从资源池借用：池统计（idle/active bytes）会包含 history。
- 资产默认开启 GPU Instancing，现在真正作用于相机 renderer list：Frame Debugger 的批次可能变化；若画面出现差异，先关掉该开关复测，再定位回报。

## 必跑测试

全量 EditMode，与基线（827 通过 / 14 失败）对比。重点：FeatureRegistryTests、MeshTransformVersionGpuTests、StochasticSSRNumericTests、
MeshDrawProfilingTests、HistoryCacheCommitTests、ResourcePoolLifecycleTests（含新的 StaleHandle 测试）、AtmosphereCacheTests、LocalShadowCacheTests、
LightCookie*/ReflectionProbe* 测试、CameraFrameStateTests、TemporalAntiAliasingTests、RenderCaptureTests、RenderGraphCompileTests（新的 segment 测试）、
RenderGraphFailureTests、RenderGraphViewerTests、RenderGraphBindingTests（新）、RGRendererListLifetimeTests、RGScoperTests、LightResourceLifetimeTests、
RenderGraphQueueGpuTests、MeshResidencySubmissionTests、MeshWorldTests、MeshOcclusionIntegrationTests。
静态检查：`python3 -B Tools/StaticCheck/static_check.py --repo <package>` 0 error；`test_static_check.py` 全部通过（已知失败已修复，现应 0 失败）。

## 画面

与基线逐场景比对（Play mode 固定机位、移动机位、Scene view、TAA 第 30 帧），并加：窗口缩放后 history 重建正常；Preview 相机；
带 targetTexture 的相机；超分开/关；debug view PreTonemapLuma。凡是表格之外的结构性差异都按 RETURN 处理并定位到提交。

## 性能与稳定性

- GC：上次闸门两侧 0 个有效样本，这次必须换方法：用 CLI 探针 `RuntimeProbeCommands` 读 `PipelineFrameMeasurement`，或 Profiler CPU 模块
  GC Alloc 列以 `InfinityRenderPipeline.Render` 为根统计；暖机 120 帧后连续 300 帧，静止与移动机位各一组，给出每帧字节数。
- 连续运行 5 分钟：池计数（estimatedAllocated/Idle/Retired）稳定，无持续增长。
- Debugger：mesh backend 分别以覆盖 Auto 与 GPUIndirect 运行，确认 Actual Backend 与 reason 正确。

## 中间数据

按交接说明放在 `<Example 项目根>/intermediate/irp-refactor/<run-id>/`；BuildScriptsOnly 的产物同样放在任务目录，验收后删除并在报告里写明回收空间。
