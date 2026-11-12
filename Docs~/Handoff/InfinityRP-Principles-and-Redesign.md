# InfinityRP 原理、规范与全仓重构设计

本文件是 plan mode 的输入。第 1–5 节是理念与规范（将改写进 AGENTS.md），第 6 节是文档重构方案，第 7–8 节是全仓诊断与目标设计，第 9 节是执行批次，第 10 节是待决问题。证据来自 5 份只读审计（RenderGraph、Mesh、Pipeline 顶层、Pass/Lighting/Atmosphere、Core/GPUResource/其他），关键结论已抽查复核。

---

## 1. 分类体系：你的四类基本对，但缺一类，且要按"如何被验证"来分

| 类别 | 回答的问题 | 来源 | 怎么验证 | 归属文档 |
|---|---|---|---|---|
| **原理**（你的"工程/技术理念"） | 什么是对的、什么是贵的 | 第一性推导，与语言/引擎无关 | 推导 | AGENTS §1 |
| **设计规则**（你的"抽象思想"） | API 与数据该长什么样 | 原理的推论 | Review：能指出它来自哪条原理 | AGENTS §2 |
| **代码规范** | 名字、文件、版式怎么写 | **约定**，价值来自统一而非正确 | 静态检查 | AGENTS §3 + Style/ |
| **工作方法**（你的"实践思想"） | 怎么改、怎么验证、何时停 | 工程纪律 | 交付物 | AGENTS §4 |
| **系统契约**（缺的一类） | 本仓库具体系统必须保持的不变量 | 本仓库的设计决定 | 测试 + Editor 证据 | ARCHITECTURE |

两条关键区分：
- **规范不要用原理去论证**。命名、`#region`、`m_` 前缀是约定，强行包装成哲学会让真正的原理被稀释。
- **系统契约不属于 AGENTS**。现在 AGENTS 的 §4–§13 几乎全是系统契约（时间抗锯齿常数、池水位、LOD 公式），这就是它 48KB、和 ARCHITECTURE（52KB）同样长、且 14/14 个抽查事实两边各写一遍的根因。

---

## 2. 原理

### 模型
程序由**事实**（必须知道的量或顺序）、**表示**（事实在代码里的形态：字段、缓冲、key、分支、keyword、字符串、副本）、**读者/写者**、**时钟**（CPU 帧、GPU queue、提交 epoch、事务代际）组成。
- 正确 = 每个读者在读的时刻拿到事实的正确版本。
- 成本 = Σ 表示 × 计算频率 × 单价 + 维护（表示数 × 交互组合）。

### 正确性公理（不分冷热路径）
- **P1 单一权威**：每个事实只有一个写者；其余表示是派生的，声明来源与版本，由来源重建或失效，从不被独立写入。静默兜底（默认值、dummy 资源、`return false`、dispatch 0）是非主人伪造事实。
- **P2 引用带时间**：引用携带版本/代际，过期即拒绝。
- **P3 原子发布**：先完整准备，再一次发布；读者只见旧或新；发布前失败完整回滚。
- **P4 寿命 = 最后读者**：表示活到最后一个读者在其自己的时钟上读完；之后复用；读完还要留就是新的所有权。防御性拷贝 = 说不出最后读者。

### 结构公理
- **P5 依赖方向**：依赖只向下。下层定义通用槽，上层把自己的概念适配进去；下层永远不点名上层概念。
- **P6 语义与优化分离**：只改变成本、不改变结果的东西（occlusion、批处理、缓存、后端选择）不进入调用方的语义接口，由机制持有。

### 经济公理（按频率加权，冷路径可放松）
- **P7 频率匹配**：计算频率 = min(变化频率, 读取频率)。读取频率为 0 的表示，存在本身就是全部成本：删除。

**读者的定义**：读者是现在存在的调用方，**或**一个有名字主人的开发中消费者（例如 Terrain 绘制）。"没有调用方"不等于"没有读者"，按 R15 分类后才决定去留。

### 认知公理
- **P8 证据强度**：断言的强度等于能证伪它的最便宜检查；低层级检查不为高层级断言结案（静态不证运行时，编译不证画面）。静默兜底同时摧毁证据。

### 冲突优先级
P1–P4 > P5–P6 > P7 > 局部可读性。P8 决定能声称什么。

---

## 3. 设计规则（原理的推论，每条附判据）

