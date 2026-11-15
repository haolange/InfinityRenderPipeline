# 最终闸门：B8d–B9 与全量验收

上一轮（`e818357`）退回两处：player 脚本编译失败（`LightShape` 读取 Editor-only 的 `Light` 成员），以及测试 compute 的无 keyword 变体编译失败。本轮只复验受影响的部分，其余结果沿用上一轮（见“沿用的结果”）。

分支 `claude/kind-knuth-d599st`。对比基线是 `1a357c0`，即 B8a 关闭时的提交。在这个基线上 Spazon 已是正确画面：AO 约 0.93，间接光正常。通用流程、判定标准和回报模板见 `InfinityRP-Local-Verification-Handoff.md`。

## 改了什么

- `e14ac4a`：没有 sun record 的相机，大气的太阳辐亮度为 0，不再由白色假太阳照亮天空。
  **预期画面变化**：没有 directional light 的场景，天空与天空 SH 环境光变黑。
- `982eee7`：
  - B8d：光源记录从 `VisibleLight` 快照读位置、朝向、最终颜色、range、spotAngle。
  - B8e：cookie atlas 与探针 atlas 改为 `INFINITY_LIGHT_COOKIES` / `INFINITY_REFLECTION_PROBES` 两个 keyword 开关，SSGI 的 AO 改为局部 keyword `SSGI_AMBIENT_OCCLUSION`；不再绑定 `whiteTexture`、空探针 atlas 或拿深度纹理冒充 AO。
  - B8g：opaque 与 fogged scene color 由各自的合成 pass 决定；history 拷贝按 feature 门控。
- `3d5f036`：静态检查新增 `silentfallback`。
- `96002a3`（B8f）：DebugView 改为查表，录制只存在于 Editor 和 Development 构建。
- `1ee1046`：大气两个缓存 key（`AtmosphereViewKey`、`AtmosphereIBLKey`）加入太阳辐亮度。改 directional light 的强度或颜色后，天空、天空环境光和 aerial perspective 必须同帧跟着变；删除方向光后天空变黑，不得残留旧表。测试里的 keyword 启用方式同步修正。
- `4a1aedb`：删除零调用者的 `RGBuilder.SnapshotRecordingArray`；`MeshViewMotionHistory` 改名 `MeshMotionHistory`（含测试类，用例数不变）。
- `e3310e4`：静态检查新增 `scalarupload`，docstring 补齐，`respath`、`sgstrings` 补测试。
- B9 文档：`341d433`（CHANGELOG、FeatureStatus、Manual）、`001a80c`（Style），`71a2677`（ARCHITECTURE/AGENTS 按代码核对）。
- `e818357`：`scalarupload` 计入复数形式。
- 本轮修正（提交号见 prompt）：
  - `LightShapeUtility` 只在 Editor 读写 `areaSize`、`shadowAngle`、`lightmapBakeType`，player 只写 `type`、`shapeRadius`。
  - 新增 `LightShapeBuildProcessor`：player build 时把每个场景光源在 Editor 里推导出的形状冻结进 `InfinityAdditionalLightData`；没有该组件的光源补一个 `maxDrawDistance = 0` 的组件，渲染与无组件时相同。只改 build 副本，不改源场景。
  - `Compute_CubemapArraySamplingContract` 无条件定义 `INFINITY_REFLECTION_PROBES`，测试不再启用 keyword。
  - 新增测试 `LightShapeTests.PlayerBuild_FreezesTheShapeOfEverySceneLight`。

## 头号怀疑

1. **shader 变体**：
   - `INFINITY_LIGHT_COOKIES`、`INFINITY_REFLECTION_PROBES` 开与关两种状态都要编译通过。涉及 Deferred、体积雾、体积云、InfinityLit、InfinityLit-Instanced 和 ShaderGraph 的 forward pass。
   - `SSGI_AMBIENT_OCCLUSION` 开与关都要编译通过。
   - Editor 只编译用到的变体，所以全部变体以 player build 的编译结果为准（见“构建”）。
