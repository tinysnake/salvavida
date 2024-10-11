using UnityEngine;
using System.Threading.Tasks;

#if USE_UNITASK
using Cysharp.Threading.Tasks;
#endif

namespace Salvavida.Unity
{
    public class UnityAsyncIOImpl : AsyncIO
    {
        private Task _threadedTask;

        public bool IsRunningAsyncJob => _threadedTask != null && !_threadedTask.IsCompleted;

        public UnityAsyncIOImpl(GameObject parentObject)
        {
            if (!parentObject)
            {
                parentObject = new GameObject("UnityAsyncIOImpl");
                Object.DontDestroyOnLoad(parentObject);
            }

            ParentObject = parentObject;
            if (!ParentObject.TryGetComponent<UnityAsyncIOScript>(out var script))
                script = ParentObject.AddComponent<UnityAsyncIOScript>();
            script._asyncIO = this;
        }

        public GameObject ParentObject { get; private set; }

#if USE_UNITASK
        internal async UniTaskVoid RunJobThreaded()
#else
        internal async void RunJobThreaded()
#endif
        {
            _threadedTask = Task.Run(RunJobs);
            await _threadedTask;
        }

        internal void CompleteFinishedJobsInternal()
        {
            CompleteFinishedJobs();
        }

        public override void ForceComplete()
        {
            if (IsRunningAsyncJob)
                _threadedTask.Wait();
            CompleteFinishedJobs();
        }
    }

    public class UnityAsyncIOScript : MonoBehaviour
    {
        internal UnityAsyncIOImpl _asyncIO;

        private void Update()
        {
            if (!_asyncIO.IsRunningAsyncJob && _asyncIO.HasJobsToRun)
            {
#pragma warning disable CS4014 // UniTaskVoid thing
                _asyncIO.RunJobThreaded();
#pragma warning restore CS4014
            }
            _asyncIO.CompleteFinishedJobsInternal();
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause)
                return;
            _asyncIO.ForceComplete();
        }
    }
}