| # | 规则 | 来源 | 判据 |
|---|---|---|---|
| R1 | 差异是数据：库提供槽（key、context、描述符），调用方填策略 | P1 P5 | 删掉一个参数后只有一类调用方会坏 → 它是特化开关，不进公共 API |
| R2 | 参数和结果是两个事实 | P1 P2 | 一个类型同时装输入和结果状态 → 拆开 |
| R3 | handle 在消费者旁边创建，作用域即寿命 | P4 | handle 穿越 >1 层参数才到消费者 → 声明位置错 |
| R4 | 一个生命周期转换一个动词 | P1 | 同一对象有 Create/Request/Prepare 三个动词 → 合并 |
| R5 | 跨边界事实快照一次：引擎对象（Light/Camera/Transform）的属性按其快照（CullingResults、VisibleLight、TransformAccessArray、相机 setup）读一次，下游读纯数据 | P1 P7 | 热循环里出现引擎对象 getter；实时 getter 与快照混读 |
| R6 | 容器跟读者走：Burst/GPU 读 → Native；托管 API 读 → 复用托管数组 | P4 P7 | 每帧在 Native 与托管之间转换 |
| R7 | 属性语法的代价必须等于字段；查找/计算是带动词的方法 | P8 | 属性内含 GetComponent/字典/计算 |
| R8 | 失败由不变量的主人在建立时报一次；下游不复查、不替换 | P1 P8 | 同一条件被检查两次；`?.`/`TryGet` 静默放过 |
| R9 | 新路径接管全部读者后删除旧路径，不留兼容层 | P1 P7 | 两条路径同时可达 |
| R10 | 特例成堆说明表示错了：先改表示再考虑补丁 | P1 P7 | 新增第 3 个同类 flag/分支 |
| R11 | 平台抽象正确时与之同构（RendererList/BRG 形状）；偏离必须给出命名理由 | P8 | 自造一套与平台平行的概念 |
| R12 | 诊断是观察者：构造时装配，在成功路径与测量范围之外，按失败/请求频率生成 | P5 P7 | 核心循环里出现 Validation/Capture 调用；查询函数写 debug 字段 |
| R13 | 按种类复制的代码（N 个 handle、N 个 encoder）收成一个泛型或一个核心 | P1 | 两份以上方法体逐行相同 |
| R14 | setup 期可知的事实（kernel index、property ID、keyword、sampler、pass index）在资源变化时解析一次成表 | P7 | record/execute 里出现字符串查找 |
| R15 | 无调用方的代码先分类再处置（见下表），不机械删除 | P1 P7 | — |
| R16 | 诊断与验证只存在于 Editor 与 Development 构建；release 构建里连分支都没有 | P5 P7 R12 | release 构建可达任何 Validation/Capture/调试符号 |

**R15 分类表**

| 类别 | 判据 | 处置 | 本仓库实例 |
|---|---|---|---|
| A 开发中特性 | 有名字主人与目标消费者 | 保留；不得把成本放进其他特性的热路径；不得静默兜底 | Terrain 全套（TerrainPassProcessor、Collector、DrawCommand、Batch、ParallelJob、HeightmapLoader、TerrainUtility、帧内 LOD） |
| B 已有依赖的重复实现 | 已声明依赖或仓库内已有权威提供同一能力 | 删除（P1） | NativeContainer 的 HashSet/IntPtr/LongPtr/PerJobThread*（Unity.Collections 的 `NativeHashSet`/`NativeReference`/`UnsafeAtomicCounter32`）、`MinMaxBox`（= `BoundingBox`）、`GraphicsUtility.DrawFullScreen` 系列 |
| C 被替换设计的残留 | 新机制已承担同一职责 | 删除（R9） | `ObjectHandle<T>`（无代际，已由 `SlotTable` 取代）、`TArray`/`TNativeArray`/`TSparseArray`（借用他引擎前缀）、`Disposer`、`ListUtility`、`FunctionLibraty`、`HistoryCache.Invalidate`、`CullFirstNoHistory`、GPUResource 的 public `Pull/Push`、6 个无人引用的 `CustomSamplerId` |
| D 有意义但未接线 | 对用户可见（Inspector/API）却不生效 | **接线**，这是缺陷不是死代码 | `enableDynamicBatch`/`enableInstanceBatch`：SRP Batcher 关闭时应进入 renderer list 的 `DrawingSettings` |
| E 活 API 内部的未用参数/只写字段 | 所在 API 是活的 | 删除（活代码没有"开发中"豁免） | 32 个未用参数、`descriptorGeneration`、`colorFormat` 等 |
| F 只有测试读取的运行时影子实现 | 测试覆盖的函数不在运行时路径上 | 测试改测运行时真实路径，删影子（P8） | `ExposureUtility.ResolveManualMultiplier`/`MidGray`、`successfulRenderCount`、`exposureState` |
| G 诊断与验证 | 只服务开发期 | 保留，按 R16 只编进 Editor/Development | Runtime/Validation、Capture、调试视图 |
| H 供应商代码中无主的通用容器 | 第三方原样拷贝、无调用方、无计划消费者 | 删除；需要时从上游重新引入 | Jackson Dunstan NativeCollections 的 `NativeLinkedList`/`NativeChunkedList`/`SharedDisposable`。若 Terrain 计划使用则归 A（见 Q9） |
| — 待确认 | 看不出归属 | 问主人 | `HeapAllocator`/`HeapBlock`（固定块分配器，可能是 Terrain/虚拟纹理的预备件，见 Q9）、`RayTraceEnvironment` 组件（可能被场景引用，工作区无 Example 无法核实） |

