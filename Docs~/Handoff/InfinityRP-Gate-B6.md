# 闸门 B6：本地验收清单（B6 + B5 遗留）

分支 `claude/kind-knuth-d599st`，HEAD 为拉取时的最新提交（至少 `6c2bfb3`），上一个闸门 `082a41d`（B5，PASS，附遗留项）。通用流程、判定标准与回报模板见
`InfinityRP-Local-Verification-Handoff.md`；本文只列这一闸门特有的内容。云端无法编译 shader，本批所有 HLSL 改动在此之前都是未验证的。

## 提交

| 提交 | 内容 |
|---|---|
| 6983ee4 | 删除 GC 字节读数（它是上一整帧、整个主线程的字节数）；probe 证据只保留 scope 内的分配次数 |
| ab6f401 | B6a：PrepareCamera → CullCamera → RecordCamera；约 70 个 pass 改收 `in CameraRecordContext`；删除跨相机可变字段与遮蔽同名字段的参数；阴影投射体剔除合并为 CullShadowCasterSplits |
| 6bf7446 | B6b：ETemporalResolve 与 EHistoryReset 各一个写入点（CameraFrameState.ResolveHistoryReset） |
| afdcb52 | B6c：VolumeSnapshot；大气参数每相机解析、校验一次；修复 6bf7446 的测试夹具元数 |
| f500442 | B6d：资产字段改 `[SerializeField] private`（名字不变）；pipeline 构造时取一次 Settings 快照；运行期不再写资产 |
| 07e2d41 | B6e：IFrameTransaction（7 个主人，各自声明相机失败策略） |
| 8cdf1b5 | B6g：RequiredProducers 一份集合并在资源解析时校验；depthTextureMode 只写一次；SubmitFrame；CombineLut 删常量与死参数（含 shader）；redundantparam 检查 |
| 9d057bd | B6f-1：删除 12 个无人读取的相机全局量；cbuffer ViewUnifrom → ViewUniform（含 shader） |
| 3752cfe | B6 复审修正：HistoryCache 改 internal（修 CS0051）、反射测试、资产 setter、Inspector 抛错、R8 守卫、subsurfaceEnabled |
| 33ca038 | B8b：HistoryCache.Read，history 有效性一条规则（体积云/雾/曝光在上一帧失败后不再混合未初始化内容） |
| a24cd36 | B8c：TextureDescriptor.Create2D |
| 93a664a | B8d：附加光源数据每帧按可见索引从注册表解析一次，删除 3 处 TryGetComponent |
| 586c574 | B8g-1：TryQuery + throw 改为 QueryTexture |
| 12ce5c0 | B8g-2：可选输入由 feature 集合决定；体积雾删除"无大气 LUT"静默分支（含 shader） |
| 6c2bfb3 | B8f-1：capture 请求解析为 ECaptureBuffer 位标志 |

## 编译

1. 默认 Editor 编译（Runtime、Editor、Tests、Validation）。
2. 定义 `INFINITY_UNITY_CLI` 后再编译（`Editor/Cli`）。
3. release 脚本编译（替代 BuildScriptsOnly，不需要 IL2CPP，不写项目设置）：
   `PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings { target = EditorUserBuildSettings.activeBuildTarget,
   group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget), options = ScriptCompilationOptions.None }, <任务目录>/release-scripts)`。
   回报 `result.assemblies` 是否含 `Unity.RenderPipelines.Infinity.Runtime`、Validation 程序集是否被 defineConstraints 排除（预期排除），以及全部编译错误。
4. Shader：刷新后 Console 新窗口里全部 shader 编译错误与警告（重点 `Compute_CombineLUTs.compute`、`TonemapCommon.hlsl`、所有 include
   `ShaderVariables.hlsl` 的 surface shader 与 Shader Graph 材质）。

允许本地机械修复：漏 `using`、批次内改名漏改、`.meta` 缺失、意图唯一的签名不匹配。每处写进报告并附 diff。编译失败时回报该程序集的全部错误。

## 重点怀疑

- `readonly struct` + `in` 参数：`CameraSetup`/`CameraScene`/`CameraRecordContext`、`InfinityRenderPipelineAsset.Settings`（嵌套在 partial 资产类里）。
- `NativeList<ShadowCasterSplitRecord>` 与 `records.Sort(new ShadowCasterSplitOrder())`：需要 `ShadowSplitData` 满足 unmanaged 约束。
- `SerializedProperty.boxedValue`（Inspector "Copy Preset To Custom"）。
- `IFrameTransaction` 为 internal 接口，实现者的成员为 public。
- 资产：字段改 private 后，任何 Editor 工具或 Example 项目脚本若直接写资产字段会编译失败（包内已全部改走 SerializedProperty）。

