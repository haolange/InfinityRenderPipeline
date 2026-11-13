# 闸门 B6f-2：本地验收清单

分支 `claude/kind-knuth-d599st`，上一个闸门 `d29fe57`（B6，PASS）。通用流程、判定标准与回报模板见 `InfinityRP-Local-Verification-Handoff.md`；
本文只列这一闸门特有的内容。云端无法编译 shader，本批所有 HLSL 改动在此之前都是未验证的。

## 提交

| 提交 | 内容 |
|---|---|
| 0eb4f36 | 相机矩阵改为每相机一个 `ViewUniform` constant buffer（`SetBufferData` + `SetGlobalConstantBuffer`）；11 个 compute 删除同名不同义的本地矩阵与逐 dispatch 拷贝，改读精确变体名；shadow caster 改读 `_ShadowCasterViewProj`；9 个 GPU 测试改经 `ViewUniformBinding` 绑定 |
| 8ed87e3 | 修复 `AerialPerspectiveLUT` 缓存 key 不含视线方向与投影（原地转身后合成仍用旧朝向的 aerial 体素） |

## 头号怀疑

compute shader 能否读到 `SetGlobalConstantBuffer` 绑定的全局 constant buffer（HDRP 依赖同一机制，但本仓库此前对 compute 一律逐 shader 显式绑定）。
它若不成立，EditMode 中 `ViewUniformBinding` 相关的 GPU 测试会失败，画面上所有 compute 重建世界坐标的效果（延迟光照、SSR、SSGI、GTAO、接触阴影、
雾、云、大气合成、SSS）会整体错误。出现即 RETURN，附一张 Frame Debugger 截图（任一 compute dispatch 的 `ViewUniform` 绑定状态）与失败测试输出，
不要本地改成逐 dispatch 绑定。

## 编译

1. 默认 Editor 编译（Runtime、Editor、Tests、Validation）。
2. 定义 `INFINITY_UNITY_CLI` 后再编译（`Editor/Cli`）。
3. release 脚本编译：`PlayerBuildInterface.CompilePlayerScripts`（同 B6 闸门的参数），回报全部错误。
4. Shader：刷新后 Console 新窗口里全部 shader 编译错误与警告。重点：`Shaders/Library/ViewUniform.hlsl`（新）、`ShaderVariables.hlsl`、
   `ShadowCaster.hlsl`、`InfinityLit.shader`、`InfinityLit-Instanced.shader`、`TerrainLit.shader`/`TerrainLitBase.shader`、任一 Shader Graph
   材质（含 shadow caster pass），以及上表的 11 个 compute 与 `Tests/Editor/Fixtures/ShaderInitializationContract.compute`。

允许本地机械修复：漏 include、include 相对路径错误、`.meta` 缺失。每处写进报告并附 diff。

## 必跑测试

全量 EditMode，与 B6 结果（833 通过 / 11 失败 / 1 忽略；10 个 ShaderStrippingTests 与 1 个 VisualContractGpuTests 为既有失败）对比。重点：
StochasticSSRNumericTests、StochasticSSGINumericTests、SSGITemporalResponseGpuTests、GtaoTemporalGpuTests、ScreenSpaceNumericContractTests、
ShaderInitializationGpuTests、DeferredLightLoopGpuTests、SubsurfaceGpuTests、CameraFrameStateTests、TemporalAntiAliasingTests、
LightBinningTests、AtmosphereParameterTests、AtmosphereCacheTests、RenderCaptureTests。静态检查 0 error，`test_static_check.py` 全部通过。

## 画面（1445×813，与 B6 结果同机位比对）

Spazon、BoxMatix：Play mode 固定机位、移动机位、Scene view、TAA 第 30 帧；cascade 与 local 阴影（含点光 6 面、聚光）；SSR、SSGI、GTAO、
RTAO（若可用）、接触阴影、体积雾、体积云、大气天空与远景大气透视、SSS 材质、DebugView 的光源复杂度视图。全部应与 B6 一致（无结构差异）。

预期变化（需确认）：
- 原地转身 180°：远景大气透视随朝向更新（修复前保持旧朝向，直到相机跨过 1 m 格子）。
- 相机静止时 Frame Debugger 中 `ComputeAtmosphericLUT` 不逐帧出现；转身或改 FOV 时出现。
- Frame Debugger 中 raster 与 compute draw/dispatch 的相机矩阵来自 `ViewUniform` constant buffer，不再是逐个 matrix 属性。

## 性能

GC：Spazon Play mode 暖机 300 帧后 runtime probe 一个窗口，frameAllocations 应仍为 0（`SetBufferData` 使用复用的托管数组）。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/<run-id>/`，验收后删除并写明回收空间。
