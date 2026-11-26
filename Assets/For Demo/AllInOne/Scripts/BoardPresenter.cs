using TMPro;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Shows the boards of the study in their original places. Works on its own copies, on which typing errors of the
    /// original texts are corrected.
    /// </summary>
    public sealed class BoardPresenter : MonoBehaviour
    {
        private static readonly Vector3 BoardPosition = new Vector3(0f, 1.65f, 0f);
        private static readonly Quaternion BoardRotation = Quaternion.Euler(0f, 180f, 0f);
        private static readonly Vector3 BoardScale = new Vector3(0.23f, 0.23f, 1f);
        private static readonly Vector3 ReminderPosition = new Vector3(0f, 2.608f, -2.440f);
        private static readonly Quaternion ReminderRotation = Quaternion.Euler(-6f, 180f, 0f);
        private static readonly Vector3 ReminderScale = new Vector3(0.6f, 0.6f, 1f);

        private static readonly (string wrong, string right)[] Corrections =
        {
            ("Instrcution", "Instruction"),
            ("interupt the other ", "interrupt the other"),
            ("interupt", "interrupt"),
            ("Tiktok", "TikTok")
        };

        [SerializeField] private Transform parent;

        private GameObject warmUp, instruction;
        private GameObject[] reminders = System.Array.Empty<GameObject>();
        private AgentSessionScript.Board current;
        private int currentReminder = -1;
        private bool isHidden;

        /// <summary>The board in front of the user, or null.</summary>
        public GameObject Shown =>
            isHidden ? null : current == AgentSessionScript.Board.WarmUp ? warmUp : current == AgentSessionScript.Board.Instruction ? instruction : null;

        /// <summary>The reminder on the wall, or -1.</summary>
        public int ShownReminder => currentReminder;

        public AgentSessionScript.Board Current => current;

        /// <summary>Hides the board in front of the user while a dialog is open.</summary>
        public bool Hidden
        {
            get { return isHidden; }
            set
            {
                isHidden = value;
                Apply();
            }
        }

        /// <summary>Makes the boards of a topic.</summary>
        public void Prepare(AgentTopic topic, AgentSessionScript script)
        {
            Clear();
            warmUp = Create(topic.warmUpBoard, BoardPosition, BoardRotation, BoardScale);
            instruction = Create(topic.instructionBoard, BoardPosition, BoardRotation, BoardScale);
            reminders = new GameObject[script.reminderBoards.Length];
            for (int i = 0; i < reminders.Length; i++)
                reminders[i] = Create(script.reminderBoards[i], ReminderPosition, ReminderRotation, ReminderScale);
            Apply();
        }

        public void Show(AgentSessionScript.Board board)
        {
            current = board;
            Apply();
        }

        /// <summary>Shows a reminder, or none with -1.</summary>
        public void ShowReminder(int index)
        {
            currentReminder = index;
            Apply();
        }

        /// <summary>Removes all boards.</summary>
        public void Clear()
        {
            current = AgentSessionScript.Board.None;
            currentReminder = -1;
            isHidden = false;
            Remove(warmUp);
            Remove(instruction);
            foreach (GameObject reminder in reminders) Remove(reminder);
            warmUp = instruction = null;
            reminders = System.Array.Empty<GameObject>();
        }

        private void Apply()
        {
            if (warmUp != null) warmUp.SetActive(!isHidden && current == AgentSessionScript.Board.WarmUp);
            if (instruction != null) instruction.SetActive(!isHidden && current == AgentSessionScript.Board.Instruction);
            for (int i = 0; i < reminders.Length; i++)
                if (reminders[i] != null) reminders[i].SetActive(i == currentReminder);
        }

        private GameObject Create(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (prefab == null) return null;
            GameObject board = Instantiate(prefab, parent);
            board.name = prefab.name;
            board.transform.SetPositionAndRotation(position, rotation);
            board.transform.localScale = scale;
            foreach (TMP_Text text in board.GetComponentsInChildren<TMP_Text>(true)) text.text = Correct(text.text);
            return board;
        }

        private static string Correct(string text)
        {
            foreach ((string wrong, string right) in Corrections) text = text.Replace(wrong, right);
            return text;
        }

        private static void Remove(GameObject board)
        {
            if (board == null) return;
            board.SetActive(false);
            Destroy(board);
        }
    }
}