---

## 4. 代码规范（约定，靠静态检查）

保留现有命名表与 `Docs~/Project/Style/`，新增/收紧：
- **变量保留类型中心词**：去领域前缀，不截中心词，不缩写。`MeshCullingContext cullingContext`、`TextureDescriptor gbufferADescriptor`（现有 `Dsc` 247 处）。
- 时间维度统一用 `previous`/`current`（现有 Last/Prev/Curr 混用）。
- 所有 shader property ID、keyword、sampler 是 `static readonly`，按 HLSL include 分组（现有 313 处可变 static）。
- 拼写黑名单进静态检查：`Culling`（`CulllingScene`）、`Uniform`（`ViewUnifrom`）、`Bias`（`_ScaleBais`）、`Sharpness`、`Calculate`、`Origin`、`Initialize`、`Terrain`、`Library`。序列化字段的拼写只在 §11 迁移内改。
- 一个文件一个主类型（`CameraUniform` 在 `InfinityRenderPipeline.cs` 内）。

---

## 5. 工作方法（保留现 AGENTS §1 的骨架，收紧）

- 从行为与证据出发；先找共同原因；按证据定规模；每个新增元素有当前读者与主人。
- 交付完整范围；停下来问的条件不变（数据丢失、API/ABI/序列化、不可逆、权限边界、无安全默认）。
- 验证：检查必须能为有意义的错误失败；静态不为运行时结案；无 Editor 时结果保持 `TODO(UNVERIFIED)`。
- 活文档：改动后删过时、改变化、加新事实，不追加日志。**每个事实只在一个文档里出现一次**。
- intermediate 数据规则保持（压缩成一段）。

---

## 6. 文档重构

### 6.1 问题
- AGENTS 48KB ≈ ARCHITECTURE 52KB，违反"AGENTS 最短"。抽查 14 个事实两边各一份，多处在 FeatureStatus 有第三份。
- AGENTS §6 共 31 条、§13 缺陷列表，是规则层的"逐例打补丁"。
- ARCHITECTURE 已有与代码不符的陈述（§7 Frame 第 4 步说 pass 建 list，实际是 `Render()` 预建）。
- `Docs~/Project/Audit/UnityBoundaryInventory.csv` 1963 行中 1342 行指向不存在的路径。

### 6.2 目标结构
**AGENTS.md（目标 ≤ 15KB，规范性，基本与本仓库实现细节无关）**
1. 模型与原理（P1–P8 + 优先级）
2. 设计规则（R1–R14，每条一行 + 判据）
3. 代码规范（命名表、中心词规则、拼写、指向 Style/）
4. 工作方法（请求→改动、交付、停止条件、验证层级、活文档、intermediate）
5. 仓库安全（Editor CLI、序列化迁移、Player 构建授权）
6. 指针：系统契约在 ARCHITECTURE（规范性）、限制在 ARCHITECTURE「Limits」、验证状态在 FeatureStatus

**ARCHITECTURE.md（实现 + 系统契约，每个事实只写一次）**
每个子系统一节，固定小节：**Owner / Data / Lifecycle / Contracts / Limits**。现 AGENTS §4–§12 的系统事实迁入对应 Contracts 并去重；AGENTS §13 删除（与 ARCH §16、FeatureStatus 重复）。

### 6.3 删除
AGENTS §13；AGENTS 中所有在 ARCH 已有的系统数值；`UnityBoundaryInventory.csv` 与 `boundary_inventory.py`（Q8 已定）。

