using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Copies the world pose, and optionally the scale, of source transforms to target transforms.
/// </summary>
[ExecuteAlways]
public class TransformAlignment : MonoBehaviour
{
    // In Edit Mode, differences below this value count as rounding errors and are not written.
    private const float EditModeTolerance = 0.00001f;

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

    /// <summary>
    /// Copies all poses now instead of in the next update.
    /// </summary>
    public void AlignNow() => AlignAll();

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

        // In Edit Mode, write only real changes, so that an aligned scene stays unmodified.
        bool writeAlways = Application.isPlaying;
        Vector3 position = source.position;
        Quaternion rotation = source.rotation;

        bool writePosition = pair.alignPosition && (writeAlways || !IsSame(target.position, position));
        bool writeRotation = pair.alignRotation && (writeAlways || !IsSame(target.rotation, rotation));

        if (writePosition && writeRotation)
        {
            target.SetPositionAndRotation(position, rotation);
        }
        else if (writePosition)
        {
            target.position = position;
        }
        else if (writeRotation)
        {
            target.rotation = rotation;
        }

        if (pair.alignScale)
        {
            Vector3 scale = GetLocalScale(target, source.lossyScale);

            if (writeAlways || !IsSame(target.localScale, scale))
            {
                target.localScale = scale;
            }
        }
    }

    private Vector3 GetLocalScale(Transform target, Vector3 worldScale)
    {
        Transform parent = target.parent;

        if (parent == null)
        {
            return worldScale;
        }

        Vector3 parentScale = parent.lossyScale;

        return new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z)
        );
    }

    private static bool IsSame(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < EditModeTolerance &&
               Mathf.Abs(a.y - b.y) < EditModeTolerance &&
               Mathf.Abs(a.z - b.z) < EditModeTolerance;
    }

    private static bool IsSame(Quaternion a, Quaternion b)
    {
        // q and -q are the same rotation
        float sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;

        return Mathf.Abs(a.x - sign * b.x) < EditModeTolerance &&
               Mathf.Abs(a.y - sign * b.y) < EditModeTolerance &&
               Mathf.Abs(a.z - sign * b.z) < EditModeTolerance &&
               Mathf.Abs(a.w - sign * b.w) < EditModeTolerance;
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
