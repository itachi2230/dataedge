import sys

# --- Fix MainWindow.xaml.cs ---
path = r'c:\Users\ITACHI\repos\dataedge\backtest\MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the constructor: replace the mangled section with the correct version
old_ctor = """            InitializeComponent();
                                    currentWeekStart = GetStartOfWeek(DateTime.Now);

                                    // Appliquer le paramètre d'activation de l'agent au démarrage
                                    UpdateAgentFabVisibility();
                                    currentWeekStart = GetStartOfWeek(DateTime.Now);

                                    // Initialisation des données"""
# Actually, let me just fix by pattern matching
# Remove extra indentation on InitializeComponent and remove the duplicate line
content = content.replace(
    '                                    InitializeComponent();\n            currentWeekStart = GetStartOfWeek(DateTime.Now);\n\n            // Appliquer le paramètre d\\'activation de l\\'agent au démarrage\n            UpdateAgentFabVisibility();\n            currentWeekStart = GetStartOfWeek(DateTime.Now);',
    '            InitializeComponent();\n            currentWeekStart = GetStartOfWeek(DateTime.Now);\n\n            // Appliquer le paramètre d\'activation de l\'agent au démarrage\n            UpdateAgentFabVisibility();'
)

# Fix HideAiAgent to use UpdateAgentFabVisibility
old_hide = """            // Fermeture INSTANTANÉE + retour immédiat du bouton flottant.
            AiAgentDrawer.Visibility = Visibility.Collapsed;
            BtnAiFab.Visibility = Visibility.Visible;
            BtnAiFab.Opacity = 1;"""
new_hide = """            // Fermeture INSTANTANÉE + retour immédiat du bouton flottant.
            AiAgentDrawer.Visibility = Visibility.Collapsed;
            UpdateAgentFabVisibility();"""
content = content.replace(old_hide, new_hide)

# Add UpdateAgentFabVisibility method before ToggleAiAgentSize
old_region = """        /// <summary>
        /// Agrandit/réduit le panneau de discussion (bouton ⤢ du chat)"""
new_method = """        /// <summary>
        /// Met à jour la visibilité du bouton flottant de l'agent (BtnAiFab)
        /// en fonction du paramètre IsAgentEnabled et de l'état du panneau.
        /// Appelée au démarrage et depuis SettingsView lors d'un changement en direct.
        /// </summary>
        public void UpdateAgentFabVisibility()
        {
            bool isEnabled = Properties.Settings.Default.IsAgentEnabled;

            if (!isEnabled && _aiAgentOpen)
            {
                // L'agent est désactivé : on ferme le panneau s'il est ouvert
                HideAiAgent();
            }

            // On ne montre le FAB que si l'agent est activé ET que le panneau est fermé
            BtnAiFab.Visibility = (isEnabled && !_aiAgentOpen)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Agrandit/réduit le panneau de discussion (bouton ⤢ du chat)"""
content = content.replace(old_region, new_method)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print('MainWindow.xaml.cs fixed')

# --- Fix SettingsView.xaml.cs ---
path2 = r'c:\Users\ITACHI\repos\dataedge\backtest\SettingsView.xaml.cs'
with open(path2, 'r', encoding='utf-8') as f:
    content2 = f.read()

# Fix the indentation on ShowAppPanel
content2 = content2.replace(
    '                    public void ShowAppPanel()',
    '        public void ShowAppPanel()'
)

# Add ChkAgentEnabled_Click handler - insert before BtnAccountTab_Click
old_btn = "        private void BtnAccountTab_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();"
new_btn = """        private void ChkAgentEnabled_Click(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ChkAgentEnabled.IsChecked ?? true;
            Properties.Settings.Default.IsAgentEnabled = isEnabled;
            Properties.Settings.Default.Save();

            // Propager le changement immédiatement sur le bouton flottant du MainWindow
            if (Application.Current.MainWindow is MainWindow mw)
                mw.UpdateAgentFabVisibility();
        }

        private void BtnAccountTab_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();"""
content2 = content2.replace(old_btn, new_btn)

with open(path2, 'w', encoding='utf-8') as f:
    f.write(content2)
print('SettingsView.xaml.cs fixed')
