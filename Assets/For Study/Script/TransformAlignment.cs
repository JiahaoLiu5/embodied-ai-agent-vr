using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class TransformAlignment : MonoBehaviour
{
    [Serializable]
    public class TransformPair
    {
        [Tooltip("The transform to follow.")]
        public Transform source;

        [Tooltip("The transform that will be aligned to the source.")]
        public Transform target;

        [Tooltip("Whether this pair is active.")]
        public bool enabled = true;

        [Header("Alignment Options")]
        public bool alignPosition = true;
        public bool alignRotation = true;
        public bool alignScale = true;
    }

    public enum UpdateMode
    {
        Update,
        LateUpdate,
        FixedUpdate
    }

    [Header("Transform Pairs")]
    public List<TransformPair> pairs = new List<TransformPair>();

    [Header("Runtime Settings")]
    [Tooltip("LateUpdate is usually best if the source object is animated or tracked.")]
    public UpdateMode updateMode = UpdateMode.LateUpdate;

    [Tooltip("If enabled, alignment also works in Edit Mode.")]
    public bool alignInEditMode = true;

    private void Update()
    {
        if (updateMode == UpdateMode.Update)
        {
            AlignAll();
        }
    }

    private void LateUpdate()
    {
        if (updateMode == UpdateMode.LateUpdate)
        {
            AlignAll();
        }
    }

    private void FixedUpdate()
    {
        if (updateMode == UpdateMode.FixedUpdate)
        {
            AlignAll();
        }
    }

    private void AlignAll()
    {
        if (!Application.isPlaying && !alignInEditMode)
        {
            return;
        }

        foreach (TransformPair pair in pairs)
        {
            if (pair == null || !pair.enabled)
            {
                continue;
            }

            if (pair.source == null || pair.target == null)
            {
                continue;
            }

            AlignTransform(pair);
        }
    }

    private void AlignTransform(TransformPair pair)
    {
        Transform source = pair.source;
        Transform target = pair.target;

        if (pair.alignPosition && pair.alignRotation)
        {
            target.SetPositionAndRotation(source.position, source.rotation);
        }
        else
        {
            if (pair.alignPosition)
            {
                target.position = source.position;
            }

            if (pair.alignRotation)
            {
                target.rotation = source.rotation;
            }
        }

        if (pair.alignScale)
        {
            SetWorldScale(target, source.lossyScale);
        }
    }

    private void SetWorldScale(Transform target, Vector3 worldScale)
    {
        Transform parent = target.parent;

        if (parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;

        target.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z)
        );
    }

    private float SafeDivide(float value, float divisor)
    {
        if (Mathf.Approximately(divisor, 0f))
        {
            return 0f;
        }

        return value / divisor;
    }
}



