using System.Collections.Generic;
using DemoKit;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// The small panels in front of the user: key help, problem, leave, and finished. Only one is shown at a time, the
    /// others wait.
    /// </summary>
    public sealed class Dialogs
    {
        private static readonly Vector3 Place = new Vector3(0f, 1.48f, -0.10f);
        private const float PanelScale = 0.68f;
        private const float Width = 820f;
        private const int Order = 10;

        private readonly AllInOneController app;
        private readonly List<DemoPanel> waiting = new List<DemoPanel>();
        private readonly DemoPanel keyHelp, problem, confirm, finished;
        private DemoLabel problemTitle, problemText;
        private DemoPanel current;

        public Dialogs(AllInOneController app)
        {
            this.app = app;
            keyHelp = Prepare(BuildKeyHelp());
            problem = Prepare(BuildProblem());
            confirm = Prepare(BuildConfirm());
            finished = Prepare(BuildFinished());
        }

        public DemoPanel KeyHelp => keyHelp;
        public DemoPanel Problem => problem;
        public DemoPanel Confirm => confirm;
        public DemoPanel Finished => finished;

        /// <summary>The dialog on screen, or null.</summary>
        public DemoPanel Current => current;

        public void OpenKeyHelp() => Open(keyHelp);

        public void OpenConfirm() => Open(confirm);

        public void OpenFinished() => Open(finished);

        public void OpenProblem(RealtimeProblem what, string detail)
        {
            problemTitle.Text = AgentDemoTexts.ProblemTitle(what);
            problemText.Text = AgentDemoTexts.ProblemText(what, app.Conversation.Model, detail);
            problem.Layout();
            Open(problem);
        }

        /// <summary>Closes a dialog and shows the next one that waits.</summary>
        public void Close(DemoPanel dialog)
        {
            waiting.Remove(dialog);
            if (current != dialog) return;

            current = null;
            dialog.Hide();
            if (waiting.Count > 0)
            {
                DemoPanel next = waiting[0];
                waiting.RemoveAt(0);
                Open(next);
            }
            app.HandleDialogChanged();
        }

        public void CloseAll()
        {
            waiting.Clear();
            if (current != null) Close(current);
        }

        private void Open(DemoPanel dialog)
        {
            if (current == dialog) return;
            if (current != null)
            {
                if (!waiting.Contains(dialog)) waiting.Add(dialog);
                return;
            }
            current = dialog;
            dialog.ShowFacing(Place, app.HeadHome);
            app.HandleDialogChanged();
        }

        private static DemoPanel Prepare(DemoPanel panel) => panel.Scale(PanelScale).OnTop(Order).Hide(true);

        private DemoPanel BuildKeyHelp()
        {
            DemoBuilder builder = DemoUi.Panel("keyhelp", Width)
                .Title(AgentDemoTexts.KeyHelpTitle)
                .Text(AgentDemoTexts.KeyHelp());
            string detail = AgentDemoTexts.KeyHelpDetail();
            if (detail.Length > 0) builder.Caption(detail);
            return builder
                .Note(AgentDemoTexts.KeyWarning, DemoIcon.Warning, DemoTone.Warning)
                .Row(r => r
                    .Button("close", "Close", () => Close(keyHelp), DemoStyle.Quiet)
                    .Flex()
                    .Primary("check", "Check again", CheckKey))
                .Build();
        }

        private void CheckKey()
        {
            app.CheckKey();
            if (app.Conversation.HasKey) Close(keyHelp);
        }

        private DemoPanel BuildProblem()
        {
            return DemoUi.Panel("problem", Width)
                .Label("problem-title", "Problem", DemoTextStyle.Title).Out(out problemTitle)
                .Label("problem-text", "Text").Out(out problemText)
                .Button("continue", "Continue without voice", app.ContinueWithoutVoice)
                .Row(r => r
                    .Button("menu", "Back to menu", app.LeaveAtOnce, DemoStyle.Quiet)
                    .Flex()
                    .Primary("retry", "Try again", app.RetryConversation))
                .Build();
        }

        private DemoPanel BuildConfirm()
        {
            return DemoUi.Panel("confirm", Width)
                .Title(AgentDemoTexts.LeaveTitle, AgentDemoTexts.LeaveText)
                .Row(r => r
                    .Flex()
                    .Button("leave", "Leave", app.LeaveAtOnce)
                    .Primary("stay", "Stay", app.StayInSession))
                .Build();
        }

        private DemoPanel BuildFinished()
        {
            return DemoUi.Panel("finished", Width)
                .Title(AgentDemoTexts.FinishedTitle, AgentDemoTexts.FinishedText)
                .Row(r => r
                    .Button("again", "Repeat the session", app.RestartSession)
                    .Flex()
                    .Primary("menu", "Back to menu", app.LeaveAtOnce, DemoIcon.Home))
                .Build();
        }
    }
}