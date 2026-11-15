using System;
using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class RGRendererListLifetimeTests
    {
        // Counts creations through the same generic loop the graph uses with its context creator.
        struct CountingCreator : IRGRendererListCreator
        {
            public int[] calls;

            public RendererList Create(ref RGRendererListRecord record)
            {
                calls[0]++;
                return RendererList.nullRendererList;
            }
        }

        static CountingCreator Creator() => new CountingCreator { calls = new int[1] };

        [Test]
        public void CreateWithoutUse_DoesNotCreateOnSrc()
        {
            var context = new RGRendererListContext();
            RGRendererListHandle unused = context.Declare(default(RendererListParams));

            context.ReleaseUnused();
            var creator = Creator();
            context.CreateLive(creator);

            Assert.AreEqual(0, context.CreateCallCount);
            Assert.AreEqual(0, creator.calls[0]);
            Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(unused));
        }

        [Test]
        public void UseAndLive_CreatesOnce()
        {
            var context = new RGRendererListContext();
            RGRendererListHandle used = context.Declare(default(RendererListParams));
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();

            var creator = Creator();
            context.CreateLive(creator);

            Assert.AreEqual(1, context.CreateCallCount);
            Assert.AreEqual(1, creator.calls[0]);
            Assert.AreEqual(ERGRendererListCompileState.Created, context.GetState(used));
        }

        [Test]
        public void CulledPass_DoesNotCreate()
        {
            var context = new RGRendererListContext();
            RGRendererListHandle unused = context.Declare(default(RendererListParams));
            RGRendererListHandle used = context.Declare(default(RendererListParams));
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();
            context.CreateLive(Creator());

            Assert.AreEqual(1, context.CreateCallCount);
            Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(unused));
            Assert.AreEqual(ERGRendererListCompileState.Created, context.GetState(used));
        }

        [Test]
        public void StaleOrUncreatedList_SubmitThrows()
        {
            var context = new RGRendererListContext();
            RGRendererListHandle used = context.Declare(default(RendererListParams));
            context.MarkLiveConsumer(used.index, 0);
            context.ReleaseUnused();
            var commands = new CommandBuffer();
            try
            {
                Assert.Throws<InvalidOperationException>(() => context.Submit(commands, used), "A live list that was never created is not drawn silently.");
                context.CreateLive(Creator());
                Assert.Throws<InvalidOperationException>(() => context.Submit(commands, used), "A created but invalid native list is not drawn silently.");
                context.ReleaseAll();
                Assert.IsFalse(context.IsLive(used));
                Assert.AreEqual(ERGRendererListCompileState.Released, context.GetState(used));
                Assert.Throws<InvalidOperationException>(() => context.Submit(commands, used), "A handle from a released graph is rejected.");
            }
            finally
            {
                commands.Release();
            }
        }
    }
}
