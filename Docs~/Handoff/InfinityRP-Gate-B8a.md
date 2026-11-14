# 闸门 B8a：本地验收清单（pass 参数与相机常量进 constant buffer）

分支 `claude/kind-knuth-d599st`。上一闸门 B7d 在 `2133e38` 判 PASS，它是本闸门的对比基线：
- EditMode 829 / 11 / 1，11 个失败是已知集合：ShaderStrippingTests 10 个，VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True) 1 个；
- GC 三个窗口均为 0；
- Spazon 六轮 marker 忙碌时间总和中，`2133e38` 三次为 1.996、1.503、1.420 ms。
通用流程、判定标准与回报模板见 `InfinityRP-Local-Verification-Handoff.md`；本文只列本闸门特有的内容。云端无法编译 C# 与 shader，本批全部 HLSL 与 C# 改动在本地编译前都是未验证的。

## 改了什么（三个提交）

1. `9734ee6`：compute 从 `ViewUniform` 矩阵推导相机位置与朝向（`ViewPositionWS`、`ViewForwardWS`）；9 个 pass 删除逐 dispatch 上传的 `_WorldSpaceCameraPos`、`SSR_Camera*`、`SSGI_Camera*`。
2. `33e23a0`：`ViewUniform` 新增 `View_DepthParameters`（near、far、orthographic、reversed Z）与 `View_JitterUV`；删除 SSR、SSGI、GTAO、TAA 各自的 `ScreenSpaceDepthParams` 与 TAA、temporal color 的 jitter 上传。
3. 本提交：
   - **pass 常量**：每个 compute pass 的每图参数改为一条 GPU-RECORD，record 时 `WriteConstants` 写入，执行时 `SetComputeConstantBufferParam` 绑定。HLSL 侧对应 `cbuffer CBV_<Name>`，成员名不变。
   - **constant buffer 的来源**：每个 pass 一个 pass-local 池化 buffer，在 pass 开始时于该 pass 所在队列上传。
   - **每相机 uniform**：阴影与光照循环常量改为两条全局 uniform，`ShadowUniform`（`ShadowSampling.hlsl`）与 `LightLoopUniform`（新文件 `Lighting/LightLoopUniform.hlsl`）。它们和 `ViewUniform` 一样，每相机在 `CullCamera` 之后写一次。`ShadowInputs` 与 `LightLoopInputs` 只剩资源绑定。
   - **删除的冗余写入**：CascadeShadowPass、LocalShadowPass 对同一批阴影常量的 `SetGlobal*` 写入，以及无读者的 `_ShadowDistance`、`_LocalShadowCount`、`g_LocalShadowSliceCount`。
   - **仍逐 dispatch 设置的值**：随 dispatch 变化的值保持逐 dispatch 设置，包括 HiZ、color pyramid、cube filter、cookie、shadow moment 的 mip/面/slice 参数，bloom mip，SSGI 两次 spatial，以及 mesh producer 每个 list 的计数。

## 头号怀疑

1. **shader 编译**：
   - 刷新后 Console 新窗口不得出现 "undeclared identifier"、"redefinition"、"Property (CBV_xxx) is not set" 或 "constant buffer size mismatch"。
   - 改过的 compute：SSR、SSGI、ScreenSpaceComposite、PyramidColor（TemporalColorMip 段）、GTAO、CombineLUTs、PostProcessing、SuperResolution、TAA、AtmosphericLUT、FogComposite、HalfResDownsample、BurleySubsurface、LightTileMask、DeferredShading、ContactShadow、VolumetricFog、VolumetricCloud、DebugView。
   - 被 raster 与 ShaderGraph 包含的库：`ShadowSampling.hlsl`、`Lighting.hlsl`、`LightLoop.hlsl`、`ReflectionProbes.hlsl`、`ScreenSpaceDepth.hlsl`、`TemporalColorSource.hlsl`。
