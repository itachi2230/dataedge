using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace backtest
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Désactiver le rendu GPU pour WPF
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            base.OnStartup(e);

            // Gérer les exceptions non gérées dans le thread principal (UI)
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            // Gérer les exceptions non gérées dans les threads en arrière-plan
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Gérer les exceptions non gérées dans les tâches (Task Parallel Library)//10h15
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Affichez l'exception
            MessageBox.Show($"Une erreur s'est produite dans l'interface utilisateur :\n{e.Exception.Message}\n\nDétails :\n{e.Exception.StackTrace}",
                            "Exception non gérée",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

            // Évitez la fermeture automatique de l'application
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Vérifiez si c'est une exception
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Une erreur critique s'est produite :\n{ex.Message}\n\nDétails :\n{ex.StackTrace}",
                                "Exception critique non gérée",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show($"Une erreur critique non gérée s'est produite.",
                                "Erreur critique",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // Affichez l'exception des tâches non observées
            MessageBox.Show($"Une erreur s'est produite dans une tâche :\n{e.Exception.Message}\n\nDétails :\n{e.Exception.StackTrace}",
                            "Exception non gérée dans une tâche",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

            // Marquez l'exception comme observée pour éviter que l'application ne se termine
            e.SetObserved();
        }
    }


}
