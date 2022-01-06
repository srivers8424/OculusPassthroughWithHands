﻿using DilmerGames.Core.Singletons;
using System;
using System.Collections.Generic;
using UnityEngine;

public class SpatialAnchorsManager : Singleton<SpatialAnchorsManager>
{
    [SerializeField]
    public GameObject anchorPrefab;

    public const ulong invalidAnchorHandle = ulong.MaxValue;

    public enum StorageLocation
    {
        LOCAL = 0
    }

    public Dictionary<ulong, ulong> locateAnchorRequest = new Dictionary<ulong, ulong>();

    public Dictionary<ulong, GameObject> resolvedAnchors = new Dictionary<ulong, GameObject>();

    private const string numUuids = "numUuids";

    void Start()
    {
        // Bind Spatial Anchor API callbacks
        OVRManager.SpatialEntityStorageSave += OVRManager_SpatialEntityStorageSave;
        OVRManager.SpatialEntityQueryResults += OVRManager_SpatialEntityQueryResults;
        OVRManager.SpatialEntityQueryComplete += OVRManager_SpatialEntityQueryComplete;
        OVRManager.SpatialEntityStorageErase += OVRManager_SpatialEntityStorageErase;
        OVRManager.SpatialEntitySetComponentEnabled += OVRManager_SpatialEntitySetComponentEnabled;
    }

    void OnDestroy()
    {
        // UnBind Spatial Anchor API callbacks
        OVRManager.SpatialEntityStorageSave -= OVRManager_SpatialEntityStorageSave;
        OVRManager.SpatialEntityQueryResults -= OVRManager_SpatialEntityQueryResults;
        OVRManager.SpatialEntityQueryComplete -= OVRManager_SpatialEntityQueryComplete;
        OVRManager.SpatialEntityStorageErase -= OVRManager_SpatialEntityStorageErase;
        OVRManager.SpatialEntitySetComponentEnabled -= OVRManager_SpatialEntitySetComponentEnabled;
    }


    #region CallBacks

    private void OVRManager_SpatialEntityStorageSave(ulong requestId, ulong space,
      bool result, OVRPlugin.SpatialEntityUuid uuid)
    {
        Logger.Instance.LogInfo("SpatialAnchorSaved requestId: " + requestId + " space: " + space + " result: " + result + " uuid: " + GetUuidString(uuid));

        // Write uuid of saved anchor to file
        if (!PlayerPrefs.HasKey(numUuids))
        {
            PlayerPrefs.SetInt(numUuids, 0);
        }
        int playerNumUuids = PlayerPrefs.GetInt(numUuids);
        PlayerPrefs.SetString("uuid" + playerNumUuids, GetUuidString(uuid));
        PlayerPrefs.SetInt(numUuids, ++playerNumUuids);
    }

    private void OVRManager_SpatialEntityQueryResults(ulong requestId, int numResults, OVRPlugin.SpatialEntityQueryResult[] results)
    {
        for (int i = 0; i < numResults; i++)
        {
            var uuid = results[i].uuid;
            var space = results[i].space;
            Logger.Instance.LogInfo("SpatialEntityQueryResult requestId: " + requestId + " space: " + space + " uuid: " + GetUuidString(uuid));
            TryEnableComponent(space, OVRPlugin.SpatialEntityComponentType.Storable);
            TryEnableComponent(space, OVRPlugin.SpatialEntityComponentType.Locatable);
        }
    }

    private void OVRManager_SpatialEntityQueryComplete(ulong requestId, bool result, int numFound)
    {
        Logger.Instance.LogInfo("SpatialEntityQueryComplete requestId: " + requestId + " result: " + result + " numFound: " + numFound);
    }

    private void OVRManager_SpatialEntityStorageErase(ulong requestId, bool result,
        OVRPlugin.SpatialEntityUuid uuid, OVRPlugin.SpatialEntityStorageLocation location)
    {
        Logger.Instance.LogInfo("SpatialEntityStorageErase requestID: " + requestId + " result: " + result + " uuid: " + GetUuidString(uuid) + " location: " + location);
    }

    private void OVRManager_SpatialEntitySetComponentEnabled(ulong requestId, bool result,
        OVRPlugin.SpatialEntityComponentType componentType, ulong space)
    {
        if (locateAnchorRequest.ContainsKey(requestId) &&
            !resolvedAnchors.ContainsKey(locateAnchorRequest[requestId]))
        {
            CreateAnchorGameObject(locateAnchorRequest[requestId]);
        }
    }

    private void CreateAnchorGameObject(ulong anchorHandle)
    {
        // Create anchor gameobject
        GameObject anchorObject = Instantiate(anchorPrefab);

        // Add gameobject to dictionary so it can be tracked
        resolvedAnchors.Add(anchorHandle, anchorObject);
    }

    private string GetUuidString(OVRPlugin.SpatialEntityUuid uuid)
    {
        byte[] uuidData = new byte[16];
        BitConverter.GetBytes(uuid.Value_0).CopyTo(uuidData, 0);
        BitConverter.GetBytes(uuid.Value_1).CopyTo(uuidData, 8);
        return AnchorHelpers.UuidToString(uuidData);
    }

    #endregion