2. **async 队列上的 constant buffer**：Unity 托管层不禁止在 AsyncCompute command buffer 上调用 `SetBufferData` 与 `SetComputeConstantBufferParam`（已核对 UnityCsReference），native 侧未验证。本机 Metal 不支持 async，无法覆盖；有 DX12 或 Vulkan 机器时补跑一次，并记录 Console。
3. **stride 16 的 constant buffer**：pass-local buffer 是 `Target.Constant`、`count = 字节数 / 16`、`stride = 16`。若某后端报错，回报原文。
4. **全局 uniform 的时序**：阴影与光照循环常量在 `CullCamera` 之后、`RecordCamera` 之前写入相机 command buffer。Scene view、Game view 与反射探针相机都要出画。

## 编译

1. 默认 Editor 编译（Runtime、Editor、Tests、Validation），加 `INFINITY_UNITY_CLI` 的 `Editor/Cli`。
2. release 脚本编译沿用 B6 闸门的 AssemblyBuilder 方案。
3. 刷新后 Console 新窗口的全部 shader 错误、警告与 Burst 错误。

只允许本地机械修复：漏 `using`、include 路径、`.meta`。其余情况 RETURN，附第一条错误原文。

## 必跑测试

全量 EditMode，与基线 829 / 11 / 1 对比：失败集合不得扩大，新增失败逐个附第一条断言。点名：
- ShaderInitializationGpuTests（DepthConversion 4 例、TraceContract、SurfaceContract）
- GtaoTemporalGpuTests、ScreenSpaceNumericContractTests
- TemporalResolveGpuTests、TemporalColorSourceGpuTests
- StochasticSSRNumericTests、StochasticSSGINumericTests、SSGITemporalResponseGpuTests
- VisualContractGpuTests、ScreenSpaceTraceContractGpuTests
- DeferredLightLoopGpuTests、ShadowSamplingGpuTests、LightTileMaskGpuTests
- ColorOutputGpuTests、DefaultVolumeOutputTests、SubsurfaceGpuTests
- 全部 RenderGraph 测试，以及新的 static check 测试 `test_cbuffer_record_requires_matching_offsets_and_whole_registers`

## 画面（1445×813，对比 `2133e38`）

Spazon 与 `2133e38` 的差异根因未定：`2133e38` 的 IndirectDiffuse 与 IndirectSpecular 都是 0.00，候选为 23.13 / 8.52；共同因子只有 AO，或天空 SH 与 GGX prefilter 同时为零。BoxMatix 要求与 `2133e38` 一致。

- **Spazon、BoxMatix 基础画面**：Play mode 固定机位、移动机位、TAA 第 30 帧，逐项对比 SSR、SSGI、GTAO、接触阴影、体积雾与体积云、大气天空与雾、bloom 与曝光、tonemap 与 color grading、次表面散射。
- **阴影**：cascade 阴影、带阴影的点光与聚光、shadowmask 距离模式，前向半透明物体上的阴影与光照也要检查，因为它们走 raster 全局路径。
- **反射探针**：探针反射正常，探针烘焙或实时探针相机能出画。
- **SR 模式**：开超分（SuperResolution）跑一次 TAA 对比。
- **正交相机**：一个正交相机样本，验证 SSR 与深度线性化。

## 性能

- GC：Spazon、Play、只开 Game view、GPU Indirect，暖机 300 帧后录三个 runtime probe 窗口，`frameAllocations` 应全为 0。
  非零帧附 GC.Alloc 调用栈。本批在 record 路径新增了 `WriteConstants` 与 pass 的 constants 列表，是头号怀疑。
- CPU 与 GPU：A = `2133e38`，B = 本提交，按 A B A B 跑四轮，每轮 300 帧、冷却 60 秒，工作副本干净时才 checkout，结束回到本提交。
  每轮记录主相机 `RecordRG`、`ExecuteRG` 的 CPU 均值和 pass 级 marker 忙碌时间总和（口径同 B7d）。
  只有两对 B 都比 A 慢 5% 以上，且差值落在 RenderGraph 的 constants 上传路径（`PrePassExecute`）或改过的 pass 上时，才判 RETURN-PERF。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/<run-id>/`，验收后删除，并写明回收的空间。