2. **release 编译**：B8f 把 DebugView 放进了 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`，release 配置下不得有未定义符号。
3. **player 场景光源的形状**：Spazon 的 player 画面应与 Editor 的 Game 视图一致；若面光源、管状光或太阳软阴影角度不一致，说明构建处理器没有生效。

## 编译与测试

1. 用 `Tools/RefreshUnityEditor.sh` 刷新，必须输出 `REFRESH_STABLE=1`。
2. 默认 Editor 编译、`INFINITY_UNITY_CLI` 编译、release player 脚本编译，三者都要 0 error。release 编译用 `PlayerBuildInterface.CompilePlayerScripts`（`target = StandaloneOSX`，`options = ScriptCompilationOptions.None`，输出到 intermediate）。Editor 内的 `AssemblyBuilder` 引用 Editor 版的 `UnityEngine` 程序集，Editor-only 成员照样能解析，证明不了 release 能编译。
3. `static_check.py` 结果为 0 error，`test_static_check.py` 全部通过。
4. 全量 EditMode，预期总数 845：833 通过 / 11 失败 / 1 跳过。11 个失败是已知集合：ShaderStrippingTests 10 个，加 `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`。

## 画面（1445×813，对比 `1a357c0`）

- Spazon：固定机位、移动机位、TAA 第 30 帧，应与 `1a357c0` 一致。
- BoxMatix：先确认场景里有没有 directional light。
  - 有：画面应与 `1a357c0` 一致。
  - 没有：天空与环境光变黑是预期结果，其余应一致。
- cookie 与反射探针：用有 light cookie 和反射探针的样本，在相机 frame settings 里分别开、关 LightCookies 和 ReflectionProbes。开的时候与 `1a357c0` 一致，关的时候 cookie 与探针反射消失，Console 无 "property not set"。
- SSGI 加 AO：关闭 AmbientOcclusion 时 SSGI 照常工作，开启时与 `1a357c0` 一致。
- 大气缓存：Spazon 进入 Play 模式后，把 directional light 的 intensity 改为一半，下一帧天空与环境光必须变暗（截前后两张）。只在 Play 模式里改，退出 Play 即还原，场景不会变脏。
- DebugView：AO、SSGI、IndirectDiffuse、CascadeShadowAtlas、LightComplexity、ReflectionProbeAtlas 各截一张，与 `1a357c0` 一致。

## 构建（需要用户授权）

- 做一次 macOS release player build，Development Build 关闭。项目设置是 IL2CPP，本机只装了 Mono：构建前把 Standalone 的 scripting backend 临时改为 Mono2x，构建后用 git 还原 `ProjectSettings.asset`，重新导入，确认读回 IL2CPP 且该文件 git status 干净。
- shader 编译日志无错误，`INFINITY_*` 与 `SSGI_AMBIENT_OCCLUSION` 的变体都被编译。回报 `InfinityRP shader stripping:` 那一行的原文。
- `Data/Managed`（Mono）中没有 `Unity.RenderPipelines.Infinity.Validation.dll`。Runtime DLL 中搜不到 `RenderCaptureService`、`ComputeDebugView`、`RenderFaultValidation` 三个名字。
- player 能启动并出画（Spazon 场景，截一张图）。

## 性能

- GC：Spazon、Play、GPU Indirect，暖机 300 帧后录三个窗口，`frameAllocations` 全为 0。
- A/B 已结案，不再跑：`e818357` 上 A1 = 1.0855 ms、B1 = 1.0527 ms（B 快 3.0%）、A2 = 1.1647 ms。RETURN-PERF 要求两对 B 都慢 5% 以上，第一对已排除。本轮改动只涉及光源形状的 Editor/player 分支，不在逐帧热路径上。

## 沿用的结果（`e818357`，本轮不重做）

- 画面：Spazon 固定、移动、TAA 第 30 帧，BoxMatix，cookie/探针开关，SSGI AO 开关，大气缓存，DebugView 六张，均与 `1a357c0` 一致。
- GC：三个窗口 `frameAllocations` 全为 0。
- `static_check.py` 0 error，`test_static_check.py` 44 OK。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/final/`。验收后删除，写明回收的空间；player build 产物也删除。
