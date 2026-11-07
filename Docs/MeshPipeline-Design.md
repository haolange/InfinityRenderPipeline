# Mesh Drawing Pipeline 设计文档（v2 提案）

状态：提案，未实现。基于对当前 `Runtime/RendererCore/PrimitivePipeline/MeshPipeline/` 的源码审查和三轮设计讨论整理。实现后，本文件的"目标"部分应并入 `ARCHITECTURE.md` §7，规范变更并入 `AGENTS.md`，本文件删除。

---

## 1. 目标与差距

| 目标 | 现状 | 本提案要解决的 |
|---|---|---|
| 接口像 SRP `DrawRenderers` 一样简洁 | 三步 API 成立，但 Pass 是硬编码 enum + `PassRegistry` 表 | 改为参数申请式 `MeshPassDesc`，Pass 身份由 interning 产生 |
| Unreal 级性能（静态 / 动态 / 缓存） | 静态场景极好（10k 下 Proxy 0.10 ms）；动态很差（10k 全动 21.5 ms，50k 105 ms） | 动态 transform 路径整体 Burst 化；MeshDrawCommand 增量缓存；每帧只对可见集排序 |
| 面向数据：SoA、拆依赖、易并行 | 表结构是 DOD 的，执行是串行的（`.Complete()` / `.Run()` / managed 重建） | 所有每帧路径变成 JobHandle 链；热路径零 managed 集合 |
| 避免 UE 的 OOP 包袱 | 已无 Proxy / PassProcessor 虚继承 | 保持；Relevance 以 `EMeshPassMask` 数据形式存在 |
| 清晰的分类 / 分层 / 命名 | 一个概念 5～6 个名字；RG ↔ Mesh 双向依赖；死代码累积 | 按作用域分层，统一命名与索引空间约定 |
| CPU / GPU 双后端 | 两条路径都通，但无共同抽象，输出语义不一致 | 统一输出契约 `MeshInstancedDraw[]` + `instanceToEntity[]` |

性能数字来源：`Docs/History/MainThread-20261025/Report.md` 第 51–62 行。

---

## 2. 四条硬边界

这四条是整个设计的不变量。任何实现细节都可以变，这四条不能变。

1. **PassDesc 决定"用什么状态画"，View 决定"画谁"。** 凡随视图变化的量（layer mask、culling mask、LOD 距离、shadow slice）不进 PassDesc，只进 View 或 Visibility。这是 MeshDrawCommand 可缓存的充分条件。
2. **MeshDrawCommand 是 `f(Element, PassDesc)` 的纯函数，且不含任何 per-entity 数据。** 实例数据全部通过 `entityIndex` 间接寻址。因此不同 Entity 天然共享同一个 Command，缓存规模是"不同渲染状态的数量"。
3. **只有一条生成路径。** "动态"不是第二条代码路径，而是同一个函数的三个参数：transform 每帧写、element 是 transient 的、不做 merge。
4. **filter、grouping、sort 是三个操作。** Merge 判断 `commandIndex` 相等，不判断 sort key 的位段相等。Sort key 饱和只影响排序质量，不会合错。

---

## 3. 分层模型

按**作用域和生命周期**分层。看名字就能知道一个类型属于哪一层、什么时候写、什么时候失效。

