using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace backtest
{
    public partial class StatisticsView : UserControl
    {
        private AdvancedStats _stats;
        private Strategie _strategie;
        private static readonly OxyColor ColorTP = OxyColor.FromRgb(34, 197, 94);
        private static readonly OxyColor ColorSL = OxyColor.FromRgb(239, 68, 68);

        public StatisticsView(Strategie strategie)
        {
            InitializeComponent();
            _strategie = strategie;
            _stats = strategie.RetrieveStats();

            // 1. Charger toutes les stats de base
            RefreshUI();

            // 2. Lancer l'audit profond (Remplit les TextBlocks de conclusion)
            GenerateDeepAudit();

            // 3. Lancer une première simulation avec les valeurs par défaut (10k, 1%)
            UpdateMoneyEquity();
        }

        public void RefreshUI()
        {
            LoadHeaderStats();
            LoadEquityCurve();
            LoadWeeklyChart();
            LoadSessionCharts();
            LoadPairCharts();
            LoadOrderTypeCharts();
            LoadDynamicCharts();
            GenerateAdvice();
        }

        private void LoadHeaderStats()
        {
            var basic = _strategie.GetStatistics();
            TxtWinrate.Text = $"{GetSafeDouble(basic["Winrate"]):F1}%";
            TxtExpectancy.Text = $"{GetSafeDouble(basic["Expectancy"]):F2} R";
            TxtProfitFactor.Text = $"{GetSafeDouble(basic["Profit Factor"]):F2}";
        }

        private void LoadEquityCurve()
        {

            var model = CreateBaseModel("");
            var series = new AreaSeries
            {
                Color = OxyColor.FromRgb(34, 211, 238),
                Fill = OxyColor.FromAColor(30, OxyColor.FromRgb(34, 211, 238)),
                StrokeThickness = 2
            };

            double cumulativeR = 0;
            double maxR = 0;
            double maxDD = 0;
            var trades = _strategie.LoadData().Trades.OrderBy(t => t.DateEntree).ToList();

            series.Points.Add(new DataPoint(0, 0));
            for (int i = 0; i < trades.Count; i++)
            {
                cumulativeR += (trades[i].Result == Resultat.TP) ? trades[i].RR : -1;
                series.Points.Add(new DataPoint(i + 1, cumulativeR));
                if (cumulativeR > maxR) maxR = cumulativeR;
                maxDD = Math.Max(maxDD, maxR - cumulativeR);
            }

            TxtMaxDrawdown.Text = $"{maxDD:F1} R";
            model.Series.Add(series);
            PlotEquity.Model = model;
        }

        private void LoadWeeklyChart()
        {
            var model = CreateBaseModel("");
            var series = new ColumnSeries { FillColor = ColorTP, NegativeFillColor = ColorSL };
            var axis = new CategoryAxis { Position = AxisPosition.Bottom, TextColor = OxyColors.Gray };

            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday) continue;
                _stats.DayOfWeekStats.TryGetValue(day, out var s);
                axis.Labels.Add(day.ToString().Substring(0, 3).ToUpper());
                series.Items.Add(new ColumnItem(s?.Expectancy ?? 0));
            }
            model.Axes.Add(axis);
            model.Series.Add(series);
            PlotWeekly.Model = model;
        }

        private void LoadSessionCharts()
        {
            PlotTokyo.Model = CreateDonutModel("TOKYO", "Tokyo");
            PlotLondon.Model = CreateDonutModel("LONDRES", "Londres");
            PlotNewYork.Model = CreateDonutModel("NEW YORK", "New York");
        }

        private void LoadPairCharts()
        {
            var dict = new Dictionary<string, PlotModel>();
            foreach (var pair in _stats.PairStats)
                dict.Add(pair.Key, CreateDonutModel(pair.Key, pair.Key));
            PairStatsContainer.ItemsSource = dict;
        }

        private void LoadOrderTypeCharts()
        {
            var dict = new Dictionary<string, PlotModel>();
            if (_stats.TypeOrdreStats.ContainsKey(TypeOrdre.BUY))
                dict.Add("ACHATS (BUY)", CreateDonutModel("BUY", TypeOrdre.BUY.ToString()));
            if (_stats.TypeOrdreStats.ContainsKey(TypeOrdre.SELL))
                dict.Add("VENTES (SELL)", CreateDonutModel("SELL", TypeOrdre.SELL.ToString()));
            OrderTypeContainer.ItemsSource = dict;
        }
        private void BtnSimulate_Click(object sender, RoutedEventArgs e)
        {
            UpdateMoneyEquity();
        }

        private void UpdateMoneyEquity()
        {
            double initialCap = GetSafeDouble(InputCapital.Text);
            double riskPercent = GetSafeDouble(InputRisk.Text) / 100.0;

            // On crée un nouveau modèle avec un Tracker personnalisé
            var model = new PlotModel
            {
                Title = "PROJECTION MONÉTAIRE",
                TitleColor = OxyColors.White,
                TitleFontSize = 12,
                SelectionColor = OxyColors.Cyan,
                DefaultFont = "Segoe UI"
            };

            // CONFIGURATION DE LA SÉRIE AVEC TRACKER PERSONNALISÉ
            var series = new AreaSeries
            {
                Color = OxyColor.FromRgb(34, 197, 94),
                Fill = OxyColor.FromAColor(40, OxyColor.FromRgb(34, 197, 94)),
                StrokeThickness = 2,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerStroke = OxyColors.White,
                MarkerFill = OxyColor.FromRgb(34, 197, 94),
                // C'EST ICI : Format du texte au survol
                // {0} = Titre de la série, {1} = Axe X, {2} = Axe Y, {4} = Valeur X, {5} = Valeur Y
                TrackerFormatString = "Trade: {2:0}\nSolde: {4:N2} €"
            };

            double currentCap = initialCap;
            series.Points.Add(new DataPoint(0, currentCap));

            var trades = _strategie.GetTrades().OrderBy(t => t.DateEntree).ToList();
            for (int i = 0; i < trades.Count; i++)
            {
                double riskAmount = currentCap * riskPercent;
                double resultR = (trades[i].Result == Resultat.TP) ? trades[i].RR : -1;
                currentCap += (riskAmount * resultR);
                series.Points.Add(new DataPoint(i + 1, currentCap));
            }

            model.Series.Add(series);

            // Axe Y (Argent)
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColors.Gray,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(15, OxyColors.White),
                StringFormat = "N0", // Affiche les milliers proprement
                Unit = "€"
            });

            // Axe X (Nombre de trades)
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                TextColor = OxyColors.Gray,
                Title = "Nombre de Trades"
            });

            PlotMoneyEquity.Model = model;
            PlotMoneyEquity.InvalidatePlot(true);
        }
        private void GenerateDeepAudit()
        {
            var basicStats = _strategie.GetStatistics();
            var trades = _strategie.GetTrades().OrderBy(t => t.DateEntree).ToList();

            double pf = GetSafeDouble(basicStats["Profit Factor"]);
            double wr = GetSafeDouble(basicStats["Winrate"]);
            double exp = GetSafeDouble(basicStats["Expectancy"]);
            int total = trades.Count;

            // --- 1. CALCUL DU MAX DRAWDOWN (Unités R) ---
            double cumulativeR = 0;
            double peakR = 0;
            double maxDD = 0;

            foreach (var t in trades)
            {
                cumulativeR += (t.Result == Resultat.TP) ? t.RR : -1;
                if (cumulativeR > peakR) peakR = cumulativeR;
                double currentDD = peakR - cumulativeR;
                if (currentDD > maxDD) maxDD = currentDD;
            }

            // --- 2. AUDIT EXÉCUTIF (Haut de page) ---
            string audit = $"Analyse basée sur {total} trades. Votre avantage statistique est de {exp:F2}R par trade. ";
            if (maxDD > 10) audit += "Attention : votre Drawdown est élevé, ce qui suggère une volatilité importante de votre équité.";
            else audit += "Votre courbe de croissance est remarquablement stable.";

            TxtExecutiveSummary.Text = audit;

            // --- 3. PROFIL DE RISQUE (Section Conclusion) ---
            if (total < 20)
            {
                TxtRiskProfile.Text = "Échantillon trop faible (moins de 20 trades). Les résultats actuels sont peut-être dus à la variance positive.";
            }
            else if (wr > 65)
            {
                TxtRiskProfile.Text = $"Profil 'Sniper' : Très haute précision ({wr:F0}%). Vos séries de pertes sont courtes ({maxDD:F1}R max), ce qui facilite la discipline.";
            }
            else if (wr < 35 && pf > 1.5)
            {
                TxtRiskProfile.Text = "Profil 'Trend Follower' : Précision faible mais gains explosifs. Capital psychologique requis pour tenir les phases de perte.";
            }
            else
            {
                TxtRiskProfile.Text = $"Profil 'Équilibré' : Statistiques saines. Distribution de risque standard avec un DD maîtrisé de {maxDD:F1}R.";
            }

            // --- 4. SCALABILITÉ ---
            if (pf > 2.0 && total > 30 && maxDD < 8)
            {
                TxtScalability.Text = "EXCELLENTE. Le système est mathématiquement robuste. Vous pouvez envisager d'augmenter le risque par trade progressivement.";
            }
            else if (pf > 1.2 && maxDD < 12)
            {
                TxtScalability.Text = "MODÉRÉE. Le système est rentable. Pour scaler, concentrez-vous sur la réduction des erreurs d'exécution.";
            }
            else
            {
                TxtScalability.Text = "FAIBLE. Le système est trop proche du point mort ou trop instable. Priorisez la survie du capital avant la croissance.";
            }

            // --- 5. VERDICT FINAL & CONSEIL ---
            if (exp <= 0)
            {
                TxtFinalVerdict.Text = "❌ SYSTÈME À ÉCARTER : Espérance négative. Ce système perd de l'argent statistiquement.";
                TxtKeyAdvice.Text = "💡 CONSEIL : Analysez vos pertes. S'agit-il d'un mauvais setup ou d'une mauvaise gestion du risque ?";
            }
            else if (exp < 0.25)
            {
                TxtFinalVerdict.Text = "⚠️ À OPTIMISER : Rentabilité marginale. Les frais de courtage et le slippage pourraient annuler vos gains réels.";
                TxtKeyAdvice.Text = "💡 CONSEIL : Filtrez les trades avec un RR inférieur à 1.5 pour booster votre espérance.";
            }
            else if (exp >= 0.5)
            {
                TxtFinalVerdict.Text = "🚀 PÉPITE DÉTECTÉE : Avantage statistique massif. Exécution prioritaire requise.";
                TxtKeyAdvice.Text = "💡 CONSEIL : Ne changez rien. Votre discipline est votre seul ennemi maintenant.";
            }
            else
            {
                TxtFinalVerdict.Text = "✅ SYSTÈME SOLIDE : Stratégie viable pour un trading professionnel régulier.";
                TxtKeyAdvice.Text = (maxDD > 8) ? "💡 CONSEIL : Réduisez légèrement votre risque pour lisser le Drawdown." : "💡 CONSEIL : Continuez ainsi, les métriques sont équilibrées.";
            }
        }
        private void LoadDynamicCharts()
        {
            var dict = new Dictionary<string, PlotModel>();
            foreach (var field in _stats.PerformanceStats)
            {
                var model = CreateBaseModel("");
                var categoryAxis = new CategoryAxis { Position = AxisPosition.Left, TextColor = OxyColors.White };
                var series = new BarSeries { FillColor = OxyColor.FromRgb(56, 189, 248) };
                foreach (var val in field.Value.OrderBy(x => x.Value.Expectancy))
                {
                    categoryAxis.Labels.Add(val.Key);
                    series.Items.Add(new BarItem(val.Value.Expectancy));
                }
                model.Axes.Add(categoryAxis);
                model.Series.Add(series);
                dict.Add(field.Key.ToUpper(), model);
            }
            DynamicStatsContainer.ItemsSource = dict;
        }

        private PlotModel CreateDonutModel(string title, string key)
        {
            var model = CreateBaseModel(title);
            PerformanceStat s = null;
            if (_stats.SessionStats.ContainsKey(key)) s = _stats.SessionStats[key];
            else if (_stats.PairStats.ContainsKey(key)) s = _stats.PairStats[key];
            else if (key == "BUY") s = _stats.TypeOrdreStats[TypeOrdre.BUY];
            else if (key == "SELL") s = _stats.TypeOrdreStats[TypeOrdre.SELL];

            var series = new PieSeries { InnerDiameter = 0.6, StrokeThickness = 0, InsideLabelFormat = "" };
            if (s != null && s.TotalTrades > 0)
            {
                series.Slices.Add(new PieSlice("Gain", s.TotalProfit) { Fill = ColorTP });
                series.Slices.Add(new PieSlice("Perte", s.TotalLoss) { Fill = ColorSL });
            }
            else
            {
                series.Slices.Add(new PieSlice("N/A", 1) { Fill = OxyColor.FromRgb(31, 41, 55) });
            }
            model.Series.Add(series);
            return model;
        }

        private void GenerateAdvice()
        {
            var best = _stats.BestConfigs.FirstOrDefault();
            var worst = _stats.WorstConfigs.FirstOrDefault();
            TxtExecutiveSummary.Text = $"Analyse : Votre avantage principal réside dans '{best?.NomParametre ?? "N/A"}'. " +
                $"Cependant, la configuration '{worst?.NomParametre ?? "N/A"}' dégrade fortement votre performance nette.";
            TxtKeyAdvice.Text = (worst != null) ? $"💡 CONSEIL : Filtrez ou éliminez les setups '{worst.NomParametre}'." : "💡 CONSEIL : Stratégie équilibrée. Continuez l'exécution.";
        }

        private PlotModel CreateBaseModel(string title) => new PlotModel
        {
            Title = title,
            TitleColor = OxyColors.White,
            TitleFontSize = 10,
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            PlotAreaBorderColor = OxyColors.Transparent
        };

        private double GetSafeDouble(object value)
        {
            if (value == null) return 0;
            string s = value.ToString().Replace(",", ".");
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double res);
            return res;
        }

        private void Control_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent, Source = sender };
                var parent = ((FrameworkElement)sender).Parent as System.Windows.UIElement;
                parent?.RaiseEvent(eventArg);
            }
        }
    }
}