using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>Pose of a head and two hands relative to a tracking space.</summary>
    public struct MotionPose
    {
        public Vector3 head, left, right;
        public Quaternion headRotation, leftRotation, rightRotation;

        public static MotionPose Lerp(in MotionPose a, in MotionPose b, float t)
        {
            return new MotionPose
            {
                head = Vector3.Lerp(a.head, b.head, t),
                left = Vector3.Lerp(a.left, b.left, t),
                right = Vector3.Lerp(a.right, b.right, t),
                headRotation = Quaternion.Slerp(a.headRotation, b.headRotation, t),
                leftRotation = Quaternion.Slerp(a.leftRotation, b.leftRotation, t),
                rightRotation = Quaternion.Slerp(a.rightRotation, b.rightRotation, t)
            };
        }
    }

    /// <summary>
    /// A recorded movement of head and hands, read from a CSV file with 22 columns: time, then position and rotation of
    /// the head, the left hand, and the right hand. Also knows in which seconds the hands rest.
    /// </summary>
    public sealed class MotionRecording
    {
        private const int Columns = 22;
        private const float RestingTravel = 0.03f;

        private readonly List<float> times = new List<float>();
        private readonly List<MotionPose> poses = new List<MotionPose>();

        // The distance that both hands travel together in each second
        private float[] handTravel = Array.Empty<float>();

        private MotionRecording() { }

        public int Count => times.Count;

        public float Duration => times.Count > 0 ? times[times.Count - 1] : 0f;

        /// <summary>Reads a file. Uses no Unity objects, so it can run on another thread.</summary>
        public static MotionRecording Parse(string text)
        {
            var recording = new MotionRecording();
            var values = new float[Columns];
            ReadOnlySpan<char> rest = text.AsSpan();
            NextLine(ref rest); // header
            while (!rest.IsEmpty)
            {
                if (ReadValues(NextLine(ref rest), values)) recording.Add(values);
            }
            recording.MeasureHandTravel();
            return recording;
        }

        /// <summary>The interpolated pose at a time. The cursor is the row found by the previous call.</summary>
        public MotionPose At(float time, ref int cursor)
        {
            int last = times.Count - 1;
            cursor = Mathf.Clamp(cursor, 0, Mathf.Max(0, last - 1));
            while (cursor > 0 && times[cursor] > time) cursor--;
            while (cursor < last - 1 && times[cursor + 1] < time) cursor++;
            if (last < 1) return poses[0];

            float span = times[cursor + 1] - times[cursor];
            float t = span > 1e-6f ? Mathf.Clamp01((time - times[cursor]) / span) : 0f;
            return MotionPose.Lerp(poses[cursor], poses[cursor + 1], t);
        }

        /// <summary>The first second from the given one in which the hands travel at least this far.</summary>
        public int NextActiveSecond(int second, float travel)
        {
            for (int s = Mathf.Max(0, second); s < handTravel.Length; s++)
                if (handTravel[s] > travel) return s;
            return Mathf.Max(0, second);
        }

        /// <summary>How many seconds in a row the hands rest, counted from the given one.</summary>
        public int RestingSeconds(int second)
        {
            int count = 0;
            while (second + count < handTravel.Length && handTravel[second + count] < RestingTravel) count++;
            return count;
        }

        private void Add(float[] v)
        {
            times.Add(v[0]);
            poses.Add(new MotionPose
            {
                head = new Vector3(v[1], v[2], v[3]),
                headRotation = new Quaternion(v[4], v[5], v[6], v[7]).normalized,
                left = new Vector3(v[8], v[9], v[10]),
                leftRotation = new Quaternion(v[11], v[12], v[13], v[14]).normalized,
                right = new Vector3(v[15], v[16], v[17]),
                rightRotation = new Quaternion(v[18], v[19], v[20], v[21]).normalized
            });
        }

        private void MeasureHandTravel()
        {
            handTravel = new float[Mathf.CeilToInt(Duration)];
            for (int i = 1; i < poses.Count; i++)
            {
                int second = (int)times[i - 1];
                if (second >= handTravel.Length) break;
                handTravel[second] += Vector3.Distance(poses[i - 1].left, poses[i].left) + Vector3.Distance(poses[i - 1].right, poses[i].right);
            }
        }

        private static ReadOnlySpan<char> NextLine(ref ReadOnlySpan<char> rest)
        {
            int end = rest.IndexOf('\n');
            ReadOnlySpan<char> line = end < 0 ? rest : rest.Slice(0, end);
            rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end + 1);
            return line.TrimEnd();
        }

        private static bool ReadValues(ReadOnlySpan<char> line, float[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                int comma = line.IndexOf(',');
                ReadOnlySpan<char> cell = comma < 0 ? line : line.Slice(0, comma);
                if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
                if (comma < 0) return i == values.Length - 1;
                line = line.Slice(comma + 1);
            }
            return true;
        }
    }
}