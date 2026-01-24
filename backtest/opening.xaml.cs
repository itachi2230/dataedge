using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using System.IO;

namespace backtest
{
    public partial class opening : Window
    {
        public opening()
        {
            InitializeComponent();

            // On lance l'enregistrement en arrière-plan sans bloquer l'UI
            _ = RegisterMachine();

            // Initialisation des dossiers de base pour le nouveau système JSON
            InitFolders();

            // Timer pour fermer la fenêtre et lancer l'application principale
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Augmenté à 2s pour laisser le temps au JSON de s'initialiser si besoin
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                done();
            };
            timer.Start();
        }

        private void InitFolders()
        {
            try
            {
                // On s'assure que les dossiers existent dès le démarrage
                if (!Directory.Exists("data")) Directory.CreateDirectory("data");
                if (!Directory.Exists("metadata")) Directory.CreateDirectory("metadata");
            }
            catch { }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void done()
        {
            if (FirstLaunchManager.IsFirstLaunch())
            {
                Demo demoWindow = new Demo();
                demoWindow.Show();
                this.Close();
            }
            else
            {
                // La MainWindow va maintenant charger les fichiers .json au lieu des .xlsx
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                Application.Current.MainWindow = mainWindow;
                this.Close();
            }
        }

        static async Task RegisterMachine()
        {
            // URL de ton API
            string apiUrl = "http://fxdataedge.com/public/index.php/api/register-user";

            string machineName = Environment.MachineName;
            string username = Environment.UserName;

            var data = new
            {
                machine_name = machineName,
                username = username
            };

            using (HttpClient client = new HttpClient())
            {
                // On définit un timeout court pour ne pas bloquer l'app si le serveur est down
                client.Timeout = TimeSpan.FromSeconds(5);

                try
                {
                    string json = JsonConvert.SerializeObject(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await client.PostAsync(apiUrl, content);
                }
                catch
                {
                    // On ignore silencieusement les erreurs réseau au démarrage
                }
            }
        }
    }
}