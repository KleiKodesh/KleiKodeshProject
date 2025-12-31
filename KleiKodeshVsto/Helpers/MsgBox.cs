using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public static class MsgBox
    {
        const MessageBoxOptions RtlOptions = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

        public static void Info(string message, string title = null)
        {
            MessageBox.Show(
                message,
                title ?? AppDomain.CurrentDomain.FriendlyName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                RtlOptions
            );
        }

        public static bool Question(string message, string title = null)
        {
            var result = MessageBox.Show(
                message,
                title ?? AppDomain.CurrentDomain.FriendlyName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1,
                RtlOptions
            );

            return result == DialogResult.Yes;
        }

        public static void Error(string message, string title = null)
        {
            MessageBox.Show(
                message,
                title ?? AppDomain.CurrentDomain.FriendlyName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                RtlOptions
            );
        }
    }
}
