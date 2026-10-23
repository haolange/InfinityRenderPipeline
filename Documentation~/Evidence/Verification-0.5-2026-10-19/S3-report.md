> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S3 本机验证报告

原始阶段为 `8032704`，候选加上已接受的 S1/S2 最小修复，前序 Fix S2 为 `e4096e4`。完整源码身份见 S3-results.json 的补丁 SHA，以及 S3/evidence-index.json → receipts → final-source-identity.json 的文件清单。平台为 Unity 6000.6.0f1 / CoreRP 17.6 / macOS Metal / Apple M3 Max；全程使用原 InfinityExample，没有 Player 构建。

| 门禁 | 实际结果 |
|---|---|
| Unity 编译 | 冷启动和运行成功，日志没有 C# 编译错误；不宣称全局 Console 从未出现错误。 |
| 阶段测试 | FeatureRegistry 5/5；原失败资源预检复验 1/1。 |
| 全部 Example EditMode | 1549 个测试均发现、开始、结束：1496 Passed、0 Failed、53 原有 Skipped。reload/resume 2/2。 |
| 测试证据完整性 | 原生 XML 1516=1496/0/20；另存派生 XML 仅补回 33 个真实 checkpointed skip，保留其原 XML 和稳定身份。未提升为全 PASS。 |
| GPU 数值 | Utility 部分清零、尾部哨兵、两组 args 清零、两类参数缓冲的 offset 12 与零 dispatch 均符合预期；缺失 texture/buffer 错误包含资源名。 |
| 实际 RenderGraph | 正常帧公共 FGraphics 任务内执行两条 RGComputeEncoder 间接派发和两个 Transfer readback，读写依赖显式声明；执行/终结 2/2，688 bytes 控制资源已释放。 |
| Debugger 和恢复 | SSR Off、Records、Blocked、恢复 Records 各有 32 个独立正常帧；原生截图显示完整 missing ssrShader 原因。11 个捕获帧、22 buffers 全部终结并退休，outstanding 0。 |
| 画面 | 匹配矩阵和当前/前一 jitter 后 Lighting 字节完全一致；Display 目视无新增几何或明显颜色异常，但并非逐像素一致。 |
| 恢复与新日志 | 恢复后另检查 32 个接受帧，新日志 Error/Exception 0。SSR/Utility 完整 JSON 恢复；正常退出后 GS/meta 与原字节一致，13 个登记的小型源输入未变。 |
| 离线门禁 | static_check 0 errors /0 warnings，git diff --check 通过。本次修正不改变生产 C#/HLSL，没有新增差分编译或 DXC 变更范围。 |

第一次全量是 1495/1/53：有效预检拒绝空 Utility slot。着色器路径、GUID 和两个 kernel 正常；未保存 clone 的公开 Populate 对照只补 Utility。普通 reload 不保证补全既有资产，容器 Reset 也不是只补 null 的操作。

曾尝试在完整 main schema 只赋值 Utility。当前 schema 的 JSON/Undo/两次保存/冷读检查均成立，但 main 不再认识旧 zBinningShader，保存丢失了这个历史字段，第二次全量因此仍为 1495/1/53。此前保全结论明确撤回。Editor 正常退出后，仅在当前 SHA 和 meta 精确匹配本任务写入身份时，撤回了整次保存操作。原件恢复为 SHA `cabd163193f185a27d3564ea735abcbaee81de103a49fdbf7dcbcb818be7ab3f`，meta 为 `09677976e5af1df19fa63616630ee2236159ea8f543cae07c74e93dbe12bbf38`。

最终测试使用显式、仅内存的 Utility 绑定，没有 SetDirty、保存、Populate/Reset 原件或 reload 自动补绑定。其余 21 个引用不变，两个 domain reload 后仍有效；结束时恢复完整原 JSON，正常退出后原件字节仍相同。**持久化资源升级没有完成、没有接受**。最终 HEAD 配置仍须按原生字段清单和允许差异另行闭合；当前 schema 的完整 JSON 不覆盖它已不认识的历史 native 字段。

匹配 S2 frame 449 / S3 frame 898 的 Lighting SHA 均为 `07dc4470a44309d69fc5cd84611916883a0b0146e22aacb48defeb7b1797ef7d`。Display 有 2794 个变化像素，RGB 平均绝对差 `1.2100914e-5`，最大 `0.4486694`；超过 0.001/0.01/0.05/0.1 的像素分别为 95/32/9/6。没有确定这些差异的根因，不宣称图像完全相同或 SSR 算法质量已接受。

验证搭建错误单独保留：早期 scratch script 委托类型错误、捕获 camera enum/ROI 错误均在执行前或参数守卫中被拒绝。首个 graph harness 的 null sampler 在 Execute 前失败（0 executed、queued false），不是生产 shader 失败。修正后真实 graph 数值通过；原未提交小型控制资源最终以正常 Editor 退出为回收边界，不描述成失败 harness 自行成功释放。

原生 Game 截图仍可见旧操作错误和旧 SSR uninitialized-variable 警告，不用于宣称全局 Console 清洁。最终单独新日志区间明确为 0 新 Error/Exception。旧 SSR 警告和算法质量归 S5 后续验收。

退出 Play 保留用户新上下文：clean untitled、Main Camera 和 Directional Light 两个根，根 GameObject/Transform JSON 与冷读快照一致，background false、Play options 1、selection null。没有用旧 Spazon 状态覆盖用户上下文。

独立 verifier 已接受上述历史 S3 运行检查点。文档候选修正资源生命周期和 schema 保全流程，不增加 Runtime fallback，已通过独立审核；Fix S3 为 `2157d2125c79fd638b94af63b1537b7cf1ae097c`，远端原 PR 分支 SHA 已核对相同；远端 main 保持 c92be77，包工作区干净。main 最终集成、S4–S17、基线正负对照、最终有效性能计时和总清理仍未完成，PR #11 继续 Draft。

精简证据位于同目录 S3/：原始失败/最终 XML、关键原生图和并排图、evidence-index.json（源码/配置/运行/捕获身份及 33 个原始跳过 leaf）。编译、数值、视觉和性能结论分别记录。

清理已完成当前阶段消费者：停止生产者后退休 407 个文件，包括 S2 六项保留输入、其任务 fixture 和已消费的 S3 raw/log/scratch，合并占用从 332800 降到 768 KiB（释放 332032 KiB）。只保留精确 replay patch 与 identity 给 S4 构建；原 GlobalSettings 前像继续复用 run02/preimage，不另存副本。统计中的 raw 绝对路径仅标识已退休原始来源；当前可打开的交付物是本目录的关键图片、原始/最终 XML、哈希与精简回执。清理详情见 S3/cleanup.json。
