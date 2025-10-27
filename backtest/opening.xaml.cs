using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
namespace backtest
{
    public partial class opening : Window
    {
        public opening()
        {
           
            InitializeComponent();
            RegisterMachine();
            // Timer pour fermer la fenêtre et lancer l'application principale
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                done();
            };
            timer.Start();
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
                this.Close(); // Ferme la fenêtre de démarrage
            }
            else
            {
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                Application.Current.MainWindow = mainWindow;
                this.Close();
            }
        }
        static async Task RegisterMachine()
        {
            string apiUrl = "http://fxdataedge.com/public/index.php/api/register-user";
            //string apiUrl = "http://localhost:8080/api/register-user";
            string machineName = Environment.MachineName; // Nom de la machine
            string username = Environment.UserName; // Nom d'utilisateur Windows

            var data = new
            {
                machine_name = machineName,
                username = username
            };

            using (HttpClient client = new HttpClient())
            {
                string json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    await client.PostAsync(apiUrl, content);
                }
                catch (Exception ex)
                {
                }
            }
        }

    }
}