---

## 7. 全仓诊断：10 个共同病因（不是症状列表）

| # | 病因 | 典型症状与证据 | 违反 |
|---|---|---|---|
| C1 | **没有 setup 期的"已解析资源"层** | kernel 解析 6 种写法；`FindKernel` 35 处在 pass 内；`HasKernel` 复查 16 处；FeatureRegistry 每相机每帧跑 2 次 × 27 feature 的 `HasKernel`（`InfinityRenderPipeline.cs:1309,1343`）；`Shader.PropertyToID` 在 record/execute ~107 处；`CachedSampler.Get(string)` 68+10 处；`new LocalKeyword` 在 execute 8 处；positional kernel 常量依赖 `#pragma` 顺序 | P1 P7 R14 |
| C2 | **没有每相机只读的录制上下文** | `m_ActiveFrameState` 86 处 / `m_CameraUniform` 50 处；42 个 `renderContext` 参数遮蔽同名字段；temporal 模式 6 处推导；history reset 3 个主人；`AtmosphereParameter.FromProfile` 每相机 7 次；VolumeStack 按 Type 查 26 次；相机矩阵拷进 pass data 44 处；history 有效性 8 份 4 种编码；`camera.transform` 实时读 18+30 处 | P1 P7 R5 |
| C3 | **语义输出靠猜而非登记** | Post 与 DebugView 依次 `TryQuery` AA→SR→Fogged（`PostProcessingPass.cs:150-152`）；`TryQuery`+throw 59 处 | P1（AGENTS §6.5） |
| C4 | **第二套物理分配器 / 生命周期权威** | `HistoryCache` 自己 `new ComputeBuffer`/`RTHandles.Alloc` 并自带退休队列；`MeshGPUVisibilityCache` 把帧内数据做成常驻缓冲且按 mask 组合泄漏增长；RG alias slot 计划池子并不执行；池 handle 无 generation（ABA）；无 graphics fence 设备上池永不复用、HistoryCache 无证明即销毁（`InfinityRenderPipeline.cs:762,789`，`HistoryCache.cs:321-325`） | P1 P2 P4 |
| C5 | **分层倒置** | RG → Pipeline（`RenderContext`、`RenderFaultValidation.AtPass` 每 pass 两次、`ClearDepthFar`）；Mesh → Camera/Capture/Debug/`InfinityShaderIDs`（8–10 文件）；Core → Volume/RG；Material → MeshComponent；Camera 层调用 Pass 层 `AntiAliasingPass.ProjectionJitterUV` | P5 |
| C6 | **静默兜底** | RG ~10 处（view 写 0、renderer list 失效不画、`QueueAttempt(null)` 返回）；LightLoop 绑 `whiteTexture`/空 probe atlas；SSGI 把 depth 当 AO 绑；无太阳时伪造头顶白光；Fog/Cloud 在 `ShouldRecord` 后再静默 return | P1 P8 |
| C7 | **死代码与只写状态** | Core 10,909 行 0 调用者（NativeContainer 等，已复核）；Terrain ~390 行 + 每帧 LOD 只喂 gizmo；Geometry/GraphicsUtility ~230 行；GPUResource 死 API ~40 行；32 个未使用参数；无 history occlusion 分支生产不可达；CombineLut 7 个死参数 | P7 |
| C8 | **按种类复制** | 4 个 pass handle + 4 个 pass 子类；3 个 encoder + `CommandBufferCommands` ~1100 行转发；2 个 gather job；2 种组件基类；2 种任务队列；Buffer 两种表示；2 种包围盒；shader 2 套 frustum/LOD 路径；"资源身份"4 种定义；visibility 身份 4 种 | P1 R13 |
| C9 | **诊断在成功路径上** | Validation 挂钩 37 处且在测量范围内；`UtilityPass.cs:247` 每帧拼字符串仅供 capture；`SelectBackend`（查询）每次写 debug 字段，导致 Debugger 的 Actual Backend 恒显示最后声明的 T2 = CPUDirect，且被 CLI 写进验证证据（已复核 `MeshWorld.Frame.cs:114-144`）；5 份重复的 request 轮询器 | R12 P8 |
| C10 | **参数逐标量上传** | `SetCompute*Param` 286 处；同一组参数 Settings→passData→Set 手写三遍；GTAO 的 HLSL 已有 cbuffer 而 C# 逐个设 | P7 P1 |

---

## 8. 子系统目标设计

