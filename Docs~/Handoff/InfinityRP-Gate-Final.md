# 最终闸门：B8d–B9 与全量验收

分支 `claude/kind-knuth-d599st`。对比基线是 `1a357c0`，即 B8a 关闭时的提交。在这个基线上 Spazon 已是正确画面：AO 约 0.93，间接光正常。通用流程、判定标准和回报模板见 `InfinityRP-Local-Verification-Handoff.md`。

## 改了什么

- `e14ac4a`：没有 sun record 的相机，大气的太阳辐亮度为 0，不再由白色假太阳照亮天空。
  **预期画面变化**：没有 directional light 的场景，天空与天空 SH 环境光变黑。
- `982eee7`：
  - B8d：光源记录从 `VisibleLight` 快照读位置、朝向、最终颜色、range、spotAngle。
  - B8e：cookie atlas 与探针 atlas 改为 `INFINITY_LIGHT_COOKIES` / `INFINITY_REFLECTION_PROBES` 两个 keyword 开关，SSGI 的 AO 改为局部 keyword `SSGI_AMBIENT_OCCLUSION`；不再绑定 `whiteTexture`、空探针 atlas 或拿深度纹理冒充 AO。
  - B8g：opaque 与 fogged scene color 由各自的合成 pass 决定；history 拷贝按 feature 门控。
- `3d5f036`：静态检查新增 `silentfallback`。
- B8f：DebugView 改为查表，录制只存在于 Editor 和 Development 构建。
- B9：文档收尾。

## 头号怀疑

1. **shader 变体**：
   - `INFINITY_LIGHT_COOKIES`、`INFINITY_REFLECTION_PROBES` 开与关两种状态都要编译通过。涉及 Deferred、体积雾、体积云、InfinityLit、InfinityLit-Instanced 和 ShaderGraph 的 forward pass。
   - `SSGI_AMBIENT_OCCLUSION` 开与关都要编译通过。
   - Editor 只编译用到的变体，所以全部变体以 player build 的编译结果为准（见“构建”）。
2. **release 编译**：B8f 把 DebugView 放进了 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`，release 配置下不得有未定义符号。
3. **`VisibleLight` 的属性可写**：测试辅助 `VisibleLightTestUtility.FromLight` 用对象初始化器构造 `VisibleLight`。若属性只读，编译会报错，回报原文。

## 编译与测试

1. 用 `Tools/RefreshUnityEditor.sh` 刷新，必须输出 `REFRESH_STABLE=1`。
2. 默认 Editor 编译、`INFINITY_UNITY_CLI` 编译、release AssemblyBuilder 编译，三者都要 0 error。
3. `static_check.py` 结果为 0 error，`test_static_check.py` 全部通过。
4. 全量 EditMode，预期总数 844：832 通过 / 11 失败 / 1 跳过。11 个失败是已知集合：ShaderStrippingTests 10 个，加 `VisualContractGpuTests.HierarchicalRay_HitIsOneActualSurfaceTexel(True)`。

## 画面（1445×813，对比 `1a357c0`）

- Spazon：固定机位、移动机位、TAA 第 30 帧，应与 `1a357c0` 一致。
- BoxMatix：先确认场景里有没有 directional light。
  - 有：画面应与 `1a357c0` 一致。
  - 没有：天空与环境光变黑是预期结果，其余应一致。
- cookie 与反射探针：用有 light cookie 和反射探针的样本，在相机 frame settings 里分别开、关 LightCookies 和 ReflectionProbes。开的时候与 `1a357c0` 一致，关的时候 cookie 与探针反射消失，Console 无 "property not set"。
- SSGI 加 AO：关闭 AmbientOcclusion 时 SSGI 照常工作，开启时与 `1a357c0` 一致。
- DebugView：AO、SSGI、IndirectDiffuse、CascadeShadowAtlas、LightComplexity、ReflectionProbeAtlas 各截一张，与 `1a357c0` 一致。

## 构建（需要用户授权）

- 做一次 macOS release player build，Development Build 关闭。
- shader 编译日志无错误，`INFINITY_*` 与 `SSGI_AMBIENT_OCCLUSION` 的变体都被编译。
- `Data/Managed`（Mono）中没有 `Unity.RenderPipelines.Infinity.Validation.dll`。Runtime DLL 中搜不到 `RenderCaptureService`、`ComputeDebugView`、`RenderFaultValidation` 三个名字。
- player 能启动并出画（Spazon 场景，截一张图）。

## 性能

- GC：Spazon、Play、GPU Indirect，暖机 300 帧后录三个窗口，`frameAllocations` 全为 0。
- A/B：A = `1a357c0`，B = 目标提交，按 A B A B 跑，每轮 300 帧，记录 marker 忙碌时间总和。两对 B 都慢 5% 以上，且差值落在本批改过的 pass 上，才判 RETURN-PERF。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/final/`。验收后删除，写明回收的空间；player build 产物也删除。