```text
┌─ World（每场景一份；对象存活期间；结构事件 / transform 写入）──────────────────┐
│  MeshEntity[e]      transform, bounds, flags, layer, customData    ← 脏页 → GPU │
│  MeshElement[x]     entity, section, material, passMask, lod       ← 脏页 → GPU │
│  MeshSection / MeshMaterial / MeshShaderState   引用计数的共享资源（dense rank）  │
└────────────────────────────────────────────────────────────────────────────────┘
            │  f(Element, PassDesc) → Command      纯函数，增量
            ▼
┌─ Pass（World × PassDesc；PassDesc 被申请期间；结构事件时增量写）──────────────────┐
│  MeshPassCache[passHandle]                                                      │
│    elementToCommand[x] : int   （-1 = 不参与）                   ← 脏页 → GPU   │
│    MeshDrawCommand[c]  : (shaderState, material, section) + args  ← 脏页 → GPU   │
│    MeshDrawCommandBinding[c] : Mesh / Material / passIndex（managed，同 index）    │
│    refCount[c] = 成员数 = 该 command 的 instance 容量上限                          │
└────────────────────────────────────────────────────────────────────────────────┘
            │  每帧、每视图，Burst，O(visible)
            ▼
┌─ View（每视图；一帧）──────────────────────────────────────────────────────────┐
│  MeshView           frustum planes, viewKey, kind, masks, 视点                   │
│  MeshViewVisibility visible[entityIndex]，lod[entityIndex]，depth[entityIndex]   │
└────────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─ DrawList（View × Pass；一帧，submit 后回收）──────────────────────────────────┐
│  Gather : visible ∩ elementToCommand ≠ -1        → VisibleMeshDrawCommand[]     │
│  Sort   : 按 commandIndex 聚拢，再按 EMeshSortMode → 两级排序                      │
│  Merge  : 相邻同 commandIndex                     → MeshInstancedDraw[]          │
│  Emit   : instanceToEntity[]（draw 内按 depth）    → Submit / IndirectArgs        │
└────────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─ Backend（CPU direct / GPU indirect：同一契约的两种实现）───────────────────────┐
└────────────────────────────────────────────────────────────────────────────────┘
┌─ Graph（RGDrawListHandle 记录 / 编译 / 执行；RenderGraph 不依赖 Mesh 类型）─────┘
```

和现状的对应关系：

| 现在 | 新层 | 变化 |
|---|---|---|
| `MeshScene` + `GpuScene` + `MeshGpuSceneSnapshot` + `MeshWorld` | World | 四合一。`MeshWorld` 是唯一根，`RenderContext` 不再单独创建 scene |
| `PassBinStore`（全量重建 + 排序 span） | Pass | **核心重写**：增量 map + refCount，不再存排序好的成员列表 |
| `MeshCandidateTableStore` | Pass | 删除，并入 `elementToCommand` 的脏页上传 |
| `MeshView` + `MeshVisibilityShare` + `MeshGpuVisibilityCache` | View | 合并到一处；CPU/GPU visibility 使用同一个 key |
| `MeshDrawExtract` / `PassView` / `MeshDrawGpuPayload` / `RGDrawListRecord` | DrawList | 收敛为 `MeshDrawList`，四步流水线固定 |
| `MeshDrawPipeline` + static `MeshDrawGPUBackend` | Backend | `IMeshDrawBackend` 两个实例，归 `MeshWorld` 所有，无 static 状态 |
| `RenderGraph/RGDrawList.cs`、`RGMeshCompile.cs`、`RGMeshOcclusion.cs` | Graph | 搬到 `MeshPipeline/Graph/`；RenderGraph 只保留 `IRGCompileExtension` |

---

## 4. World 层数据布局

### 4.1 表

全部是固定 index 的 SparseArray（`NativeArray` + 并行 `generation[]` + 空闲链表 + 64-slot 脏页位图）。五张表用一个泛型 `SlotTable<T>` 实现，替代现在五份复制的 Alloc / Free / Restore / Write。

| 表 | 索引空间 | 列（SoA） | GPU 镜像 |
|---|---|---|---|
| `MeshEntity` | `entityIndex` | `float4x4 transform`、`FBound bounds`、`uint flags`、`uint layer`、`uint renderingLayer`、`uint2 sceneCullingMask`、`customData[N]` | 是，按列上传 |
| `MeshElement` | `elementIndex` | `entityIndex`、`sectionId`、`materialId`、`EMeshPassMask passMask`、`byte lodIndex`、`int renderQueue` | 是 |
| `MeshSection` | `sectionIndex` | `meshUnityId`、`subMesh`、`indexCount/startIndex/baseVertex`、`refCount`、`revision` | 否 |
| `MeshMaterial` | `materialIndex` | `materialUnityId`、`renderQueue`、`shaderStateId`、`refCount`、`revision` | 否 |
| `MeshShaderState` | `shaderStateIndex` | `shaderUnityId`、`passIndex`、keyword hash、render state hash、`refCount` | 否 |