### 8.1 RenderGraph
- **Handle**：有效 ⇔ `generation != 0`；删隐式 `int`、`m_IsValid`、平行 `m_Generations`、`GenerationOfTexture`；只留一个校验型 `Resolve(handle)`；`GetTextureDescriptor` 等全部走它。资源身份一种定义。
- **绑定**：encoder 收 `RGTextureHandle`，view 取 `descriptor.view`（O(1)）；删 `RGResourceFactory.current`、三个隐式转换、`TryGetTextureView`；图外绑定登记过 view 的属性即抛异常。
- **图内列表统一**：renderer list 与 mesh draw list 共用"声明 / 消费者 / 活性 / 提交"机制，失效 handle 一律抛异常。
- **Extension**：依赖边只在 record 产生；`Contribute` 只读裁剪结果；删 `BindVisibilityRead`/`BindGpuCulling`/`m_ExtensionContributed`。
- **编译**：删永真/永不触发的验证（`ValidateCycles`、`ValidateDuplicates`、execute 期重复检查）；pass-local owner 改 O(1) 记录；memoryless 在规划前决定；字节估算一份。
- **alias slot**：删除 slot 计划（Q7 已定）；池子是唯一物理权威，segment 拆分由池可见的 release 事件推导；AGENTS §2.4 中"Overlap is a descriptor-level slot plan executed by ResourcePool"改写为"Overlap is descriptor-level reuse by ResourcePool"。
- **API**：`Execute(ScriptableRenderContext)`；`RGPassHandle<TData>`（类型只写一次，`Dispose` 校验 execute 已设）；encoder 一个只收 int ID 的核心实现各接口，删 string/`params` 重载，`NextSubPass` 内部化；加 `SetKeyword(ComputeShader, in LocalKeyword, bool)`。
- **Scoper**：归 builder 所有、随 `ClearPass` 清空；角色 key（SceneColor 等）由最后的 producer 登记，消费者只查一个。
- **Buffer**：统一 `GraphicsBuffer`，target 进 `BufferDescriptor`；`ImportBuffer(GraphicsBuffer)` 同样保留 recording 引用。
- **录制数组**：每图一个 arena，冻结记录共享引用；删 `SnapshotRecordingArray` 与 `(Type,int)` 字典池。删 `cachedHash`，key 由池从描述符计算。
- **保留**：handle+generation 的 renderer list 模板、命令接口泛型约束无装箱、失败图分配延后退休、已接受 pass 先入队再重抛、`RGResourceKey` 失败时才格式化、编译 scratch 复用。

### 8.2 Mesh Pipeline
三层：机制（`Runtime/Mesh`）/ 图接入（`Runtime/Mesh/Graph`）/ 概念适配（`Runtime/Pipeline`）。
- **机制层槽**：`MeshCullingContext { planes(inline), LODParameters lod, layerMask, sceneCullingMask, renderingLayers }`，GPU 侧同名 GPU-RECORD；不含任何 RG 类型、相机、阴影概念。
- **唯一 desc**：`MeshDrawListDesc(in MeshCullingContext cullingContext, ShaderTagId passTag)`，与 `RendererListDesc` 同构；在 pass 内部、消费者旁边 `CreateMeshDrawList`，同一调用记录其 producer（Create = Prepare）。
- **可见性**：`(cullingContext, entity, occluder)` 的纯函数，融进每个 list 的 producer kernel（CPU 后端同理在 list 的 Burst job 内），共享 include 标 `precise`。证伪：Example 场景主视图各 list 的 culling+compaction GPU 时间之和高于现状 → extension 内部退回按 view 共享的惰性 producer，API 不变。
- **Occlusion = 内部优化**：mesh 层定义 occluder 槽，HiZ pass 注册当前金字塔与 VP；list 创建时与相同 view 的最新 occluder 测试（无则用 history，首帧/reset 不测）；不变量"每帧同一 (view, pass) 每个实例最多画一次"，第二次创建得到余量 = recovery。
- **后端**：每相机解析一次 camera backend（纯函数），registry 只叠加 `desc.cpuBackend`；debug 字段由 pipeline 写一次。
- **command meta** 进 cache 自己的 buffer，按 revision 在 `UpdateResidency` 上传；删 GPU list 的 upload pass。
- **执行期**全部经 encoder + RG handle；visibility 是图临时资源；删 `MeshGPUVisibilityCache` 整文件、`ReleaseView`。
- **list 状态**一份 record（无旁路字典），prepared 由构造保证。
- **motion binding** 由 frame state 在每图开始交给 extension 一次。
- **身份**：`MeshPassDesc` 持 `ShaderTagId`、token、sampler，cache 按表下标；删 `MeshDrawProfiling` 的 pass switch、字符串 sampler 查找。
- **删除清单**（审计已列，约 1000 行 Runtime + 2400 行测试改写）：`MeshView.cs`、`MeshVisibilityKey/Share/Handle`、`MeshGPUVisibilityCache.cs`、frustum batch/slice、`RequestMeshGPUCulling`、`PrepareMeshDrawList`、`FlushMeshFrustumCulling`、occlusion 特殊流程、`CullFirstNoHistory`、shader 的全局 uniform frustum 路径、`MeshGpuCullPass.cs`、`MeshOcclusionPass.cs` 大部分、7 个 pass 的 `view`/list 参数、`viewPosition`、两个 gather job → 一个泛型 job。
- **保留**：9 个 pass cache 按 commit 增量、candidate 按 revision 上传、`MeshWorldUpdate` 事务、`SlotTable` 代际、payload 空壳 + pool 归还、`MeshTransformVersion` 不可变版本、`SortKeyPacking` 饱和、LOD 双侧镜像。

