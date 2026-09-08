using System;
using NUnit.Framework;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class FrameSubmissionTests
    {
        sealed class Submission : IFrameSubmission
        {
            public Exception queueError, submitError;
            public int queueCalls, submitCalls;
            public void Queue(CommandBuffer commands) { queueCalls++; if (queueError != null) throw queueError; }
            public void Submit() { submitCalls++; if (submitError != null) throw submitError; }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void QueueFailureStillAttemptsSubmit_AndPreservesOriginalException(bool queueFails, bool submitFails)
        {
            var queueError = new InvalidOperationException("Queue failure");
            var submitError = new InvalidOperationException("Submit failure");
            var submission = new Submission { queueError = queueFails ? queueError : null, submitError = submitFails ? submitError : null };
            Exception firstError = null;
            bool valid = FrameSubmission.Execute(submission, null, ref firstError, out bool submitted);
            Assert.AreEqual(1, submission.queueCalls);
            Assert.AreEqual(1, submission.submitCalls);
            Assert.AreEqual(!submitFails, submitted);
            Assert.AreEqual(!queueFails && !submitFails, valid);
            Assert.AreSame(queueFails ? queueError : submitFails ? submitError : null, firstError);
            if (firstError != null) StringAssert.Contains(queueFails ? "Queue" : "Submit", firstError.StackTrace);
        }

        [Test]
        public void EarlierCameraExceptionRemainsPrimary_WhenFinalSubmitAlsoFails()
        {
            Exception earlier = new InvalidOperationException("Camera recording failed first");
            Exception first = earlier;
            var submission = new Submission { submitError = new InvalidOperationException("Later Submit error") };
            Assert.IsFalse(FrameSubmission.Execute(submission, null, ref first, out bool submitted));
            Assert.IsFalse(submitted);
            Assert.AreSame(earlier, first);
        }
    }
}