**决策（已确认）**：Entity 与 Transform 严格 1:1，`transformIndex == entityIndex`。删除 `TransformId`、`TransformRecord` 和 owner 映射。GPU 侧 `EntityTransforms[entityIndex]` 直接寻址。将来如需层级或共享 transform，重新引入独立表。

Section / Material / ShaderState 查找用 `NativeParallelHashMap<ulong, int>`，消除现在 `AllocOrUpdateSection/Material` 的线性扫描（`MeshScene.cs:751`、`:844`，O(N²) 加载）。

### 4.2 写入分两类

| 类别 | 例子 | 机制 |
|---|---|---|
| **结构性编辑** | 创建 / 销毁 Entity、增删 Element、换材质、换 mesh | `MeshWorldUpdate` 事务：undo log，`Dispose` 不 `Commit` 则回滚。refCount 归零的共享资源入队，`Commit` 时释放 |
| **每帧覆盖写** | transform、bounds、flags、customData | **绕过 undo log**（已确认的规范变更）。Burst job 直接写 `MeshEntity` 列和脏页位图。覆盖写幂等，失败只需丢弃本帧脏页 |

动态 transform 路径目标形态：

```text
TransformAccessArray ──IJobParallelForTransform──► 写 MeshEntity.transform / bounds / dirtyPage
                                                      （不回主线程，不逐 MonoBehaviour 处理）
主线程只处理：新增 / 删除 / Mobility 切换
```

现在的路径（`RenderContext.MeshTransforms.cs:75-108`）是 Burst 读完后回主线程逐个 `MarkTransformDirty` → `s_TransformUpdates` → 事务内逐个 `SetTransform`（两次胖 undo push）→ `AcceptTransform`。这是动态场景的主要瓶颈。

### 4.3 GPU 镜像 `MeshWorldGpu`

- 每列一个 `GraphicsBuffer`，64-slot 脏页 run 上传；静态场景上传 0 字节（保持现状）。
- 前一帧 transform 继续用 `MeshTransformVersionCache` 的不可变版本机制（motion vector 需要）。
- **Pass 层的 `elementToCommand` 与 `MeshDrawCommand` 表也走同一套脏页上传**，不再有单独的 candidate 表全量 `SetData`。

---

## 5. Pass 层：参数申请式 + 增量 MeshDrawCommand 缓存

### 5.1 `MeshPassDesc`

```csharp
public struct MeshPassDesc : IEquatable<MeshPassDesc>   // field-level Equals
{
    public int               lightMode;      // InfinityPassIDs.*
    public RenderQueueRange  queueRange;
    public EMeshPassMask     requireMask;    // Depth | GBuffer | Forward | Motion | Shadow | Translucent...
    public EMeshSortMode     sortMode;       // 见 §7
    public bool              mergeInstances; // 半透明 = false
    public RenderStateBlock  stateOverride;  // 可选
}
```

内置 9 个 Pass 退化为 9 个预设常量（`MeshPassPresets.GBuffer` 等）。`MeshPassId` enum、`MeshPassTypes.cs`、`PassRegistry.cs` 删除。

### 5.2 Interning

```text
CreateDrawList(view, desc)
   → MeshWorld.Passes.Intern(desc)        规范化 + Equals 查表 → MeshPassHandle
   → 首次：对全部 Element 跑一次 Burst 并行 build（只发生一次）
   → 之后：结构事件增量维护；连续 N 帧无人申请则淘汰
```

作者看到的是 `DrawRenderers` 的写法，引擎拿到的是 UE 的缓存。

### 5.3 增量维护

