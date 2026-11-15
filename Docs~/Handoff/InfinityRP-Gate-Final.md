# 最终闸门：B8d–B9 与全量验收

分支 `claude/kind-knuth-d599st`。画面基线是 `1a357c0`（B8a 关闭时的提交，Spazon 的 AO 约 0.93，间接光正常）。通用流程、判定标准和回报模板见 `InfinityRP-Local-Verification-Handoff.md`。

## 状态

- `e818357`：画面、GC、A/B 通过。退回两处：player 脚本编译失败；测试 compute 无 keyword 变体编译失败。
- `5eb2ee2`：两处已修。Editor 编译、`INFINITY_UNITY_CLI` 编译、EditMode（845：833 / 11 / 1）、`LightShapeTests`、静态检查通过。player 构建成功，但运行时黑屏，Runtime DLL 中还残留 `ComputeDebugView`。
- 本轮修正（提交号见 prompt）：
  - `093053f`：`ProfilingSampler.Get` 在非 development player 里返回 null，RenderGraph 录制 pass 时读它的 `name`，每帧在第一个 compute pass 抛 NRE，这就是黑屏的原因。pass 名改为只在 sampler 存在时读取；null sampler 不开、也不关 profiling scope。新测试 `RenderGraphCompileTests.NullSampler_RecordsAnUnnamedPassAndOpensNoScope`。
  - R16 清扫：Rendering Debugger 整体移进 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`：
    - 面板、`EDebugView` 与每相机的 debug view；
    - 光照计数、mesh 计数与 mesh backend 覆盖；
    - pass timings；
    - SSR、SSGI、TAA 的 debug kernel 与诊断输出；
    - `CustomSamplerId.ComputeDebugView`。

    静态检查 `diagnostics` 现在也覆盖这些符号。

## 头号怀疑

1. **Editor 里的 Rendering Debugger 与 debug view**：Editor 行为应当不变。SSR 和 TAA 的诊断 kernel 只在 debug view 或 capture 请求时才启用。
2. **player 画面**：黑屏修好后，Spazon 的 player 画面应与 Editor Game 视图一致，包括面光源、管状光和太阳阴影的软硬（构建期冻结的光源形状）。
3. **Metal MRT 错误**：上一轮 `Player.log` 里有 `render target return type SV_Target1 has duplicated indices pair (<0, 0>)`（SV_Target1–5）。这可能是黑屏异常路径的连带结果。若仍出现，需要它前面几行带 shader 名和 pass 名的日志。

## 编译与测试

1. 用 `Tools/RefreshUnityEditor.sh` 刷新，必须输出 `REFRESH_STABLE=1`。新增日志窗口里 shader error 为 0。
2. 默认 Editor 编译与 `INFINITY_UNITY_CLI` 编译均为 0 error。release 脚本编译以 player 构建的结果为准：在这个 Unity 版本上，单独调用 `PlayerBuildInterface.CompilePlayerScripts` 会在 UGUI 的 `InputField.cs` 报错（缺少 `Unity.Scripting.LifecycleManagement`），不能用作证据。Editor 内的 `AssemblyBuilder` 引用 Editor 版 `UnityEngine`，也证明不了 release 能编译。
3. `static_check.py` 结果为 0 error，`test_static_check.py` 全部通过。
4. 全量 EditMode，预期总数 846：834 通过 / 11 失败 / 1 跳过。11 个失败是已知集合：ShaderStrippingTests 10 个，加 `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`。

## Editor 回归（R16 清扫）

- Spazon，Play 模式，固定机位，用图形 transfer capture 截 Game 视图。整帧均值应约等于上一轮的 21.16。
- Rendering Debugger：
  - 依次选 `SSRRayHit`、`TAAConfidence`、`AO` 三个 debug view，各截一张，应有内容，不是品红，也不是全黑。
  - Infinity Mesh 面板的实体数、命令数不为 0，requested/actual backend 有值。
  - Infinity Lighting 面板的光源计数不为 0。
  - 不保存任何设置。

## 构建（需要用户授权）

- macOS release player，Development Build 关闭，scenes 只给 Spazon，不修改 EditorBuildSettings。
- 项目设置是 IL2CPP，本机只装了 Mono：构建前在内存里把 Standalone 的 scripting backend 改为 Mono2x，构建后改回 IL2CPP，不调用 SaveAssets。Editor 退出后，若 `ProjectSettings.asset` 变脏，先 diff 确认，再用 git 还原。
- 回报：
  - 构建结果；
  - `InfinityRP shader stripping:` 那一行；
  - `Data/Managed` 中没有 `Unity.RenderPipelines.Infinity.Validation.dll`；
  - Runtime DLL 的 `strings` 输出里，以下名字整行匹配（`grep -x`）都搜不到：`RenderCaptureService`、`RenderFaultValidation`、`ComputeDebugView`、`InfinityDebugDisplaySettings`、`EDebugView`、`DebugViewCatalog`。
- player 窗口模式启动（1445×813），截图后与 Editor 同一场景的截图对比。`Player.log` 不得有异常；若有 Metal 的 `xlatMtlMain` 错误，回报第一条错误及其前 5 行。

## 沿用的结果

- `e818357`：
  - 画面：Spazon 固定、移动、TAA 第 30 帧，BoxMatix，cookie/探针开关，SSGI AO 开关，大气缓存，DebugView 六张，均与 `1a357c0` 一致。
  - GC：三个窗口 `frameAllocations` 全为 0。
  - A/B 已结案：B1 比 A1 快 3.0%，RETURN-PERF 要求两对 B 都慢 5% 以上，第一对已排除。
- `5eb2ee2`：
  - player 构建成功；
  - shader 编译日志 0 error；
  - stripping：kept 10751，compute variants kept 319；
  - 没有 Validation DLL；
  - `RenderCaptureService`、`RenderFaultValidation` 已不在 Runtime DLL 中。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/final3/`。验收后删除，写明回收的空间；player build 产物也删除。
