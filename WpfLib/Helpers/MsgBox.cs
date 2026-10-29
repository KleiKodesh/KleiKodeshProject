using System;
using System.Windows;

namespace WpfLib.Helpers
{
    public static class MsgBox
    {
        public static void Information(string message, string title = "", bool? isRtl = null)
        {
            if (isRtl ?? System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
                 MessageBox.Show(
                                   message,
                                   title ?? AppDomain.CurrentDomain.FriendlyName,
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information,
                                   MessageBoxResult.None,
                                   MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
                               );
            else
                MessageBox.Show(
                           message,
                           title ?? AppDomain.CurrentDomain.FriendlyName,
                           MessageBoxButton.OK,
                           MessageBoxImage.Information,
                           MessageBoxResult.None
                       );
        }

        public static MessageBoxResult Question (string message, string title = "", bool? isRtl = null)
        {
            if (isRtl ?? System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
                return  MessageBox.Show(
                                   message,
                                   title ?? AppDomain.CurrentDomain.FriendlyName,
                                   MessageBoxButton.YesNo,
                                   MessageBoxImage.Question,
                                   MessageBoxResult.Yes,
                                   MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
                               );
            else
                return MessageBox.Show(
                                  message,
                                  title ?? AppDomain.CurrentDomain.FriendlyName,
                                  MessageBoxButton.YesNo,
                                  MessageBoxImage.Question,
                                  MessageBoxResult.Yes
                              );
        }
    }
}