| 事件 | 操作 | 复杂度 |
|---|---|---|
| 新增 Element（K 个） | 对每个活跃 Pass：算 `passMask & requireMask`、queue 范围；命中则 hash 查 / 建 Command，写 `elementToCommand[x]`，`refCount++` | O(K × Passes) |
| 换材质 / 换 mesh | 旧 Command `refCount--`，新 Command `refCount++`，改写一个 slot | O(1) |
| 移动 | 不触碰 Pass 层 | 0 |
| 删除 Element | 写回 -1，`refCount--`；归零的 Command 延迟释放（同 Section 现有做法） | O(1) |

现在的 `PassBinStore.Rebuild`（`PassBinStore.cs:181-233`）在任何结构变化时让 9 个 Pass 全部变脏并全量重建（managed `List.Sort(lambda)` + `Dictionary`），这是流式加载的 O(scene) 开销。

### 5.4 `MeshDrawCommand` 拆成两张同 index 的表

Unity 的 API 层无法缓存 PSO 和 binding（每次 draw 都要传 `Mesh` / `Material` / `passIndex` / MPB），所以 Unity 下缓存 MDC 的实际收益是**消除每帧的 `EntityIdToObject`、`FindPass` 和 MPB 重建**。

```csharp
// Burst 可见：排序、合并、GPU 上传只用这张
struct MeshDrawCommand          // GPU-RECORD
{
    int  shaderStateIndex, materialIndex, sectionIndex;   // dense rank，用于 sort key
    uint indexCount, startIndex, baseVertex;              // indirect args 元数据
    uint flags;
}
// 仅主线程 Submit 使用，同 index，Command 创建时填一次
struct MeshDrawCommandBinding { Mesh mesh; int subMesh; Material material; int passIndex; }
```

Submit 时每个 draw 只做 `binding[c]` 数组索引；MPB 每个 DrawList 设置一次，每个 draw 只改 `instanceOffset`。`Material` 被销毁时在结构事件里检测（`binding.material == null`），不在每帧。

---

## 6. View 层

`MeshView` 只描述"画谁"：frustum planes、`viewKey`、`EMeshViewKind`、`layerMask`、`sceneCullingMask`、`renderingLayerMask`、`excludeFlags`、视点（用于 depth 与 LOD）。

`MeshViewVisibility` 每帧产出三列，索引空间都是 `entityIndex`：

| 列 | 产生者 | 消费者 |
|---|---|---|
| `visible[e]` | Burst frustum job（CPU）或 `CullFrustum` kernel（GPU）；主视图可叠加两阶段 Hi-Z | Gather |
| `lodIndex[e]` | 按视点距离选择 | Gather（`element.lodIndex == lodIndex[entity]` 才收） |
| `depth[e]` | 视空间深度 | Sort |

同一视图的多个 DrawList（Depth、GBuffer、Motion）共享同一份 Visibility。CPU 与 GPU 使用同一个 `MeshVisibilityKey`（field-level Equals，frustum hash 只做加速）。现在 `MeshVisibilitySignature.Equals` 比较的是量化后的 64 位 hash（`MeshVisibilityCache.cs:42-47`），hash 成了身份，与 AGENTS §4 冲突。

CPU 后端现无遮挡剔除（`RefineVisibilityHiZ` 为空方法，`MeshVisibilityCache.cs:285`）。本提案不新增 CPU Hi-Z，但删除这个空方法。

---

## 7. DrawList 层：四步流水线与 SortKey

### 7.1 四步

```text
Gather  visible[entity] ∩ elementToCommand[x] ≠ -1 ∩ lod 匹配
        → VisibleMeshDrawCommand { commandIndex, sortKey, entityIndex }
Sort    ① 按 commandIndex 聚拢（radix）
        ② MeshInstancedDraw 按 EMeshSortMode 的 64-bit key（代表深度取成员 min depth）
        ③ draw 内 instance 按 depth front-to-back   ← 不破坏 merge 的免费 Early-Z
Merge   相邻同 commandIndex → MeshInstancedDraw { commandIndex, instanceOffset, instanceCount }
Emit    instanceToEntity[]  → CPU: 上传 + DrawMeshInstancedProcedural
                            → GPU: 稳定 compaction + BuildIndirectArgs
```

