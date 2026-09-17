using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class RGRendererListLifetimeTests
    {
        static RendererListDesc DummyDesc() => default;

        [Test]
        public void CreateWithoutUse_DoesNotCreateOnSrc()
        {
            var context = new RGRendererListContext();
            RGRendererListRef unused = context.Declare(DummyDesc());

            context.ReleaseUnused();
            context.CreateLiveWith(_ => RendererList.nullRendererList);

            Assert.AreEqual(0, context.CreateCallCount);
            Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(unused));
        }

        [Test]
        public void UseAndLive_CreatesOnce()
        {
            var context = new RGRendererListContext();
            RGRendererListRef used = context.Declare(DummyDesc());
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();

            int factoryCalls = 0;
            context.CreateLiveWith(_ =>
            {
                factoryCalls++;
                return RendererList.nullRendererList;
            });

            Assert.AreEqual(1, context.CreateCallCount);
            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(ERGRendererListCompileState.Created, context.GetState(used));
        }

        [Test]
        public void CulledPass_DoesNotCreate()
        {
            var context = new RGRendererListContext();
            RGRendererListRef unused = context.Declare(DummyDesc());
            RGRendererListRef used = context.Declare(DummyDesc());
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();
            context.CreateLiveWith(_ => RendererList.nullRendererList);

            Assert.AreEqual(1, context.CreateCallCount);
            Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(unused));
            Assert.AreEqual(ERGRendererListCompileState.Created, context.GetState(used));
        }

        [Test]
        public void StaleRef_DrawRejects()
        {
            var context = new RGRendererListContext();
            RGRendererListRef used = context.Declare(DummyDesc());
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();
            context.CreateLiveWith(_ => RendererList.nullRendererList);
            context.ReleaseAll();

            Assert.IsFalse(context.IsLiveRef(used));
            Assert.IsFalse(context.TryGetCreated(used, out _));
            Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(used));

            var commands = new CommandBuffer();
            try
            {
                Assert.DoesNotThrow(() => context.Submit(commands, used));
            }
            finally
            {
                commands.Release();
            }
        }
    }
}
