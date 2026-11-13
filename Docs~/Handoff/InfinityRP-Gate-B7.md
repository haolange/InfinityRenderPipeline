# 闸门 B7：本地验收清单（含 B6f-2 遗留）

分支 `claude/kind-knuth-d599st`，上一个闸门 `110c830`（B6f-2，PASS）。通用流程、判定标准与回报模板见
`InfinityRP-Local-Verification-Handoff.md`；本文只列这一闸门特有的内容。云端无法编译 shader 与 Burst，本批所有 HLSL 与 job 改动在此之前都是未验证的。

## 改了什么

- 每视图的可见性产物全部删除（CPU 剔除 job 与 interning、GPU frustum batch、常驻 occlusion 缓冲）。每个 draw list 在自己的
  producer 里做 mask / LOD / 视锥 / 遮挡测试：GPU 为一个 `ComputeMeshDrawList` pass（`InitializeIndirectArgs` + `BuildDrawList*`），
  CPU 为 `UploadMeshData` + 泛型 gather job。
- `MeshCullingContext` 替换 `MeshView`。occlusion 改为每 context 一个 occluder 槽（history Hi-Z → 本帧 early Hi-Z），同一 context+pass 的
  第二个 depth list 只画第一次没画的候选（原来的 recovery）。
- Frame Debugger 名称：`ComputeMeshCulling` 与 `ComputeMeshCompaction` 消失，换成 `ComputeMeshDrawList`（内含 pass token、
  `InitializeIndirectArgs`、`BuildDrawList`）。CLI 字段 `lastCameraRecordedCompaction*` / `lastCameraRecordedCullDispatches` 改为
  `lastCameraRecordedDrawListProductions` / `lastCameraRecordedDrawListDispatches`；runtime probe 去掉 `allocatedViewCount`。

## 头号怀疑

1. shader：`Compute_MeshDrawPipeline.compute` 的三个 build kernel 与 `MeshOcclusion.hlsl`；plain kernel 不得引用 occluder 或 drawn 表
   （否则 Unity 报 "property not set"）。
2. Burst：`MeshDrawGatherJob<StateSortKey>` 与 `MeshDrawGatherJob<TranslucentColorSortKey>` 能否 Burst 编译（Console 中 Burst 错误即 RETURN）。
3. GPU 时间证伪（计划写定的条件）：Spazon 主相机一帧内所有 `ComputeMeshDrawList` 的 GPU 时间之和，对比 `d7cef36` 基线中
   `ComputeMeshCulling + ComputeMeshCompaction + UploadMeshData` 之和（各 300 帧均值，同机位）。新值更高即 RETURN-PERF，附两组数字。

## 编译

1. 默认 Editor 编译（Runtime、Editor、Tests、Validation），定义 `INFINITY_UNITY_CLI` 后再编译 `Editor/Cli`。
2. release 脚本编译改用 B6 闸门用过的 AssemblyBuilder 方案（`CompilePlayerScripts` 卡在 UGUI 包，与本仓库无关），回报 Runtime 与
   Validation 的结果与全部错误。
3. Shader 与 Burst：刷新后 Console 新窗口里的全部 shader 错误/警告与 Burst 错误。

允许本地机械修复：漏 `using`、include 路径、`.meta` 缺失。其余 RETURN。

## 必跑测试

全量 EditMode，与 B6f-2 结果（833 / 11 / 1）对比；本批改写或新增的测试：MeshCullingContextTests、MeshOcclusionGpuTests、
MeshDrawListLifetimeTests（含 Occluder_LeavesARemainderOnlyForItsContext、HistoryOccluder_RequiresTheAcceptedTransformVersion）、
MeshLODTests、MeshDrawListTests、MeshDrawProfilingTests、MeshOcclusionIntegrationTests、UnityEntityIdentityTests。
MeshOcclusionGraphTests 已删除；测试总数会变化，按名单逐项回报。

## 画面（1445×813，对比 B6f-2）

- Spazon、BoxMatix：Play mode 固定机位、移动机位、TAA 第 30 帧；mesh backend 分别强制 GPU Indirect 与 CPU Direct。
- 两段 occlusion：Spazon 运行至少 4 帧（history 有效）后快速移动一个遮挡物，被遮挡物体不得缺帧或闪烁；相机瞬移（reset）帧无缺物体。
- LOD 交叉淡化（有 LODGroup 语义资产的场景）、半透明 T0/T1/T2、cascade 阴影。
- B6f-2 遗留：带阴影的点光（6 面）与聚光各一个样本、Scene view 比对、DebugView 光源复杂度视图。

## 性能

GC：Spazon Play mode 暖机 300 帧后 runtime probe 一个窗口，frameAllocations 应为 0。GPU 时间见"头号怀疑"第 3 条。

## 中间数据

放在 `<Example 项目根>/intermediate/irp-refactor/<run-id>/`，验收后删除并写明回收空间。
