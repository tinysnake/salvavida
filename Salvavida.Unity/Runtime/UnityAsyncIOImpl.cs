using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

#if USE_UNITASK
using Cysharp.Threading.Tasks;
#endif

namespace Salvavida.Unity
{
    public class UnityAsyncIOImpl : AsyncIO
    {
        private Task _threadedTask;

        public bool IsRunningAsyncJob => _threadedTask != null && !_threadedTask.IsCompleted;

        public UnityAsyncIOImpl(GameObject parentObject = null)
        {
            if (!parentObject)
            {
                parentObject = GameObject.Find("/UnityAsyncIOImpl");
                if (!parentObject)
                    parentObject = new GameObject("UnityAsyncIOImpl");
                UnityEngine.Object.DontDestroyOnLoad(parentObject);
            }

            ParentObject = parentObject;
            if (!ParentObject.TryGetComponent<UnityAsyncIOScript>(out var script))
                script = ParentObject.AddComponent<UnityAsyncIOScript>();
            script._asyncIOList.Add(this);
        }

        public GameObject ParentObject { get; private set; }

        internal void RunJobThreaded()
        {
            if (IsRunningAsyncJob)
                return;
            _threadedTask = CreateJobThreaded();
            if (_threadedTask.IsFaulted)
            {
                PrintException(_threadedTask.Exception);
            }
        }

        private Task CreateJobThreaded()
        {
            return Task.Run(RunJobs);
        }

        private void PrintException(Exception ex)
        {
            if (ex is AggregateException aex)
            {
                foreach (var iex in aex.Flatten().InnerExceptions)
                {
                    PrintException(iex);
                }
            }
            else
            {
                Debug.LogException(ex);
            }
        }

        internal void CompleteFinishedJobsInternal()
        {
            CompleteFinishedJobs();
        }

        public override void ForceComplete()
        {
            _threadedTask = CreateJobThreaded();
            if (IsRunningAsyncJob)
                _threadedTask.Wait();
            CompleteFinishedJobs();
        }

        public override void Dispose()
        {
            ForceComplete();
            _threadedTask = null;
            if (ParentObject)
                ParentObject.GetComponent<UnityAsyncIOScript>()._asyncIOList.Remove(this);
        }
    }

    public class UnityAsyncIOScript : MonoBehaviour
    {
        internal List<UnityAsyncIOImpl> _asyncIOList = new();

        private void Update()
        {
            foreach (var _asyncIO in _asyncIOList)
            {
                if (!_asyncIO.IsRunningAsyncJob && _asyncIO.HasJobsToRun)
                {
#pragma warning disable CS4014 // UniTaskVoid thing
                    _asyncIO.RunJobThreaded();
#pragma warning restore CS4014
                }
                _asyncIO.CompleteFinishedJobsInternal();
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (!pause)
                return;
            foreach (var asyncIO in _asyncIOList)
                asyncIO.ForceComplete();
        }

        private void OnApplicationQuit()
        {
            foreach (var asyncIO in _asyncIOList)
                asyncIO.ForceComplete();
        }
    }
}
