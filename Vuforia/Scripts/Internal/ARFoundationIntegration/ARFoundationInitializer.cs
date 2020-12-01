/*===============================================================================
Copyright (c) 2020 PTC Inc. All Rights Reserved.
Confidential and Proprietary - Protected under copyright and other laws.
Vuforia is a trademark of PTC Inc., registered in the United States and other
countries.
===============================================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

#if ARFOUNDATION_DEFINED
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace Vuforia.UnityRuntimeCompiled.ARFoundationIntegration
{
    public static class ARFoundationInitializer
    {
        static OpenSourceUnityARFoundationFacade sFacade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad()
        {
            InitializeFacade();
        }

        public static void InitializeFacade()
        {
            if (sFacade != null) return;

            sFacade = new OpenSourceUnityARFoundationFacade();
#if ARFOUNDATION_DEFINED
            UnityARFoundationFacade.Instance = sFacade;
#endif
        }
    }

#if ARFOUNDATION_DEFINED
    class OpenSourceUnityARFoundationFacade : IUnityARFoundationFacade
    {
        ARCameraManager   mCameraManager;
        ARPoseDriver      mPoseDriver;
        ARAnchorManager   mAnchorManager;
        ARSession         mSession;
        ARSessionOrigin   mSessionOrigin;
        ARRaycastManager  mRaycastManager;

        Dictionary<string, ARAnchor> mAnchors = new Dictionary<string, ARAnchor>();

        public event Action<ARFoundationImage> ARFoundationImageEvent = image => { };
        public event Action<Transform, long> ARFoundationPoseEvent = (pose, timestamp) => { };

        public event Action<List<(string, Transform)>, List<(string, Transform)>> AnchorsChangedEvent = (removed, updated) => {};

        public bool IsARFoundationScene()
        {
            var arSession = GameObject.FindObjectOfType<ARSession>();
            return arSession != null;
        }

        public IEnumerator CheckAvailability()
        {
            yield return ARSession.CheckAvailability();
        }

        public void FindDependencies()
        {
            mCameraManager = GameObject.FindObjectOfType<ARCameraManager>();
            mPoseDriver = GameObject.FindObjectOfType<ARPoseDriver>();
            mSession = GameObject.FindObjectOfType<ARSession>();
            mSessionOrigin = GameObject.FindObjectOfType<ARSessionOrigin>();
            mRaycastManager = GameObject.FindObjectOfType<ARRaycastManager>();

            mAnchorManager = mSessionOrigin.GetComponent<ARAnchorManager>();
            if (mAnchorManager == null)
                mAnchorManager = mSessionOrigin.gameObject.AddComponent<ARAnchorManager>();
        }

        public void Init()
        {
            mAnchorManager.anchorsChanged += OnAnchorsChanged;
            mCameraManager.frameReceived += OnFrameReceived;
        }

        public void Deinit()
        {
            ClearAnchors();
            mSession.Reset();
            mAnchorManager.anchorsChanged -= OnAnchorsChanged;
            mCameraManager.frameReceived -= OnFrameReceived;
        }

        public IEnumerator WaitCameraForReady()
        {
            var waitForEndOfFrame = new WaitForEndOfFrame();
            while (mCameraManager == null || mCameraManager.subsystem == null || !mCameraManager.subsystem.running ||
                !mCameraManager.permissionGranted)
            {
                yield return waitForEndOfFrame;
            }
        }

        public bool IsARFoundationReady()
        {
            return ARSession.state >= ARSessionState.Ready;
        }

        public Transform GetPose()
        {
            return mPoseDriver.transform;
        }

        public List<CameraMode> GetProfiles()
        {
            var profiles = new List<CameraMode>();
            using (var configurations = mCameraManager.GetConfigurations(Allocator.Temp))
            {
                if (!configurations.IsCreated || configurations.Length <= 0)
                    return profiles;

                foreach (var configuration in configurations)
                {
                    profiles.Add(new CameraMode
                    (
                        configuration.width,
                        configuration.height,
                        configuration.framerate ?? 30,
#if UNITY_IOS
                        2
#elif UNITY_ANDROID
                        3
#else
                        0
#endif
                    ));
                }
            }
            return profiles;
        }

        public bool SelectProfile(CameraMode profile)
        {
            using (var configurations = mCameraManager.GetConfigurations(Allocator.Temp))
            {
                if (!configurations.IsCreated || configurations.Length <= 0)
                    return false;

                var configs = new SortedDictionary<int, List<XRCameraConfiguration>>();
                foreach (var configuration in configurations)
                {
                    var framerate = configuration.framerate ?? 30;
                    if (!configs.ContainsKey(framerate))
                        configs.Add(framerate, new List<XRCameraConfiguration>());
                    configs[framerate].Add(configuration);
                }

                var selectedConfiguration = configs[(int) profile.Fps]
                    .First(x => x.width == profile.Width && x.height == profile.Height);

                mCameraManager.currentConfiguration = selectedConfiguration;
            }
            return true;
        }

        void OnFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (!mCameraManager.TryGetIntrinsics(out var cameraIntrinsics))
                return;
            if (!mCameraManager.TryAcquireLatestCpuImage(out var cameraImage))
                return;

            var timestamp = eventArgs.timestampNs ?? (long)(cameraImage.timestamp*1000000000);
            var image = new ARFoundationImage(
                cameraImage.GetPlane(0).data,
                cameraImage.GetPlane(1).data,
#if UNITY_ANDROID
                cameraImage.GetPlane(2).data,
#else
                new NativeArray<byte>(new byte[0], Allocator.None),
#endif
                cameraImage.GetPlane(0).rowStride,
                cameraImage.GetPlane(1).rowStride,
                cameraImage.GetPlane(1).pixelStride,
                timestamp,
                cameraIntrinsics.principalPoint,
                cameraIntrinsics.focalLength
            );
            ARFoundationImageEvent.Invoke(image);
            ARFoundationPoseEvent(mPoseDriver.transform, timestamp);
            cameraImage.Dispose();
        }

        void OnAnchorsChanged(ARAnchorsChangedEventArgs eventArgs)
        {
            var removed = new List<(string, Transform)>();
            foreach (var anchor in eventArgs.removed)
            {
                var uuid = anchor.trackableId.ToString();
                if (mAnchors.ContainsKey(uuid))
                {
                    removed.Add((uuid,anchor.transform));
                    mAnchors.Remove(uuid);
                }
            }
            var updated = new List<(string, Transform)>();
            foreach (var anchor in eventArgs.updated)
            {
                var uuid = anchor.trackableId.ToString();
                if (mAnchors.ContainsKey(uuid))
                {
                    updated.Add((uuid,anchor.transform));
                }
            }
            AnchorsChangedEvent.Invoke(removed, updated);
        }

        public bool RemoveAnchor(string uuid)
        {
            var result = false;
            if (mAnchors.ContainsKey(uuid))
                result = mAnchorManager.RemoveAnchor(mAnchors[uuid]);
            if (result)
                mAnchors.Remove(uuid);
            return result;
        }

        public string AddAnchor(Pose pose)
        {
            var anchor = mAnchorManager.AddAnchor(pose);
            if (anchor == null) return null;

            var id = anchor.trackableId.ToString();
            mAnchors[id] = anchor;
            return id;
        }

        public void ClearAnchors()
        {
            foreach (var anchor in mAnchors)
                mAnchorManager.RemoveAnchor(anchor.Value);
            mAnchors.Clear();
        }

        public bool HitTest(Vector2 screenPoint, out List<Pose> hitPoses)
        {
            var hits = new List<ARRaycastHit>();
            var hitSuccess = mRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon);
            hitPoses = hits.ConvertAll(hit => hit.pose);
            return hitSuccess;
        }
    }
#else
    class OpenSourceUnityARFoundationFacade {}
#endif // ARFOUNDATION_DEFINED
}