### 7.2 `EMeshSortMode`

| 模式 | Key 布局（高 → 低） | merge | 用途 |
|---|---|---|---|
| `State` | queue │ shaderState │ material │ section | ✅ | GBuffer、Forward |
| `FrontToBack` | queue │ depthBucket(4–6 bit) │ shaderState │ material | ✅（draw 级） | Depth prepass、Shadow |
| `StateThenDepth` | queue │ shaderState │ depthBucket │ material | ✅ | PSO 切换昂贵时 |
| `BackToFront` | queue │ ~depth（全精度） | ❌（一 element 一 draw） | 半透明 |

规则：
- 位段来自 dense rank（`shaderStateIndex` 等），**不是** Unity InstanceID。
- 每段饱和后打包（AGENTS §4）。饱和只影响排序质量。
- `sortMode` 放在 `MeshPassDesc`（不随视图变），depth 来自 View。

### 7.3 后端契约

```csharp
interface IMeshDrawBackend
{
    // 输入：MeshPassCache + MeshViewVisibility；输出：固定契约
    void Prepare(in MeshDrawListRequest req, out MeshInstancedDraw[] draws, out instanceToEntity);
    void Submit(in MeshDrawList list, IRasterCommands cmd);
}
```

| | CPU direct | GPU indirect |
|---|---|---|
| Gather + Sort + Merge | Burst JobHandle 链（并行 gather、radix sort） | Compaction kernel 读 `elementToCommand` 与 `visible`；`CommandBase` 由 refCount 前缀和得到 |
| 第 ③ 级顺序 | 自然得到 | 需**稳定 compaction**（per-command prefix scan），不能用现在的 `InterlockedAdd` |
| Emit | `SetBufferData` 上传 `instanceToEntity` | 直接在 GPU buffer 中 |
| Draw | `DrawMeshInstancedProcedural` 每个 `MeshInstancedDraw` 一次 | `DrawMeshInstancedIndirect` 每个一次 |

两者的 draw call 数相同（Unity 无 MDI）。GPU 后端的价值是去掉每实例 CPU 工作和上传。

GPU payload 容量按 `Σ refCount`（该 Pass 的成员数）分配，不再是现在的 `max(candidates, 全场景 InstanceHighWater)`（`MeshDrawGPUBackend.cs:434`）。

---

## 8. 边界情况：一条路径覆盖

| 场景 | 处理 | Command 缓存 |
|---|---|---|
| LOD | 每级 LOD 的 section 各自是 Element（带 `lodIndex`）；每视图选择是 Visibility 的一列 | ✅ |
| 半透明 | `mergeInstances = false`，`BackToFront`；一 element 一 draw | ✅ |
| Per-instance custom data | `MeshEntity.customData` 列，shader 用 `entityIndex` 寻址 | ✅ |
| 程序化 / 每帧几何 | 同一个 build 函数；Element 放帧作用域 transient 区段，每帧清空 | 状态 ✅，成员不缓存 |
| 多视图 / cascade | 共享 `MeshPassCache`，只有 Visibility 和 Sort 是每视图的 | ✅ |
| Mobility | 只决定是否进 `TransformAccessArray` 每帧读；不是独立代码路径 | ✅ |

Relevance 保留为**数据**：`EMeshPassMask` 在 Element 创建时由 material route 一次算好，存进 SoA；PassDesc 用 `requireMask` 筛，一条 AND。

---

## 9. 命名与索引空间

### 9.1 类型命名

原则：保留 UE 中描述"数据是什么"的词（DrawCommand、Visible…），丢掉描述"OOP 角色"的词（SceneProxy、SceneInfo、MeshBatch）。避开 Primitive（在图形 API、Filament、UE、本仓库目录中指四种粒度）。