### 8.3 Pipeline 每相机流程
```
Render(ctx, cameras)
  BeginFrame()                       // pool/async、proxy update、residency、RT 几何同步(每帧一次)、transactions.BeginFrame
  foreach camera:
    CameraSetup setup = PrepareCamera(camera)
        // 只决策：state(含 sampler)、policy、FrameSettings、dimensions、ETemporalResolve、
        // history reset 原因(唯一主人)、VolumeSnapshot、AtmosphereBinding、output decision、mesh backend
    try
      CameraScene scene = CullCamera(setup)     // camera props、APV、VFX、Unity cull、LightContext.Build、atlas resolve
      FeaturePlan plan  = features.Resolve(staticSupport, setup, scene)   // 位运算
      RecordCamera(new CameraRecordContext(setup, scene, plan))           // 顺序 pass 列表，handle 就近创建
      Execute(setup)
    catch: rollback + transactions.DiscardUnproduced(queueUncertain)
  SubmitFrame()                      // fence → submission → transactions Commit/Rollback → FlushRetired → recycle → observers(测量外)
```
- 删跨相机可变字段（`m_ActiveFrameState`、`m_CameraUniform`、`m_FeatureSupportContext`、`m_ShadowCasterSplits`）与 42 个冗余参数。
- `IFrameTransaction`（7 个实现）注册一次、遍历一处，失败策略显式。
- **Settings 快照**在构造时生成；资产字段改为同名 `[SerializeField] private`（序列化名不变，不重置数据）。
- **相机 cbuffer** 一次写全部矩阵、near/far/ortho/reversedZ/position/forward、current/previous jitter UV；shader 直接引用全局名，删 11 个 compute 里同名不同义的本地矩阵与 C# 端 44 处拷贝。
- 必需 producer 一份集合，前置条件在构造时校验；`camera.depthTextureMode` 不再每帧改写。
- **Renderer list 构造收成一个 helper**（现 7 处 `new RendererListDesc`）：改用 `RendererListParams(cullingResults, DrawingSettings, FilteringSettings)`，SRP Batcher 关闭时由 `enableInstanceBatch`/`enableDynamicBatch` 设置 `DrawingSettings.enableInstancing`/`enableDynamicBatching`；字段保留（R15-D）。
- CombineLut 常量与死参数删除；key 不含输出设置。

### 8.4 Pass / Lighting / Atmosphere
- **`ComputeShaderTable`**（每个 shader：kernel 名/index/thread group size/`LocalKeyword`），在 RuntimeShaders 容器赋值或变更时解析；`FeatureRegistry` 持有并只在资源变化时求 static support；pass 读 `table.X`。删所有 `RequiredKernels`、positional 常量、8 处复查、`HasRequiredKernels`。
- **GPU-RECORD cbuffer** 每 pass 一个，一次上传，替代 286 次标量 Set。
- **History**：`HistoryCache.Read(id, descriptor) → {handle, valid}` 统一 reset/created/generation；每个 feature 一份 history descriptor。
- `TextureDescriptor.Create2D(...)` 默认 Tex2D/Clamp；删 13 个局部 factory 与 50 行默认值赋值。
- **光源**：从 `VisibleLight`（`finalColor`、`localToWorldMatrix`、`range`）打包，cascade 旋转读快照；ext 每帧按 visible index 解析一次进数组，删 3 处 `TryGetComponent` 兜底。
- **LightLoop** 可选输入改 keyword gating（照 `ShadowInputs`）；SSGI 的 AO 同样 keyword 化；无太阳 → 太阳辐亮度为 0（真事实），不伪造方向光。
- `AtmosphereParameter` 每帧解析并校验一次；view cache 复用 observer。
- DebugView 改表 `EDebugView → (kernel, semantic)`；capture request 每帧解析为 `[Flags] ECaptureBuffer` + 统一 `CaptureMatches(camera)`；`RenderCapturePass` 的 `Resources.Load` 移入 RuntimeShaders 容器。
- 删 32 个未使用参数与列出的死状态。

