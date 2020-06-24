using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutorunInfo;

namespace MyAutorunsApp
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AutorunViewer autoruns_;
        private readonly System.Collections.ObjectModel.ObservableCollection<Info> list_;
        private System.Threading.CancellationTokenSource ctSource_;

        public MainWindow()
        {
            InitializeComponent();

            list_ = new System.Collections.ObjectModel.ObservableCollection<Info>();
            autoruns_ = new AutorunViewer();
            ctSource_ = new System.Threading.CancellationTokenSource();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            imgForMenuItemAdmin.Source = AdminUtils.AdminRightsIcon;

            if (AdminUtils.IsAdminMode)
            {
                Title = "MyAutoruns (administrator)";
                miRunAsAdmin.IsEnabled = false;
            }                

            lw.ItemsSource = list_;            

            Task.Run(() => {
                Action<Delegate, object[]> invoker = (dlgt, args) => Dispatcher.Invoke(dlgt, args);
                System.Threading.CancellationToken ctoken = ctSource_.Token;

                var startTime = DateTime.Now;
                
                autoruns_.BeginInitialize();
                autoruns_.GetTaskSchedulerInfo(list_, invoker);

                if (ctoken.IsCancellationRequested)
                    return;

                autoruns_.GetStartupInfo(list_, invoker);

                if (ctoken.IsCancellationRequested)
                    return;

                autoruns_.GetRegistryHKCUInfo(list_, invoker);

                if (ctoken.IsCancellationRequested)
                    return;

                autoruns_.GetRegistryHKLMInfo(list_, invoker);

                if (ctoken.IsCancellationRequested)
                    return;

                autoruns_.GetServicesInfo(list_, invoker);

                autoruns_.EndInitialize();

                var endTime = DateTime.Now;
                var rez = endTime - startTime;
                Dispatcher.Invoke(() => {
                    if (miRunAsAdmin.IsEnabled)
                        Title = "MyAutoruns. Total - " + list_.Count + " Time - " + rez;
                    else
                        Title = "MyAutoruns (administrator). Total - " + list_.Count;
                });
          });
        }

        private void ListView_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var it = (Info)((ListViewItem)e.Source).Content;

            if (it.Directory != null && it.Directory.Length > 0)
            {
                var path = System.IO.Path.Combine(it.Directory, it.FileName);

                if (System.IO.File.Exists(path))
                   System.Diagnostics.Process.Start("explorer.exe", "/select, " + path);
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender == miExit)
                Close();
            else
            //miRunAsAdmin
            if (AdminUtils.RunAsAdmin())
            {
                ctSource_.Cancel();
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ctSource_.Cancel();
            ctSource_.Dispose();

            autoruns_.Dispose();
        }
    }
}