| 层 | 类型 | 替换 |
|---|---|---|
| World | `MeshWorld`、`MeshWorldUpdate`、`MeshWorldGpu` | `MeshScene`、`MeshSceneUpdate`、`GpuScene`、`MeshGpuSceneSnapshot` |
| World | `MeshEntity`、`MeshEntityId` | `MeshInstanceRecord`、`MeshInstanceId`、`TransformId` |
| World | `MeshElement`、`MeshElementId` | `MeshDraw`、`MeshDrawId` |
| World | `MeshSection`、`MeshMaterial`、`MeshShaderState` | `MeshSectionRecord`、`MaterialDataRecord`、（新） |
| Pass | `MeshPassDesc`、`MeshPassHandle`、`MeshPassPresets`、`MeshPassCache` | `MeshPassId`、`MeshPassTypes`、`PassRegistry`、`PassBinStore` |
| Pass | `MeshDrawCommand`、`MeshDrawCommandBinding` | `MeshDrawCommandKey`、`MeshPassCommand` |
| Pass | `EMeshPassMask`、`EMeshSortMode` | `EPassEligibility`、`EMeshSortPolicy`/`MeshSortKey`/`MeshSortPlan` |
| View | `MeshView`、`MeshViewVisibility`、`MeshVisibilityKey` | `MeshView`、`MeshViewCullingResult`、`MeshVisibilitySignature`、`MeshGpuVisibilityKey` |
| DrawList | `MeshDrawList`、`VisibleMeshDrawCommand`、`MeshInstancedDraw` | `MeshDrawExtract`、`PassView`、`MeshDrawCommand`（旧语义） |
| Backend | `IMeshDrawBackend`、`MeshCpuBackend`、`MeshGpuBackend`、`MeshDrawPayload` | `MeshDrawPipeline`、`MeshDrawGPUBackend`、`MeshDrawGpuPayload`、`MeshDrawGpuStaging` |
| Graph | `RGDrawListHandle`（保留）、`RGDrawListContext`、`RGMeshCompile` | 位置从 `RenderGraph/` 移到 `MeshPipeline/Graph/` |

后缀约定：World 层实体名词；Pass 层 `MeshPass*` 或 `*DrawCommand`；View 层 `MeshView*`；DrawList 层 `Visible*`、`*Draw`、`*DrawList`。Enum 一律 `E` 前缀（`MeshPassId` 现在违规）。

**ID 约定**：自有类型化 ID 以 `Id` 结尾（`MeshEntityId`）；Unity 对象身份一律是 `ulong` 字段、以 `UnityId` 结尾（`meshUnityId`），不做成类型，避免与 Unity `EntityId` 碰撞。

### 9.2 索引空间约定（解决 Instance 歧义）

现在 "instance" 同时指逻辑对象、GPU instancing、cull slot 三样东西（AGENTS §5.6 专门写了一条规则区分 slot 和 transform index，就是在付这个歧义的成本）。

1. **"Instance" 只表示 GPU instancing**：一次 draw 内的序号。只出现在 `instanceCount`、`instanceOffset`、`instanceIndex`、`MeshInstancedDraw`。
2. 全系统只有 5 个索引空间，整数索引一律带空间后缀：

| 空间 | 含义 | 变量名 |
|---|---|---|
| entity | World 固定 index（= transform index） | `entityIndex` |
| element | World 固定 index | `elementIndex` |
| command | PassCache 固定 index | `commandIndex` |
| draw | DrawList 中第几个 `MeshInstancedDraw` | `drawIndex` |
| instance | 一次 draw 内的序号 | `instanceIndex` |

3. 跨空间映射数组命名为 `<From>To<To>`：`elementToCommand`、`elementToEntity`、`instanceToEntity`。

Shader 侧：

```hlsl
// 现在
transformBuffer[instanceIndexBuffer[SV_InstanceID + instanceIndexOffset]]
// 之后
uint entityIndex = InstanceToEntity[InstanceOffset + SV_InstanceID];
float4x4 world   = EntityTransforms[entityIndex];
```

