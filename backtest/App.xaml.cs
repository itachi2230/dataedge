using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using backtest.Services; // Assure-toi que le namespace est correct

namespace backtest
{
    public partial class App : Application
    {
        // On crée une instance rapide pour les rapports de crash 
        // au cas où l'injection de dépendance n'est pas encore prête
        private readonly FxCloudService _crashReporter = new FxCloudService();

        protected override void OnStartup(StartupEventArgs e)
        {
            // Rendu Software pour éviter les crashs liés aux drivers GPU (courant en trading)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            base.OnStartup(e);

            // 1. Thread UI
            // this.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            // 2. Threads en arrière-plan
            //AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 3. Tasks (TPL)
            //TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private async void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true; // Empêche le crash brutal
            await HandleFatalError(e.Exception, "Interface Utilisateur (UI)");
        }

        private async void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                await HandleFatalError(ex, "Domaine Applicatif (Critique)");
            }
        }

        private async void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // Empêche la fermeture
            await HandleFatalError(e.Exception, "Tâche Asynchrone (Task)");
        }

        /// <summary>
        /// Centralise l'envoi du rapport au serveur Symfony et informe l'utilisateur
        /// </summary>
        private async Task HandleFatalError(Exception ex, string context)
        {
            // On extrait la source réelle (le nom de l'erreur + le message)
            string errorHeader = $"[{ex.GetType().Name}] {ex.Message}";

            // On nettoie le StackTrace pour ne garder que ce qui concerne TON code (pas le système Windows)
            var lines = ex.StackTrace?.Split('\n')
                          .Where(line => line.Contains("backtest")) // Remplace par ton namespace si différent
                          .Select(line => line.Trim())
                          .ToList() ?? new System.Collections.Generic.List<string>();

            string cleanStack = lines.Count > 0 ? string.Join("\n   -> ", lines) : "Pas de détails locaux.";

            string finalReport = $"{errorHeader}\nCONTEXTE: {context}\nTRACE:\n   -> {cleanStack}";

            // Envoi asynchrone sans bloquer
            _ = _crashReporter.SendCrashReportAsync(finalReport);

            // Affichage utilisateur simplifié
            MessageBox.Show(
                $"Une anomalie a été détectée ({ex.GetType().Name}).\n\n" +
                "Le rapport technique a été envoyé au support pour analyse.",
                "DataEdge Support", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}