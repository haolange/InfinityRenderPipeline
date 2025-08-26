# RenderGraph API 重构文档

## 概述

本次重构对 InfinityRenderPipeline 的 RenderGraph 系统进行了重大升级，引入了意图驱动的 `EAccessFlag` 系统，实现了自动化的 Load/Store Action 推导和 Pass 合并优化。

## 核心变更

### 1. 新增 EAccessFlag 枚举

```csharp
public enum EAccessFlag : byte
{
    Read,      // 读取操作
    Write,     // 写入操作，保留现有内容
    WriteAll,  // 全屏写入，优化LoadAction
    Discard,   // 写入但不关心现有内容
    ReadWrite, // 读写操作
}
```

### 2. 新的 API

#### 颜色附件设置
```csharp
// 旧 API
passRef.SetColorAttachment(texture, 0, RenderBufferLoadAction.Clear, RenderBufferStoreAction.Store);

// 新 API - 推荐使用
passRef.SetColorAttachment(texture, 0, EAccessFlag.WriteAll);
```

#### 深度附件设置
```csharp
// 旧 API
passRef.SetDepthStencilAttachment(depthTexture, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, EDepthAccess.ReadOnly);

// 新 API - 推荐使用
passRef.SetDepthAttachment(depthTexture, EAccessFlag.Read);
```

#### 输入附件设置（新功能）
```csharp
// 用于 Subpass 合并优化
passRef.SetInputAttachment(inputTexture, EAccessFlag.Read);
```

#### Pass 合并控制
```csharp
// 控制是否允许 Pass 合并优化
passRef.AllowPassMerge(true);
```

### 3. 自动化优化

#### Load/Store Action 自动推导
- **EAccessFlag.WriteAll**: 自动推导为 `Clear` 或 `DontCare`
- **EAccessFlag.Write**: 根据是否首次写入推导 LoadAction
- **EAccessFlag.Read**: 自动推导为 `Load`
- **EAccessFlag.Discard**: 自动推导为 `DontCare`

#### Pass 合并优化
- **常规 Pass 合并**: 相同 Render Target 配置的连续 Pass
- **Subpass 合并**: 使用 InputAttachment 的 Pass 合并为 Subpass

### 4. AsyncCompute Bug 修复

修复了 AsyncCompute Pass 的依赖分析问题：
- 避免数组越界访问
- 正确处理纯 AsyncCompute 工作流
- 改进同步点检测逻辑

## 使用示例

### 深度预Pass
```csharp
using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<DepthPassData>(sampler))
{
    // 深度预Pass，全屏写入深度
    passRef.SetDepthAttachment(depthTexture, EAccessFlag.WriteAll);
    
    passRef.SetExecuteFunc((passData, encoder, pool) => {
        // 渲染深度
    });
}
```

### 前向渲染Pass
```csharp
using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<ForwardPassData>(sampler))
{
    // 全屏写入颜色，只读深度用于测试
    passRef.SetColorAttachment(colorTexture, 0, EAccessFlag.WriteAll);
    passRef.SetDepthAttachment(depthTexture, EAccessFlag.Read);
    
    passRef.SetExecuteFunc((passData, encoder, pool) => {
        // 前向渲染
    });
}
```

### Subpass 合并示例
```csharp
// Pass 1: GBuffer 写入
using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<GBufferData>(sampler))
{
    passRef.SetColorAttachment(gbufferTexture, 0, EAccessFlag.WriteAll);
    passRef.AllowPassMerge(true);
}

// Pass 2: 使用 GBuffer 进行光照计算
using (RGRasterPassRef passRef = m_RGBuilder.AddRasterPass<LightingData>(sampler))
{
    // 关键：声明输入附件，触发 Subpass 合并
    passRef.SetInputAttachment(gbufferTexture, EAccessFlag.Read);
    passRef.SetColorAttachment(lightingTexture, 0, EAccessFlag.WriteAll);
    passRef.AllowPassMerge(true);
}
```

### AsyncCompute Pass
```csharp
using (RGComputePassRef passRef = m_RGBuilder.AddComputePass<ComputeData>(sampler))
{
    passRef.EnableAsyncCompute(true);  // 现在可以安全使用
    passRef.WriteTexture(outputTexture);
    
    passRef.SetExecuteFunc((passData, encoder, pool) => {
        // 异步计算
    });
}
```

## 兼容性

- **向后兼容**: 旧的 API 仍然可用，但推荐迁移到新 API
- **渐进迁移**: 可以在同一个 RenderGraph 中混用新旧 API
- **性能提升**: 新 API 能够实现更好的自动化优化

## 性能优势

1. **自动化 Load/Store Action 优化**: 减少不必要的内存带宽消耗
2. **Pass 合并**: 减少 RenderPass 切换开销
3. **Subpass 合并**: 利用 Tile-Based 硬件特性，数据在 On-Chip 内存中传递
4. **AsyncCompute 稳定性**: 修复依赖分析 bug，支持更复杂的计算工作流

## 注意事项

1. 使用 `EAccessFlag.WriteAll` 时确保真的会写入所有像素
2. 使用 `EAccessFlag.Discard` 时确保不会读取未定义区域
3. Subpass 合并需要硬件支持，在不支持的平台会自动降级
4. Pass 合并是一个优化提示，编译器会根据实际情况决定是否合并

## 未来扩展

本次实现为 Pass 合并和 Subpass 合并提供了基础框架，后续版本将实现：
- 更复杂的合并条件分析
- 多 Pass 链合并
- 动态合并决策
- 性能分析工具集成