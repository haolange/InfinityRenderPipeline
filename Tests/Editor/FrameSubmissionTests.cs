using System;
using NUnit.Framework;
using UnityEngine.Rendering;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class FrameSubmissionTests
    {
        sealed class Submission : IFrameSubmission
        {
            public Exception queueError, submitError, postSubmitError;
            public bool NativeSubmitted { get; private set; }
            public int queueCalls, submitCalls;
            public void Queue(CommandBuffer commands) { queueCalls++; if (queueError != null) throw queueError; }
            public void Submit()
            {
                submitCalls++;
                if (submitError != null) throw submitError;
                NativeSubmitted = true;
                if (postSubmitError != null) throw postSubmitError;
            }
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
            bool valid = FrameSubmission.Execute(submission, null, ref firstError, out bool submitted, out bool queued, out bool nativeSubmitted);
            Assert.AreEqual(1, submission.queueCalls);
            Assert.AreEqual(1, submission.submitCalls);
            Assert.AreEqual(!submitFails, submitted);
            Assert.AreEqual(!queueFails, queued);
            Assert.AreEqual(!submitFails, nativeSubmitted);
            Assert.AreEqual(!queueFails && !submitFails, valid);
            Assert.AreSame(queueFails ? queueError : submitFails ? submitError : null, firstError);
            if (firstError != null) StringAssert.Contains(queueFails ? "Queue" : "Submit", firstError.StackTrace);
        }

        [Test]
        public void PostSubmitObserverFailure_PreservesNativeAcceptanceWithoutAcceptingTheFrame()
        {
            var failure = new InvalidOperationException("Observer after native Submit");
            var submission = new Submission { postSubmitError = failure };
            Exception error = null;
            Assert.IsFalse(FrameSubmission.Execute(submission, null, ref error, out bool accepted, out bool queued, out bool native));
            Assert.IsFalse(accepted); Assert.IsTrue(queued); Assert.IsTrue(native); Assert.AreSame(failure, error);
        }

        [Test]
        public void EarlierCameraExceptionRemainsPrimary_WhenFinalSubmitAlsoFails()
        {
            Exception earlier = new InvalidOperationException("Camera recording failed first");
            Exception first = earlier;
            var submission = new Submission { submitError = new InvalidOperationException("Later Submit error") };
            Assert.IsFalse(FrameSubmission.Execute(submission, null, ref first, out bool submitted, out _, out bool nativeSubmitted));
            Assert.IsFalse(submitted); Assert.IsFalse(nativeSubmitted);
            Assert.AreSame(earlier, first);
        }
    }
}
