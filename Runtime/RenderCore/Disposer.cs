using System;

namespace InfinityTech.Core
{
    [Serializable]
    public abstract class Disposer : IDisposable
    {
        private bool m_IsDisposed = false;

        public Disposer()
        {
            
        }

        ~Disposer()
        {
            Dispose(false);
        }

        protected abstract void DisposeManaged();

        protected virtual void DisposeUnManaged() 
        {

        }

        private void Dispose(bool disposing)
        {
            if (!m_IsDisposed)
            {
                if (disposing)
                {
                    DisposeManaged();
                }
                DisposeUnManaged();
            }
            m_IsDisposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
