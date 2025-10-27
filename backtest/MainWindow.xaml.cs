using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace backtest
{
    public partial class MainWindow : Window
    {
        private DateTime currentWeekStart;
        private readonly string notesFolderPath = Path.Combine(Environment.CurrentDirectory, "Notes");
        private ObservableCollection<Trade> Journal;
        private HabitsManager habitsManager;
        public MainWindow()
        {
            InitializeComponent();
            currentWeekStart = GetStartOfWeek(DateTime.Now);
            LoadNotesForCurrentWeek();
            LoadInvestingCalendar();
            
            loadStrategies();//charge les strategies et le journal
            habitsManager = new HabitsManager();
            DisplayHabitsInBorder();
            // LoadInvestingCalendarAsync(); // Appel de la méthode asynchrone
        }

        private void DisplayHabitsInBorder()
        {
            // Nettoyer le contenu précédent
            if (croissance.Child is StackPanel panel)
            {
                panel.Children.Clear();
            }
            else
            {
                panel = new StackPanel { Margin = new Thickness(10),CanVerticallyScroll=true };
                croissance.Child = panel;
            }

            // Ajouter un titre
            panel.Children.Add(new TextBlock
            {
                Text = "Checklist quotidienne",
                FontSize = 18,
                Foreground=Brushes.Black,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Ajouter les CheckBox pour chaque habitude
            foreach (var habitState in habitsManager.DailyHabitStates)
            {
                // Créez une StackPanel pour regrouper CheckBox et bouton de suppression
                var habitPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    CanVerticallyScroll=true,
                    Margin = new Thickness(5)
                };

                // Créez la CheckBox
                var checkBox = new CheckBox
                {
                    Content = habitState.HabitName,
                    IsChecked = habitState.IsChecked,
                    Style = (Style)FindResource("CustomCheckBoxStyle")
                };

                checkBox.Checked += (s, e) => habitsManager.UpdateHabitState(habitState.HabitName, true);
                checkBox.Unchecked += (s, e) => habitsManager.UpdateHabitState(habitState.HabitName, false);

                // Créez le bouton de suppression
                var deleteButton = new Button
                {
                    Content = "🗑️", // Icône ou texte pour le bouton
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.White,
                    HorizontalAlignment=HorizontalAlignment.Right,
                    Cursor = Cursors.Hand
                };

                // Événement de suppression
                deleteButton.Click += (s, e) =>
                {
                    // Supprimer l'habitude de la liste
                    habitsManager.RemoveHabit(habitState.HabitName);

                    // Rafraîchir l'affichage
                    RefreshHabitsDisplay();
                };

                // Ajoutez la CheckBox et le bouton dans le panel
                habitPanel.Children.Add(checkBox);
                habitPanel.Children.Add(deleteButton);

                // Ajoutez le panel à l'affichage principal
                panel.Children.Add(habitPanel);
            }



            // Ajouter un bouton pour ajouter une nouvelle habitude
            var addButton = new Button
            {
                Content = "     +      ",
                Margin = new Thickness(5),
                FontSize = 20,
                Background=Brushes.Black,
                Foreground=Brushes.White,
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            addButton.Click += AddHabit_Click;
            panel.Children.Add(addButton);

        }
        private void RefreshHabitsDisplay()
        {
            habitsManager.LoadDailyHabits(); // Recharge les habitudes
            DisplayHabitsInBorder(); // Réaffiche les habitudes
        }
       
        private void AddHabit_Click(object sender, RoutedEventArgs e)
        {
            var newHabitName = Microsoft.VisualBasic.Interaction.InputBox("Entrez le nom de l'habitude :", "Nouvelle habitude", "Nouvelle habitude");
            if (!string.IsNullOrWhiteSpace(newHabitName))
            {
                habitsManager.AddHabit(newHabitName);
                DisplayHabitsInBorder();
            }
        }
        private void TitleBar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Vérifie si l'utilisateur a double-cliqué avec le bouton gauche de la souris
           
                // Alterne entre Maximisé et Normal
                if (this.WindowState == WindowState.Normal)
                {
                    this.WindowState = WindowState.Maximized;
                }
                else if (this.WindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                }

        }


        private void LoadNotesForCurrentWeek()
        {
            Directory.CreateDirectory(notesFolderPath);
            string filePath = GetNotesFilePath(currentWeekStart);

            if (File.Exists(filePath))
            {
                TextRange textRange = new TextRange(richTextBoxNotesWeeks.Document.ContentStart, richTextBoxNotesWeeks.Document.ContentEnd);
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    textRange.Load(fs, DataFormats.Rtf);
                }
            }
            else
            {
                
                SaveNotes(); // Crée un fichier vide pour la semaine courante
            }
            UpdateWeekStartDateDisplay(); //  pour actualiser la date affichée
        }
        private void SaveNotes()
        {
            string filePath = GetNotesFilePath(currentWeekStart);
            TextRange textRange = new TextRange(richTextBoxNotesWeeks.Document.ContentStart, richTextBoxNotesWeeks.Document.ContentEnd);

            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                textRange.Save(fs, DataFormats.Rtf);
            }
        }

        private string GetNotesFilePath(DateTime weekStart)
        {
            return Path.Combine(notesFolderPath, $"Notes_{weekStart:yyyyMMdd}.rtf");
        }
        private DateTime GetStartOfWeek(DateTime date)
        {
            int daysToSubtract = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
            return date.AddDays(-daysToSubtract).Date;
        }

        // Sauvegarde lors du raccourci CTRL+S
        private void RichTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveNotes();
            }
        }

        // Sauvegarde automatique quand le RichTextBox perd le focus
        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveNotes();
        }

        // Chargement de la semaine précédente
        private void PreviousWeek_Click(object sender, RoutedEventArgs e)
        {
            richTextBoxNotesWeeks.Document.Blocks.Clear();
            currentWeekStart = currentWeekStart.AddDays(-7);
            LoadNotesForCurrentWeek();
        }

        // Chargement de la semaine suivante
        private void NextWeek_Click(object sender, RoutedEventArgs e)
        {
            richTextBoxNotesWeeks.Document.Blocks.Clear();
            currentWeekStart = currentWeekStart.AddDays(7);
            LoadNotesForCurrentWeek();
        }

        //charger les strategies
        public void loadStrategies()
        {
            var strategy= utils.getStrategies();
            StrategieListBox1.ItemsSource = strategy;
            StrategieListBox1.DisplayMemberPath = "Nom";
            //charger le journal
            Journal = new ObservableCollection<Trade>();
            foreach(Strategie st in strategy)
            {
                foreach(Trade tr in st.GetJournal())
                {
                    tr.strategie = st.Nom;
                    Journal.Add(tr);
                }
            }
            TradesDataGri.ItemsSource = Journal;
            var column = TradesDataGri.Columns.FirstOrDefault(c => c.Header.ToString() == "Date IN");
            if (column != null)
            {
                TradesDataGri.Items.SortDescriptions.Clear(); // Supprime les tris existants
                TradesDataGri.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("DateEntree",
                    System.ComponentModel.ListSortDirection.Descending));

                // Applique visuellement l'indicateur de tri à la colonne
                column.SortDirection = System.ComponentModel.ListSortDirection.Descending;
            }
            nbreText.Text = Journal.Count.ToString();
            Statistics stats = utils.CalculateStatistics(Journal);
            tauxBuy.Text = stats.SuccessRateBuy.ToString()+"%";
            tauxSell.Text = stats.SuccessRateSell.ToString() + "%";
            meilleurePaire.Text = stats.BestPair;
            PirePaire.Text = stats.WorstPair;
            perfStrat.Children.Clear();
            foreach ( var statStr in stats.StrategyPerformance)
            {
                perfStrat.Children.Add(new ControlStat(statStr.Key, statStr.Value));
            }
            
        }
        private void InitialiserCefSharp()
        {
            //var settings = new CefSettings();
            // settings.IgnoreCertificateErrors = true;
            // settings.CefCommandLineArgs.Add("disable-web-security", "1");

            //CefSharp.Cef.Initialize(settings);
        }

        private void LoadInvestingCalendar()
        {
            InvestingCalendarBrowser.Address = "https://sslecal2.investing.com?columns=exc_flags,exc_currency,exc_importance,exc_actual,exc_forecast,exc_previous&features=datepicker,timezone&countries=110,17,25,34,32,6,37,26,5,22,39,93,14,48,10,35,105,43,38,4,36,12,72&calType=week&timeZone=55&lang=5";
        }

        private void VoirDonnees_Click(object sender, RoutedEventArgs e)
        {
            // Ouvrir une fenêtre pour voir les données de backtest
            MessageBox.Show("Fenêtre pour voir les données de backtest.");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        // Mise à jour de l'affichage de la date de début de la semaine
        private void UpdateWeekStartDateDisplay()
        {
            weekStartDateText.Text = $"Semaine du {currentWeekStart:dd/MM/yy}";
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (annoncesInvesting.Visibility == Visibility.Visible)
            {
                annoncesInvesting.Visibility = Visibility.Collapsed;
            }
            else
            {
                annoncesInvesting.Visibility = Visibility.Visible;
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (croissance.Visibility == Visibility.Visible)
            {
                croissance.Visibility = Visibility.Collapsed;
            }
            else
            {
                croissance.Visibility = Visibility.Visible;
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            new addStrategieWindow().ShowDialog();
            loadStrategies();
        }
        private void ShowStatisticsControl_Click(object sender, RoutedEventArgs e)
        {
            
                // Animation de sortie glissement et fondu pour le dashboard actuel
                var slideOut = new ThicknessAnimation
                {
                    From = new Thickness(0),
                    To = new Thickness(-MainGrid.ActualWidth, 0, MainGrid.ActualWidth, 0),
                    Duration = TimeSpan.FromSeconds(0.1),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };

                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.1)
                };

                slideOut.Completed += (s, ev) =>
                {
                // Remplace le dashboard par le StatisticsControl après l'animation de sortie
                MainGrid.Children.Clear();
                    var statisticsControl = new StatisticsControl();

                // Animation d'entrée glissement et fondu pour le StatisticsControl
                statisticsControl.Margin = new Thickness(MainGrid.ActualWidth, 0, -MainGrid.ActualWidth, 0); // Position de départ hors écran
                MainGrid.Children.Add(statisticsControl);

                    var slideIn = new ThicknessAnimation
                    {
                        From = new Thickness(MainGrid.ActualWidth, 0, -MainGrid.ActualWidth, 0),
                        To = new Thickness(0),
                        Duration = TimeSpan.FromSeconds(0.4),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromSeconds(0.4)
                    };

                    statisticsControl.BeginAnimation(MarginProperty, slideIn);
                    statisticsControl.BeginAnimation(OpacityProperty, fadeIn);
                };

                // Applique les animations de sortie si le dashboard est actuellement affiché
                if (MainGrid.Children.Count > 0)
                {
                    var currentControl = MainGrid.Children[0] as UIElement;
                    currentControl?.BeginAnimation(MarginProperty, slideOut);
                    currentControl?.BeginAnimation(OpacityProperty, fadeOut);
                }
                else
                {
                    // Si aucun contrôle n'est affiché, passe directement au StatisticsControl

                    ShowStatisticsDirect();
                }
            
        }

        // Fonction pour revenir au dashboard avec la même animation
        public void ShowDashboard()
        {
            var slideOut = new ThicknessAnimation
            {
                From = new Thickness(0),
                To = new Thickness(-MainGrid.ActualWidth, 0, MainGrid.ActualWidth, 0),
                Duration = TimeSpan.FromSeconds(0.1),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.1)
            };

            slideOut.Completed += (s, ev) =>
            {
                MainGrid.Children.Clear();
                MainGrid.Children.Add(dashboard);

            // Animation d'entrée glissement et fondu pour le Dashboard
            dashboard.Margin = new Thickness(MainGrid.ActualWidth, 0, -MainGrid.ActualWidth, 0);
                var slideIn = new ThicknessAnimation
                {
                    From = new Thickness(MainGrid.ActualWidth, 0, -MainGrid.ActualWidth, 0),
                    To = new Thickness(0),
                    Duration = TimeSpan.FromSeconds(0.1),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(0.1)
                };

                dashboard.BeginAnimation(MarginProperty, slideIn);
                dashboard.BeginAnimation(OpacityProperty, fadeIn);
            };

            if (MainGrid.Children.Count > 0)
            {
                var currentControl = MainGrid.Children[0] as UIElement;
                currentControl?.BeginAnimation(MarginProperty, slideOut);
                currentControl?.BeginAnimation(OpacityProperty, fadeOut);
            }
        }
        // Fonction directe si le dashboard est masqué et qu'on veut montrer les statistiques
        private void ShowStatisticsDirect(object sender=null, MouseButtonEventArgs e=null)
        {
            if (StrategieListBox1.SelectedItem != null)
            {
                var statisticsControl = new StatisticsControl((Strategie)StrategieListBox1.SelectedItem);
                MainGrid.Children.Clear();
                MainGrid.Children.Add(statisticsControl);

                // Animation de fondu entrant pour StatisticsControl
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
                statisticsControl.BeginAnimation(OpacityProperty, fadeIn);
            }
        }


        private void StrategieListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ShowStatisticsControl_Click(object sender, MouseButtonEventArgs e)
        {

        }
        private void AddTradeButton_Click(object sender, RoutedEventArgs e)
        {
            // Récupérer les stratégies
            var strategies = utils.getStrategies();

            // Peupler la ListBox dans le Popup
            StrategyListBox.ItemsSource = strategies;
            StrategyListBox.DisplayMemberPath = "Nom"; // Affiche le nom des stratégies

            // Afficher le Popup
            StrategyPopup.IsOpen = true;
        }

        private void StrategyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StrategyListBox.SelectedItem is Strategie selectedStrategy)
            {
                // Ouvrir la fenêtre d'ajout de trade
                new AjoutTrade(selectedStrategy, true).ShowDialog();

                // Réinitialiser la sélection pour éviter des actions répétées
                StrategyListBox.SelectedItem = null;

                // Fermer le Popup
                StrategyPopup.IsOpen = false;
                loadStrategies();
            }
        }

        private void TradesDataGri_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {

            if (TradesDataGri.SelectedItem is Trade selectedTrade)
            {
                VisuelTrade visuelTradeWindow = new VisuelTrade(selectedTrade);
                visuelTradeWindow.Show();
            }

        }

        private void StrategieListBox1_KeyDown(object sender, KeyEventArgs e)
        {
            // Vérifie si la touche appuyée est "Delete"
            if (e.Key == Key.Delete)
            {
                // Vérifie si un élément est sélectionné et qu'il s'agit d'une stratégie
                if (StrategieListBox1.SelectedItem is Strategie str)
                {
                    // Confirmer la suppression avec l'utilisateur
                    MessageBoxResult result = MessageBox.Show(
                        $"Voulez-vous vraiment supprimer la stratégie '{str.Nom}' ?",
                        "Confirmation de suppression",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        // Appeler la méthode de suppression
                        str.SupprimerStrategie();
                        loadStrategies();
                        // Supprimer la stratégie de l'interface utilisateur
                       // ((ObservableCollection<Strategie>)StrategieListBox1.ItemsSource)?.Remove(str);
                    }
                }
            }
        }
        private void TradesDataGri_KeyUp(object sender, KeyEventArgs e)
        {
            // Vérifie si la touche appuyée est "Delete"
            if (e.Key == Key.Delete)
            {
                // Vérifie si un élément est sélectionné et qu'il s'agit d'un trade
                if (TradesDataGri.SelectedItem is Trade trade)
                {
                    // Trouve la stratégie correspondante en utilisant le nom de la stratégie du trade
                    Strategie strategieAssociee = null;

                    foreach (Strategie strategie in StrategieListBox1.Items)
                    {
                        if (strategie.Nom == trade.strategie)
                        {
                            strategieAssociee = strategie;
                            break;
                        }
                    }

                    // Vérifie si une stratégie correspondante a été trouvée
                    if (strategieAssociee != null)
                    {
                        // Affiche une boîte de confirmation
                        MessageBoxResult result = MessageBox.Show(
                            $"Êtes-vous sûr de vouloir supprimer le trade avec l'ID {trade.Id} de la stratégie '{trade.strategie}' ?",
                            "Confirmation de suppression",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );

                        // Vérifie si l'utilisateur a confirmé la suppression
                        if (result == MessageBoxResult.Yes)
                        {
                            // Supprime le trade de la stratégie associée
                            strategieAssociee.RemoveJournalById(trade.Id);

                            loadStrategies();
                        }
                    }
                    else
                    {
                        // Si aucune stratégie correspondante n'est trouvée, affiche un message d'erreur
                        MessageBox.Show(
                            "Impossible de trouver la stratégie associée au trade sélectionné.",
                            "Erreur",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
            }


        }

        private void StrategieListBox1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if(StrategieListBox1.SelectedItem is Strategie st)
            {
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), st.filePath);
                // Vérifiez si le fichier existe avant d'essayer de l'ouvrir

                if (!string.IsNullOrEmpty(st.filePath) && File.Exists(fullPath))
                {
                    try
                    {
                        // Lancer le fichier avec l'application par défaut (Excel dans ce cas)
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = fullPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        // Gérez les exceptions si le fichier ne peut pas être ouvert
                        MessageBox.Show($"Une erreur est survenue lors de l'ouverture du fichier : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Le fichier spécifié n'existe pas ou le chemin est vide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void TradesDataGri_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Vérifie si un élément est sélectionné et qu'il s'agit d'un trade
            if (TradesDataGri.SelectedItem is Trade trade)
            {
                // Trouve la stratégie correspondante en utilisant le nom de la stratégie du trade
                Strategie st = null;

                foreach (Strategie strategie in StrategieListBox1.Items)
                {
                    if (strategie.Nom == trade.strategie)
                    {
                        st = strategie;
                        break;
                    }
                }

                // Vérifie si une stratégie correspondante a été trouvée
                if (st != null)
                {
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), st.journalFilePath);
                    if (!string.IsNullOrEmpty(st.journalFilePath) && File.Exists(fullPath))
                    {
                        try
                        {
                            // Lancer le fichier avec l'application par défaut (Excel dans ce cas)
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = fullPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            // Gérez les exceptions si le fichier ne peut pas être ouvert
                            MessageBox.Show($"Une erreur est survenue lors de l'ouverture du fichier : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Le fichier spécifié n'existe pas ou le chemin est vide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // Si aucune stratégie correspondante n'est trouvée, affiche un message d'erreur
                    MessageBox.Show(
                        "Impossible de trouver la stratégie associée au trade sélectionné.",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }
    }
}