### 9.3 目录

```text
Runtime/RendererCore/PrimitivePipeline/MeshPipeline/
  World/     MeshWorld, MeshWorldUpdate, MeshWorldGpu, SlotTable<T>, MeshEntity, MeshElement, MeshSection, MeshMaterial, MeshShaderState, MeshTransformVersionCache
  Pass/      MeshPassDesc, MeshPassHandle, MeshPassPresets, MeshPassCache, MeshDrawCommand, MeshDrawCommandBinding, EMeshPassMask, EMeshSortMode
  View/      MeshView, MeshViewVisibility, MeshVisibilityKey, MeshFrustumCullJob, MeshOcclusion
  DrawList/  MeshDrawList, VisibleMeshDrawCommand, MeshInstancedDraw, MeshDrawListJobs
  Backend/   IMeshDrawBackend, MeshCpuBackend, MeshGpuBackend, MeshDrawPayload
  Graph/     RGDrawListContext, RGMeshCompile, RGMeshOcclusion
Runtime/RendererCore/RenderGraph/   不 using InfinityTech.Rendering.MeshPipeline；只保留 IRGCompileExtension
```

依赖方向：`Graph → DrawList → Backend → View/Pass → World`。理想情况下 Runtime 拆成两个 asmdef 让编译器强制；本提案先用命名空间约定 + `static_check.py` 检查。

---

## 10. 规范变更（AGENTS.md / ARCHITECTURE.md）

实现时需要同步修改的规范条目：

| 条目 | 现状 | 变更 |
|---|---|---|
| AGENTS §5.1 | "`MeshSceneUpdate` undoes every mutation" | 改为"结构性编辑走事务与回滚；transform / bounds / flags / customData 的覆盖写不入 undo log"（**已确认**） |
| AGENTS §5.6 | "GPU culling indexes instance slots. Compacted indices … are transform indices" | 删除；替换为 §9.2 的五个索引空间约定 |
| AGENTS §5.10 | "`CreateInstance` rejects an owned transform" | 删除（Transform 表不再存在） |
| AGENTS §5 表 | 模块表 | 按 §3 重写 |
| AGENTS §5.13–14 | `NotifyInstanceChanged` 等公开 API | 公开 API 改名（`NotifyEntityChanged`）放在**单独一批**，带序列化迁移验证 |
| AGENTS §13 | 缺陷清单 | 新增"半透明 `MeshComponent` 无绘制路径"（实现 Translucent DrawList 后删除）；删除"bins sort only by CompareStructural" |
| ARCHITECTURE §7 | 整节 | 按本文件 §3–§9 重写；"Out of scope: GPU LOD" 改为 "LOD 是 Visibility 的一列" |
| 命名规则 | Enum `E` 前缀 | `MeshPassId` 消失，不再违规 |

---

## 11. 对现有代码的处置

| 动作 | 对象 |
|---|---|
| **删除**（死代码 / 空实现） | `MeshSortKey`、`MeshSortPlan`、`MeshSortField`、`defaultSort`、`defaultFilter`、`EMeshSortSemantic`、`ESortDirection`、`RefineVisibilityHiZ`、`MeshVisibilityDispatch`、`GpuScene.Clear/Update(block)`、`MeshDrawGpuStaging.Build`、`MeshComponent.SetCustomPrimitiveData` 空壳（由 customData 列替代）、`OnTransformChange` 及无调用者的 `EventUpdate`、`MeshAsset`/`MeshLODInfo`（LOD 改为 Element 的 `lodIndex`） |
| **删除**（被替代） | `MeshCandidateTableStore`、`PassRegistry`、`MeshPassTypes`、`MeshPassId` enum、`TransformId`/`TransformRecord`、`MeshGpuSceneSnapshot` |
| **重写** | `PassBinStore` → `MeshPassCache`；`MeshPassExtractJob` → DrawList 并行 jobs；`RenderContext.MeshTransforms` → 直写 World 的 job；GPU `Compact` → 稳定 compaction |
| **改名 + 搬目录** | §9.1 表 |
| **修正** | 静默 fallback 改 throw（`ResolvePassIndex < 0` continue、`UseDrawList` 返回 Invalid、`Draw` 对 Invalid no-op、强制 GpuIndirect 静默降级）；`MeshVisibilityKey` field-level Equals；拼写（`movebility`、`RenderTransfrom`、`CaculateWorldBound`、`BuiltinMeshesPasses`）；补 `Documentation~/mesh.md` |