    #region CRUD Operations
    public ulong CreateSpatialAnchor(Transform transform)
    {
        OVRPlugin.SpatialEntityAnchorCreateInfo createInfo = new OVRPlugin.SpatialEntityAnchorCreateInfo()
        {
            Time = OVRPlugin.GetTimeInSeconds(),
            BaseTracking = OVRPlugin.GetTrackingOriginType(),
            PoseInSpace = OVRExtensions.ToOVRPose(transform, false).ToPosef()
        };

        ulong anchorHandle = AnchorSession.kInvalidHandle;
        if (OVRPlugin.SpatialEntityCreateSpatialAnchor(createInfo, ref anchorHandle))
        {
            Logger.Instance.LogInfo("Spatial anchor created with handle: " + anchorHandle);
        }
        else
        {
            Logger.Instance.LogError("OVRPlugin.SpatialEntityCreateSpatialAnchor failed");
        }

        TryEnableComponent(anchorHandle, OVRPlugin.SpatialEntityComponentType.Locatable);
        TryEnableComponent(anchorHandle, OVRPlugin.SpatialEntityComponentType.Storable);

        return anchorHandle;
    }

    public void QueryAllLocalAnchors()
    {
        Logger.Instance.LogInfo("QueryAllLocalAnchors called");
        var queryInfo = new OVRPlugin.SpatialEntityQueryInfo()
        {
            QueryType = OVRPlugin.SpatialEntityQueryType.Action,
            MaxQuerySpaces = 20,
            Timeout = 0,
            Location = OVRPlugin.SpatialEntityStorageLocation.Local,
            ActionType = OVRPlugin.SpatialEntityQueryActionType.Load,
            FilterType = OVRPlugin.SpatialEntityQueryFilterType.None,
        };

        ulong newReqId = 0;
        if (!OVRPlugin.SpatialEntityQuerySpatialEntity(queryInfo, ref newReqId))
        {
            Logger.Instance.LogInfo("OVRPlugin.SpatialEntityQuerySpatialEntity initiated");
        }
    }

    public void SaveAnchor(ulong anchorHandle, StorageLocation location)
    {
        Logger.Instance.LogInfo("SaveAnchor called on anchorHandle: " + anchorHandle);
        ulong saveRequest = 0;
        if (!OVRPlugin.SpatialEntitySaveSpatialEntity(ref anchorHandle, OVRPlugin.SpatialEntityStorageLocation.Local, OVRPlugin.SpatialEntityStoragePersistenceMode.IndefiniteHighPri, ref saveRequest))
        {
            Logger.Instance.LogInfo("OVRPlugin.SpatialEntitySaveSpatialEntity initiated for anchorHandle " + anchorHandle + " location " + location);
        }
    }

    public void DestroyAnchor(ulong anchorHandle)
    {
        Logger.Instance.LogInfo("DestroyAnchor called on anchorHandle: " + anchorHandle);

        // Destroy anchor gameObject
        if (resolvedAnchors.ContainsKey(anchorHandle))
        {
            var anchorObject = resolvedAnchors[anchorHandle].gameObject;
            resolvedAnchors.Remove(anchorHandle);
            Destroy(anchorObject);
        }

        // Destroy anchor in memory
        if (!OVRPlugin.DestroySpace(ref anchorHandle))
        {
            Logger.Instance.LogError("OVRPlugin.DestroySpace failed for anchorHandle " + anchorHandle);
        }
    }

    public void EraseAnchor(ulong anchorHandle)
    {
        Logger.Instance.LogInfo("EraseAnchor called on anchorHandle: " + anchorHandle);

        // Destroy anchor gameObject
        if (resolvedAnchors.ContainsKey(anchorHandle))
        {
            Destroy(resolvedAnchors[anchorHandle].gameObject);
            resolvedAnchors.Remove(anchorHandle);
        }

        // Erase anchor from storage
        ulong eraseRequest = 0;
        if (!OVRPlugin.SpatialEntityEraseSpatialEntity(ref anchorHandle, OVRPlugin.SpatialEntityStorageLocation.Local, ref eraseRequest))
        {
            Logger.Instance.LogError("OVRPlugin.SpatialEntityEraseSpatialEntity initiated for anchorHandle " + anchorHandle);
        }
    }

    private void TryEnableComponent(ulong anchorHandle, OVRPlugin.SpatialEntityComponentType type)
    {
        bool enabled;
        bool changePending;
        bool success = OVRPlugin.SpatialEntityGetComponentEnabled(ref anchorHandle, type, out enabled, out changePending);
        if (!success)
        {
            Logger.Instance.LogError("SpatialEntityGetComponentEnabled did not complete successfully");
        }

        if (enabled)
        {
            Logger.Instance.LogWarning("Component of type: " + type + " already enabled for anchorHandle: " + anchorHandle);
        }
        else
        {
            ulong requestId = 0;
            OVRPlugin.SpatialEntitySetComponentEnabled(ref anchorHandle, type, true, 0, ref requestId);
            Logger.Instance.LogInfo("Enabling component for anchorHandle: " + anchorHandle + " type: " + type + " requestId " + requestId);
            switch (type)
            {
                case OVRPlugin.SpatialEntityComponentType.Locatable:
                    locateAnchorRequest.Add(requestId, anchorHandle);
                    break;
                case OVRPlugin.SpatialEntityComponentType.Storable:
                    break;
                default:
                    Logger.Instance.LogError("Tried to enable component that's not supported");
                    break;
            }
        }
    }

    #endregion

    void LateUpdate()
    {
        foreach (var resolvedAnchor in resolvedAnchors)
        {
            var anchorHandle = resolvedAnchor.Key;
            var anchor = resolvedAnchor.Value;

            if (anchorHandle == invalidAnchorHandle)
            {
                Logger.Instance.LogError("Error: AnchorHandle invalid in tracking loop!");
                return;
            }

            // Set anchor gameobject transform to pose returned from LocateSpace
            var pose = OVRPlugin.LocateSpace(ref anchorHandle, OVRPlugin.GetTrackingOriginType());
            anchor.transform.position = pose.ToOVRPose().position;
            anchor.transform.rotation = pose.ToOVRPose().orientation;
        }
    }
}