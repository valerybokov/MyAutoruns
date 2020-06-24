using System;
using System.Windows.Media;

namespace MyAutorunsApp
{
    //https://habr.com/post/185264/
    static class AdminUtils
    {
        internal static bool IsAdminMode
        {
            get
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);

                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Запуск текущего приложения от имени администратора
        /// </summary>
        /// <returns>
        /// True - пользователь решил запустить приложение, когда показался диалог UAC.
        /// False - пользователь передумал запускать приложение от имени администратора</returns>
        internal static bool RunAsAdmin()
        {
            try
            {
                RunAsAdmin(System.Reflection.Assembly.GetExecutingAssembly().Location, null);
                return true;
            }
            catch (System.ComponentModel.Win32Exception x)
            {
                return false;
            }
        }

        internal static void RunAsAdmin(string fileName, string args)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo();

            processInfo.FileName = fileName;
            processInfo.Arguments = args;
            processInfo.UseShellExecute = true;
            processInfo.Verb = "runas";

            System.Diagnostics.Process.Start(processInfo);
        }

        internal static ImageSource AdminRightsIcon
        {
            get
            {
                var icon = System.Drawing.SystemIcons.Shield;

                var result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    new System.Windows.Int32Rect(0, 0, icon.Width, icon.Height),
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                icon.Dispose();

                return result;
            }
        }
    }
}
