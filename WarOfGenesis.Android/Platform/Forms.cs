// WinForms(윈도우 전용)의 아주 작은 대용품 — 원본(duel-dx/System/Part1TitleScreen.cs 의 「PART 2 연결」 따위)이 쓰는 것만.
// MessageBox 는 PlatformHost.Dialog(안드로이드: 대화 상자, 시험기: 첫 단추)로 띄우고 답을 기다린다. 폴더 고르기 창은 앱의 고르기 화면이 대신한다.
#nullable enable
using DuelDx.Native;

namespace System.Windows.Forms
{
    public enum DialogResult { None = 0, OK = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7 }
    public enum MessageBoxButtons { OK = 0, OKCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3, YesNo = 4, RetryCancel = 5 }
    public enum MessageBoxIcon { None = 0, Error = 16, Question = 32, Warning = 48, Information = 64 }

    public interface IWin32Window { IntPtr Handle { get; } }

    public class NativeWindow : IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public void AssignHandle(IntPtr handle) => Handle = handle;
        public void ReleaseHandle() => Handle = IntPtr.Zero;
    }

    public static class Application
    {
        public static void EnableVisualStyles() { }
    }

    public sealed class Cursor
    {
        public static Cursor? Current { get; set; }
    }

    public static class Cursors
    {
        public static Cursor WaitCursor { get; } = new();
        public static Cursor Default { get; } = new();
    }

    public static class MessageBox
    {
        public static DialogResult Show(string text) => Show(null, text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(null, text, caption, buttons, icon);
        public static DialogResult Show(IWin32Window? owner, string text, string caption) => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons) => Show(owner, text, caption, buttons, MessageBoxIcon.None);

        public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            (string Name, DialogResult Result)[] choices = buttons switch
            {
                MessageBoxButtons.OKCancel => [("확인", DialogResult.OK), ("취소", DialogResult.Cancel)],
                MessageBoxButtons.YesNo => [("예", DialogResult.Yes), ("아니요", DialogResult.No)],
                MessageBoxButtons.YesNoCancel => [("예", DialogResult.Yes), ("아니요", DialogResult.No), ("취소", DialogResult.Cancel)],
                MessageBoxButtons.RetryCancel => [("다시 시도", DialogResult.Retry), ("취소", DialogResult.Cancel)],
                MessageBoxButtons.AbortRetryIgnore => [("중단", DialogResult.Abort), ("다시 시도", DialogResult.Retry), ("무시", DialogResult.Ignore)],
                _ => [("확인", DialogResult.OK)],
            };
            int picked = PlatformHost.Dialog?.Invoke(text, caption, [.. choices.Select(c => c.Name)]) ?? 0;
            if (picked < 0) return buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Cancel;
            return choices[Math.Clamp(picked, 0, choices.Length - 1)].Result;
        }
    }

    /// <summary>폴더 고르기 창 — 안드로이드는 앱 첫 화면에서 고른다. 늘 「취소」.</summary>
    public sealed class FolderBrowserDialog : IDisposable
    {
        public string Description { get; set; } = "";
        public string SelectedPath { get; set; } = "";
        public bool ShowNewFolderButton { get; set; }
        public bool UseDescriptionForTitle { get; set; }

        public DialogResult ShowDialog(IWin32Window? owner = null)
        {
            MessageBox.Show(owner, "원본 폴더는 앱 첫 화면(게임을 끝내고 다시 켜기)의 「폴더 고르기」에서 고릅니다.", "창세기전3");
            return DialogResult.Cancel;
        }

        public void Dispose() { }
    }
}

namespace System.Windows.Forms
{
    public enum TaskDialogIcon { None = 0, Information, Warning, Error, ShieldBlueBar }

    /// <summary>작업 대화 상자 단추 — 누르면 Click, AllowCloseDialog 가 거짓이면 창을 다시 띄운다.</summary>
    public sealed class TaskDialogButton(string text)
    {
        public string Text { get; } = text;
        public bool AllowCloseDialog { get; set; } = true;
        public event EventHandler? Click;
        internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
        public static TaskDialogButton Cancel { get; } = new("취소");
        public static TaskDialogButton OK { get; } = new("확인");
        public static TaskDialogButton Yes { get; } = new("예");
        public static TaskDialogButton No { get; } = new("아니요");
    }

    public sealed class TaskDialogPage
    {
        public string? Caption { get; set; }
        public string? Heading { get; set; }
        public string? Text { get; set; }
        public TaskDialogIcon Icon { get; set; }
        public List<TaskDialogButton> Buttons { get; } = [];
    }

    public static class TaskDialog
    {
        public static TaskDialogButton ShowDialog(IntPtr owner, TaskDialogPage page)
        {
            while (true)
            {
                string text = string.Join("\n\n", new[] { page.Heading, page.Text }.Where(s => !string.IsNullOrEmpty(s)));
                int picked = DuelDx.Native.PlatformHost.Dialog?.Invoke(text, page.Caption ?? "", [.. page.Buttons.Select(b => b.Text)]) ?? 0;
                var button = picked >= 0 && picked < page.Buttons.Count ? page.Buttons[picked] : TaskDialogButton.Cancel;
                button.RaiseClick();
                if (button.AllowCloseDialog) return button;
            }
        }
    }
}