## 序列化（本闸门没有 schema 变更）

B6d 只改访问修饰符，字段名、类型、默认值均未变。验证方式：对 Example 项目的 RenderPipelineAsset 先做持久备份，在 Inspector 中不改任何值、保存一次，
字节必须与备份相同。不同即 RETURN，附 diff。

## 预期变化（允许，需逐项确认）

- 切换 SDR/HDR 输出不再触发 CombineLut 重算（key 不含输出设置）；调色与胶片曲线开关画面不变。
- Editor 中每次 pipeline 重建（改 RP Asset、域重载）都会刷新一次静态姿态。
- RP Asset 的阴影/SR/render scale/probe 修改通过 pipeline 重建生效；运行期脚本写资产不再生效（setter 已删除）。
- 资源缺少 Depth/GBuffer/DeferredShading/Display 所需 kernel 时，pipeline 在构造时报错一次，而不是每帧报错。
- 上传的相机全局量从 25 个降到 14 个；Frame Debugger 中不再出现 FrameIndex、TAAJitter 等。
- 相机失败后的下一帧，体积云、体积雾、自动曝光把 history 视为无效（原来会混合池中未初始化的内容）。
- 运行中给灯新增 InfinityAdditionalLightData 时，从下一帧（注册任务处理后）起生效；不再有同帧 TryGetComponent 兜底。

## 运行期新增的异常（出现即定位第一个 owner 回报）

| 消息片段 | 含义 |
|---|---|
| `The camera Volume stack has no X` | 默认 Volume profile 缺少被消费的组件 |
| `InfinityRP cannot render without X` | 资源解析时必需 producer 不受支持 |
| `required producer X is not going to produce this frame` | 某相机录制后缺少必需产出（含 TAA/超分） |
| `IndexOutOfRange` 出自 CullShadowCasterSplits / MeshGpuCullPass | ShadowAllocator 发布了越界的可见光索引（原来被静默跳过） |
| `RGScoper.QueryTexture: X is not registered this frame` | 某 feature 被判定录制但其 producer 没有登记输出（原来被 TryQuery 静默跳过） |

## 必跑测试

全量 EditMode，与 B5 基线（833 通过 / 14 失败）对比。重点：CameraFrameStateTests（history reset 原因、SkippedFrame）、TemporalAntiAliasingTests、
LightBinningTests、RenderCaptureTests、AtmosphereCacheTests、HistoryCacheCommitTests、LocalShadowCacheTests、LightCookie*/ReflectionProbe* 测试、
QualityLifecycleTests、NormalizationContractTests、SceneViewOutputTests、BakedGITests、ColorOutputGpuTests、DefaultVolumeOutputTests、ExposureOutputTests、
HistoryCacheCommitTests、RenderCaptureTests、ImageRegressionRequestTests、ShadowAllocator/LightBinning 相关测试。
静态检查：`static_check.py` 0 error；`test_static_check.py` 全部通过（含新的 redundantparam 夹具）。

## 画面

与 B5 结果比对：Spazon、BoxMatix 的 Play mode 固定机位、移动机位、Scene view、TAA 第 30 帧；另加 Preview 相机、带 targetTexture 的相机、超分开/关、
窗口缩放、相机瞬移（应无拖影）、cascade 与 local 阴影、Volume 覆盖切换（雾、云、SSR、SSGI、GTAO、RTAO、接触阴影、后处理）实时生效。
Spazon 先做一次"基线对基线"的同条件两次采样，给出噪声底（SSIM 与 diff 比例），再判断 B6 的差异是否超出噪声底。

## B5 遗留（并入本闸门）

1. RenderPipelineAsset：用 Example 项目的版本控制对比该 .asset 与 VCS HEAD，贴出完整 diff（B5 时被写回磁盘，少了 68 字节，`enableDynamicBatch` 一行只占 24 字节）。
   不要还原，等判断。
2. GC 定位：Spazon，Play mode，只开 Game view，暖机 300 帧后用 Profiler（CPU 模块开启 GC.Alloc 调用栈）录约 300 帧；取典型帧，在 FrameRendering 下
   按 GC Alloc 排序列出前 15 个 marker（名称、次数、字节），对前 5 个各给一条调用栈（展开到第一个 InfinityTech.* 帧）。再用 runtime probe 跑一个窗口，
   回报 frameAllocations 的 p50 / mean。只回报表格与调用栈文本。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/<run-id>/`；release 脚本编译产物同样放在任务目录，验收后删除并写明回收空间。
