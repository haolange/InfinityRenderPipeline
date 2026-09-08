#import <Metal/Metal.h>
#include <atomic>
#include "IUnityGraphicsMetal.h"

struct DisplayProbe
{
    uint64_t pixelFormat;
    uint64_t width;
    uint64_t height;
    uint64_t samples;
    int32_t status;
};
static IUnityGraphicsMetalV2* s_Metal;

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_Metal = interfaces->Get<IUnityGraphicsMetalV2>();
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
    s_Metal = nullptr;
}
static void UNITY_INTERFACE_API Probe(int eventId, void* payload)
{
    DisplayProbe* result = static_cast<DisplayProbe*>(payload);
    if (!result) return;
    MTLRenderPassDescriptor* descriptor = s_Metal ? s_Metal->CurrentRenderPassDescriptor() : nil;
    id<MTLTexture> texture = descriptor.colorAttachments[0].texture;
    if (!texture)
    {
        std::atomic_ref<int32_t>(result->status).store(-1, std::memory_order_release);
        return;
    }
    result->pixelFormat = static_cast<uint64_t>(texture.pixelFormat);
    result->width = texture.width;
    result->height = texture.height;
    result->samples = texture.sampleCount;
    std::atomic_ref<int32_t>(result->status).store(1, std::memory_order_release);
}
extern "C" void* UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API InfinityCaptureMetalEvent()
{
    return reinterpret_cast<void*>(&Probe);
}
extern "C" int UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API InfinityCaptureMetalReady(void* payload)
{
    return std::atomic_ref<int32_t>(static_cast<DisplayProbe*>(payload)->status).load(std::memory_order_acquire);
}
extern "C" const char* UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API InfinityCaptureMetalFormatName(uint64_t value)
{
    switch (static_cast<MTLPixelFormat>(value))
    {
        case MTLPixelFormatBGRA8Unorm: return "BGRA8Unorm";
        case MTLPixelFormatBGRA8Unorm_sRGB: return "BGRA8Unorm_sRGB";
        case MTLPixelFormatRGBA8Unorm: return "RGBA8Unorm";
        case MTLPixelFormatRGBA8Unorm_sRGB: return "RGBA8Unorm_sRGB";
        case MTLPixelFormatRGBA16Float: return "RGBA16Float";
        case MTLPixelFormatRGBA32Float: return "RGBA32Float";
        case MTLPixelFormatRGB10A2Unorm: return "RGB10A2Unorm";
        case MTLPixelFormatBGR10_XR: return "BGR10_XR";
        case MTLPixelFormatBGR10_XR_sRGB: return "BGR10_XR_sRGB";
        default: return "OtherMetalPixelFormat";
    }
}
