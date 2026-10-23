> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S4 本机验证报告

原始阶段 `f70837766999be3453987e11df75e2933e2995a0`，携带已接受的 S1/S2 修复；前序 main 修复为 `2157d21`。平台 Unity 6000.6.0f1 / CoreRP 17.6 / macOS Metal / Apple M3 Max。使用原 InfinityExample，无 Player 构建。源码及补丁身份见 S4-results.json。

| 门禁 | 实际结果 |
|---|---|
| Unity 编译 | 历史 S4、S3 A/B reference、main 修复候选均无 C# 编译错误。 |
| 原阶段测试 | QualityPresetTests 4/4；全部 Example 1553=1500 Passed /0 Failed /53 既有 Skipped。 |
| 修复后测试 | CloudShell GPU 3/3；全部 Example 1556=1503 Passed /0 Failed /53 既有 Skipped，reload/resume 2/2。 |
| 测试完整性 | 原生 XML 1523=1503/0/20，callback XML 补回 33 个真实 checkpointed skip；1556 稳定身份均发现/开始/结束。根仍 Skipped:Ignored，frameworkComplete=false、callbacksComplete=true，不称全部 PASS。 |
| main 针对性验证 | 新 Cloud GPU 3/3；QualityPresetTests 名称过滤实际发现 10/10（含后续质量测试），均无 skip。main 全量最终验收仍待后续阶段。 |
| 质量 UI | 四个生产 Volume Inspector：Custom 低层值保留、五档显示、艺术字段可编辑、原生 Copy 按钮、Undo/Redo 全 JSON 精确恢复。档位循环通过公开参数 API 驱动；没有宣称所有原生下拉菜单由自动化点击。 |
| Custom 图像保持 | S3＋共同云修复 对比 S4＋同一修复：GTAO、RTAO、fog、cloud 共 11 对缓冲逐字节一致。八个正常帧捕获均有 128 帧接受预热、相同矩阵/当前及前一 jitter/帧相位，捕获 1 帧，outstanding 0，全部退休。 |
| 云原始失败 | 五档均出现前景亮斑和缺失天空云，保留失败网格及原始身份。Frame Debugger 定位 ComputeVolumetricCloud > VolCloud_Trace > Compute_VolumetricCloud。 |
| 云修复画面 | 五档生产帧和中间 Cloud 输出有效；前景墙使 Cloud 精确 (0,0,0,1)，层内 1m /100m 墙 alpha 均值约 .994258 /.538415。 |
| 掠射及远平面 | camera 高 3000m、far=100m；中心射线两个云段约 [37842,130660] 与 [243446,336264]m，planet 无交点；Depth 全 clear，真实 Cloud 输出非 identity、有限。 |
| 局部光调用链 | camera 高 1000m，方向光禁用、cloud ambient=0，真实 Point 位于 1300m、range600；正对照 intensity1e6 时 LocalLightList Records、Cloud RGB 非零；归零后 Off、Cloud RGB 全零。Lighting 精确相同，Cloud/Display 不同。两次采样相位/jitter不同，不作只改光强的定量图像等价证明。该极端强度仅作路径控制，不是艺术质量基准。 |
| 离线门禁 | 最终 main 生产 cloud 和两个测试 kernel DXC 通过；static_check 0 errors /0 warnings，diff check 通过。 |
| 恢复 | 所有本任务 Editor 正常退出；场景/根/Transform、background=false、Play options1 恢复。GS 内存绑定撤销，完整 JSON 与原 source/meta 哈希保持，没有保存用户资产。 |

修复针对三类实证几何错误：高度函数坐标中心错误且提前 saturate；硬编码地球半径偏离 Profile；主射线/光线没有按真实云壳、planet 和不透明深度截断。新共享 helper 使用 Profile 半径、稳定球交点、按序两段壳区间和有限局部光距离；主射线及 LightMarch 共用。现有 jittered inverse VP 没有变更。正常捕获增加真实 VolumetricCloudBuffer 映射，未引入额外渲染或替代资源。

GPU 数值测试采用独立 double 边界中点分类 oracle，覆盖两种半径、层下/内/上、切线、双段、planet、opaque、有限光线和 exact-clear/near-clear depth。生产帧的几何与深度控制另外记录，不用 helper case 名称代替实际 local Light 路径。固定模拟时间的控制不证明动画/动态时域质量，未采集性能结论。主视口为窗口左下 320×180，其余 Game 区域可能保留旧像素；云密度/强度极端控制可能饱和，不作整体美术验收。

仍需明确保留：新 CloudShell.hlsl 在 Metal 发出 potentially-uninitialized 警告（CloudSceneRayLimit、CloudSphereIntersection、CloudRaySegments）。测试和有效生产控制通过，源码未发现未赋值分支；没有抑制警告，也不归为旧 SSR 警告。Shader 跨平台与完整外观质量仍 Open。

main 冷启动的第一条运行错误是 default Volume profile incomplete，来自当前 schema 的资源/Profile 预检；它独立于本次云修复，故 main 渲染未接受。新 GPU/质量测试通过不掩盖该阻断。原资产保持不变，持久资源升级/APV 在后续对应阶段及最终 HEAD 继续闭合。

搭建错误不计通过：最初 NotEditable scratch host、普通 Lit 用于 MeshComponent、墙旋转后未更新 bounds 的控制已作废并保留精简失败说明。补充脚本最初缺少 namespace/使用 obsolete API，以及 Vector3 JSON 循环，均在有效捕获之前修正。第一 session 的 initial-bind receipt 被后次冷启动覆盖，原 context restoration、测试/日志/源码身份仍保留；后次 receipt 没有冒充原件。

独立 verifier 已接受历史质量 UI、测试身份、生产云基本控制及 Custom A/B；补充生产控制也已独立接受；最后提交候选与精简报告也已独立接受，Fix S4 已提交并推送为 `279c336dad9801bee0c7bad491c521f6997b12c8`；远端原 PR 分支 SHA 一致、远端 main 不变、包工作区干净，S5 已解锁。S5–S17、main 最终集成、图像回归正负对照、有效计时与总清理未完成，PR #11 维持 Draft。FeatureStatus 和未验证测试标记未作提前更新。

清理：独立消费者完成后，607 个 scratch 文件/60,170,712 logical bytes 已退休，ExFAT 分配占用 391168→768 KiB。只留下精确 S4 replay patch/identity（149447 bytes）供 S5 构建，消费后即删；活跃 Library/DataStore 缓存未动。此前已消费的 raw buffer 分批清理，交付物为 S4/ 中27 payload、身份索引与 cleanup.json。