### 8.5 Resources / History
- `ResourcePool` 是唯一物理所有者；handle 带获取代数，release 校验（防 ABA）；`handle` 字段改名或删除。
- graphics fence 支持在构造时验证，不满足即抛；删 `hasCompletion` 参数与 `ReleaseRetiredNow` 无 fence 分支。
- `HistoryCache` 保留事务 API（6 个所有者在用），slot 只存 pool handle，统一 `ReleaseTexture(handle, reuseFence)`；删 `PendingDestroy*`、`Invalidate`、读时重分配的不一致语义。
- 删死 API（`Pull/Push` public、`AfterSubmit(GraphicsFence)`、`textureAcquired`、`clearBuffer`、`enableMSAA` → 改查 `msaaSamples`）。

### 8.6 Core / Terrain / Scene / RayTracing / Material
- Core 按 R15 处置：B/C/H 类删除（NativeContainer 中与 Unity.Collections 重复者、无主供应商容器、`ObjectHandle`、`TArray` 系、`Disposer`、`ListUtility`、`FunctionLibraty`），`HeapAllocator`/`HeapBlock` 待 Q9；AGENTS 命名表示例 `ObjectHandle<T>` 改为 `RGTextureHandle`/`MeshEntityId` 等现存类型。
- `GraphicsUtility` 拆分：深度常量留 Core（中性 namespace）；`VolumeComponentActive` → Volume；blit 材质经 pass data；投影方法 → 相机 uniform 的主人；`ComputeUtility` → RenderGraph/Pipeline。
- 包围盒只留 `BoundingBox`（删 `MinMaxBox`、`PlaneEquation`）。
- 组件只留 `EntityComponent` 一种基类；注册统一类型化任务。
- RayTracing：几何每帧在相机循环前同步一次；`CanRecord` 为唯一就绪决策；原因用枚举；scratch 走 pool + reuse fence。
- Material：一张 pass 表 `{ShaderTagId, token, routeMask}`（`MeshPassCache.m_RouteMask` 为权威）；`ESurfaceRoute`/`ETranslucentStage` 枚举；路由变化通知改事件，去掉 Material → MeshComponent 依赖。
- Terrain：开发中特性（R15-A），保留全部代码与帧内 LOD；只做不改变其设计的修正：`BuildBounds` 的 min/max 命名颠倒、`Initializ`/`viewOringin`/`terrianPosition`/`terrainBatchs` 拼写；`HeightmapLoader` 的文件 IO 属于导入工具，接线时移入 Editor 程序集（记为 Terrain 的 TODO，本轮不动）。

### 8.7 Validation / 诊断
- 一个 `IPipelineFrameObserver` 列表，构造时装配，在测量范围外调用；RG 只通过 `IRGPassQueueObserver` 扩展点；capture lock 作为 reset 决策的输入而非直接改状态。
- 5 份 request 轮询收成一个；capture 支持集合由 producer 注册推导；`targetIdentity` 在 capture 内构造。
- **构建范围（R16）**：
  - `Runtime/Validation` 拆为独立程序集 `Unity.RenderPipelines.Infinity.Validation`，asmdef `defineConstraints: ["UNITY_EDITOR || DEVELOPMENT_BUILD"]`。它引用 Runtime，并在加载时把自己注册为观察者；Runtime 只认识观察者接口。依赖方向正确，release 构建里不存在这个程序集。`Editor/Cli` 与相关测试改为引用它。
  - 调试视图是 `InfinityRenderPipeline` 的 partial，无法移出程序集：其录制、`InfinityDebugDisplaySettings` 注册与面板用 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包住。release 构建中 Player shader stripper 剥离调试 kernel 与调试变体。
  - 计数器类调试数据（mesh 后端、light 计数等）由 pipeline 在每相机写一次，同样只在上述构建存在。

