using System;
using System.Threading;

namespace Tmrh.Core
{
    /// <summary>
    /// Only one copy of the tool may run for a Windows sign-in, so two copies can never write the saved settings file
    /// or the registry at the same time. Taken before anything else happens and held until the program exits.
    /// </summary>
    public sealed class InstanceLock : IDisposable
    {
        public const string DefaultName = @"Local\WeAreCrazy.TrackManiaRegistrationHelper";

        Mutex mutex;
        public bool Acquired { get; private set; }

        /// <summary>True when the previous holder exited without letting go (it crashed). We own the lock now and that is safe:
        /// the saved settings file is only ever swapped in whole, and an unfinished change shows as "didn't finish".</summary>
        public bool WasAbandoned { get; private set; }

        InstanceLock() { }

        public static InstanceLock TryAcquire(string name)
        {
            var l = new InstanceLock { mutex = new Mutex(false, name) };
            try { l.Acquired = l.mutex.WaitOne(0, false); }
            catch (AbandonedMutexException) { l.Acquired = true; l.WasAbandoned = true; }
            if (!l.Acquired) { l.mutex.Dispose(); l.mutex = null; }
            return l;
        }

        public void Dispose()
        {
            if (mutex == null) return;
            if (Acquired)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }   // already released, or another thread owns it
                Acquired = false;
            }
            mutex.Dispose();
            mutex = null;
        }
    }
}