---

## 12. 实施顺序与验收

按"先验证核心假设、再收敛命名"的顺序。改名放最后，因为它触及序列化字段和测试，等结构定型后返工最少。

| 批次 | 内容 | 验收 |
|---|---|---|
| **B0 正确性** | 半透明 DrawList 接入 `TranslucentPass`；删除死代码与空实现；静默 fallback 改 throw；补文档 | EditMode 通过；半透明 `MeshComponent` 在 T0/T1/T2 可见（截图） |
| **B1 World 热路径** | `SlotTable<T>`；transform = entity 合并；hash 查找；transform job 直写 + 绕过 undo；`MeshWorldGpu` 列式脏页 | 10k 全动 Proxy p50 **< 2 ms**（现 21.5 ms）；静态仍 0 字节上传；`MatrixDuplicateRatio` 指标删除 |
| **B2 Pass 缓存** | `MeshPassDesc` + interning；`MeshPassCache` 增量；`MeshDrawCommand` 双表；`elementToCommand` 脏页上传；删 `MeshCandidateTableStore` | spawn 1 物体 O(K)，不再全量重建；Submit 每 draw 零 `EntityIdToObject` |
| **B3 DrawList** | 四步并行 jobs；`EMeshSortMode` 四种 key；GPU 稳定 compaction；`IMeshDrawBackend` 实例化 | CPU / GPU 输出 `MeshInstancedDraw` 序列一致（EditMode 对比）；Depth prepass 有 front-to-back |
| **B4 收敛** | 改名、搬目录、断 RG ↔ Mesh 循环、AGENTS / ARCHITECTURE 同步 | `static_check.py` 依赖方向检查通过；公开 API 改名单独批次 + 迁移验证 |

每批次结束按 AGENTS §1 清理 intermediate 数据，截图验证记入 `Docs/FeatureStatus.md`。

---

## 13. 已确认的决策

| 决策 | 结论 |
|---|---|
| Transform 表 | 与 Entity 1:1 合并，删除 `TransformId` |
| 逻辑对象名 | `MeshEntity`（配合 §9.1 的 ID 约定） |
| GPU 镜像名 | `MeshWorldGpu` |
| 动态写入与 undo | 覆盖写绕过 undo log；AGENTS §5.1 相应修改 |
| Pass 定义 | 参数申请式 `MeshPassDesc` + interning，删除 enum/Registry |
| 半透明 | 接入 DrawList（不是注册时拒绝） |

## 14. 未决问题

1. **`MeshShaderState` 的 intern 键**：keyword set 与 render state 从 `Material` 读取的成本和时机（结构事件时读一次？`ApplyPassState` 后重算？）。需要看 `MaterialRouteUtility` 现有的缓存能否复用。
2. **GPU 稳定 compaction 的具体算法**：per-command prefix scan（两次 dispatch）还是 compaction 后 per-draw bitonic sort。前者简单，后者在 command 数极多时更省。
3. **Pass 淘汰阈值 N 帧**：Scene view / Preview 相机间歇申请，N 太小会反复全量 build。
4. **asmdef 拆分**：是否接受 Runtime 拆为 `Mesh` 与 `Graph` 两个程序集。拆分能让编译器强制依赖方向，但影响所有 `Runtime/RenderPipeline/Pass/` 的引用。
5. **`MeshAsset` ScriptableObject** 删除后，Editor 侧的 LOD 作者流程由什么承载（`MeshComponent` 上直接配 LOD 列表？）。