### 8.8 StaticCheck：从枚举特例换成原理级门禁
新增：按目录的分层检查（Core/GPUResource/RenderGraph/Mesh 不得引用上层命名空间）；0 调用者类型检测；Validation 符号隔离；record/execute 中禁止 `FindKernel`/`PropertyToID`/`CachedSampler.Get(string)`/`new LocalKeyword`/`m_CommandBuffer`（表文件除外）；拼写黑名单；可变 static ID。
被原理级检查覆盖后删除的枚举检查：`meshsubmit`、`meshpasses`、`graphdependency`、`unityboundary` 的墓碑项及其负例夹具。

---

## 9. 执行批次（依赖顺序）

| 批次 | 内容 | 依赖 | 静态门禁 |
|---|---|---|---|
| B0 | AGENTS/ARCHITECTURE 按第 6 节重构（原理先行，系统契约迁移去重） | — | 链接检查、AGENTS 体积 |
| B1 | 删除死代码（Core、Terrain 死路径、Geometry、GraphicsUtility、GPUResource 死 API、死参数/字段） | B0 | 0 调用者检查 |
| B2 | 分层修复（RG 只收 SRC、Validation 观察者、Core 拆分、Mesh 自有 ID、Material 事件） | B1 | 分层检查、Validation 隔离 |
| B3 | setup 期解析层（ComputeShaderTable、static readonly ID/sampler/keyword、FeatureRegistry 按资源变化） | B2 | 禁止热路径字符串查找 |
| B4 | Resources/History（pool 代际、fence 前置条件、HistoryCache 基于 pool、Buffer 统一） | B2 | — |
| B5 | RG 核心（handle、绑定、列表统一、extension、编译清理、pass handle 泛型、encoder 核心、scoper 角色 key、alias 决定） | B3 B4 | — |
| B6 | Pipeline 流程（CameraSetup/RecordContext、VolumeSnapshot、ETemporalResolve、reset 主人、IFrameTransaction、Settings 快照、相机 cbuffer） | B5 | — |
| B7 | Mesh 重构（8.2 全部） | B5 B6 | 机制层上层名词检查 |
| B8 | Pass/Lighting/Atmosphere（cbuffer、history Read、descriptor、光源快照、LightLoop gating、DebugView/capture） | B3 B6 | — |
| B9 | 文档事实同步、FeatureStatus 更新、清理 intermediate | 全部 | — |

每批：实现者自测 + 静态检查 + 独立 review；运行时与画面结果按 AGENTS 保持 `TODO(UNVERIFIED)` 直到 Editor 验证（见 Q1）。

---

## 10. 决定记录（用户已定）

| # | 问题 | 决定 |
|---|---|---|
| Q1 | 验证环境 | 云端实现 + 静态检查；本地 agent 在 Editor 中编译、测试、截图并回报。交接要求见 `InfinityRP-Local-Verification-Handoff.md` |
| Q2 | 包外 API 依赖 | 没有，公共 API 直接改 |
| Q3 | `enableDynamicBatch`/`enableInstanceBatch` | 有用：SRP Batcher 关闭时生效。保留并接线（R15-D，见 8.3） |
| Q4 | Terrain 与"无调用方"代码 | Terrain 是开发中特性，保留。所有无调用方代码按 R15 分类处置 |
| Q5 | 诊断与验证 | 只存在于 Editor/Development 构建，release 不含（R16，见 8.7） |
| Q6 | 算法细节文档 | ARCHITECTURE 只写别人依赖的契约（分辨率、输入输出、history、被其他代码依赖的常数）；逐 kernel 的算法步骤写进对应 shader 文件头注释，和实现放在一起 |
| Q7 | RG alias slot | 删除。Unity 没有 heap，只能做到描述符级复用；池子是唯一物理权威，segment 拆分由池可见的 release 事件推导 |
| Q8 | boundary inventory CSV | 删除 CSV 与 `boundary_inventory.py`；AGENTS §10 中对它的引用一并删除 |

## 11. 仍需确认

9. **`HeapAllocator`/`HeapBlock`、`NativeLinkedList`/`NativeChunkedList`**：Terrain（或其他开发中特性，例如虚拟纹理、GPU 页面分配）是否计划使用？计划使用则归 R15-A 保留；否则按 C/H 删除。
10. **`RayTraceEnvironment` 组件**：代码引用为 0，但可能挂在你的场景里（工作区里没有 Example 项目，无法核实）。场景用到就保留。
