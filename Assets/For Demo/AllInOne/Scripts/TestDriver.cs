#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Runs an automated test late in every frame, when all poses of the frame are final. Nested routines are supported.
    /// An exception ends the innermost routine that was started as a test.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class TestDriver : MonoBehaviour
    {
        /// <summary>A routine whose exceptions are caught.</summary>
        public sealed class Guarded
        {
            public IEnumerator routine;
            public Action<Exception> onError;
        }

        private struct Level
        {
            public IEnumerator routine;
            public Action<Exception> onError;
        }

        private readonly Stack<Level> levels = new Stack<Level>();

        public bool IsDone => levels.Count == 0;

        public static TestDriver Run(IEnumerator routine)
        {
            var driver = new GameObject("Test Driver").AddComponent<TestDriver>();
            DontDestroyOnLoad(driver.gameObject);
            driver.levels.Push(new Level { routine = routine });
            return driver;
        }

        private void LateUpdate()
        {
            // Routines that start or end do not cost a frame.
            while (levels.Count > 0)
            {
                object current;
                try
                {
                    if (!levels.Peek().routine.MoveNext())
                    {
                        levels.Pop();
                        continue;
                    }
                    current = levels.Peek().routine.Current;
                }
                catch (Exception e)
                {
                    Unwind(e);
                    continue;
                }

                if (current is Guarded guarded) levels.Push(new Level { routine = guarded.routine, onError = guarded.onError });
                else if (current is IEnumerator nested) levels.Push(new Level { routine = nested });
                else return;
            }
        }

        private void Unwind(Exception e)
        {
            while (levels.Count > 0)
            {
                Level level = levels.Pop();
                if (level.onError == null) continue;
                level.onError(e);
                return;
            }
            Debug.LogException(e, this);
        }
    }
}
#endif