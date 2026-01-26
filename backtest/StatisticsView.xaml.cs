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

        private void RefreshUI()
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
            // On récupère les valeurs saisies par l'utilisateur
            double initialCap = GetSafeDouble(InputCapital.Text);
            double riskPercent = GetSafeDouble(InputRisk.Text) / 100.0;

            if (initialCap <= 0) initialCap = 1000; // Sécurité

            var model = CreateBaseModel("ÉVOLUTION DU CAPITAL RÉEL");

            // Style de la ligne : Vert trading pour l'argent
            var series = new AreaSeries
            {
                Color = OxyColor.FromRgb(34, 197, 94),
                Fill = OxyColor.FromAColor(40, OxyColor.FromRgb(34, 197, 94)),
                StrokeThickness = 2,
                MarkerType = MarkerType.None
            };

            double currentCap = initialCap;
            series.Points.Add(new DataPoint(0, currentCap));

            // On récupère les trades triés par date
            var trades = _strategie.GetTrades().OrderBy(t => t.DateEntree).ToList();

            foreach (var t in trades)
            {
                // Calcul du risque monétaire sur le capital ACTUEL (Compound Interest)
                double riskAmount = currentCap * riskPercent;

                // Un TP gagne son RR x Risque, un SL perd 1x Risque
                double resultMultiplier = (t.Result == Resultat.TP) ? t.RR : -1;

                currentCap += (riskAmount * resultMultiplier);
                series.Points.Add(new DataPoint(series.Points.Count, currentCap));
            }

            model.Series.Add(series);

            // Axe Y avec format monétaire (€)
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColors.White,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(20, OxyColors.White),
                LabelFormatter = d => d.ToString("N0") + " €"
            });

            // CRUCIAL : Réassigner le modèle pour forcer WPF à redessiner
            PlotMoneyEquity.Model = model;
            PlotMoneyEquity.InvalidatePlot(true);
        }

        private void GenerateDeepAudit()
        {
            var basicStats = _strategie.GetStatistics();
            double pf = GetSafeDouble(basicStats["Profit Factor"]);
            double wr = GetSafeDouble(basicStats["Winrate"]);
            double exp = GetSafeDouble(basicStats["Expectancy"]);
            int total = _strategie.GetTrades().Count;

            // --- 1. PROFIL DE RISQUE ---
            if (total < 20)
            {
                TxtRiskProfile.Text = "Échantillon trop faible. Les résultats actuels sont peut-être dus à la chance (Variance).";
            }
            else if (wr > 65)
            {
                TxtRiskProfile.Text = "Profil 'Sniper' : Haute précision. Vos séries de pertes (drawdowns) seront courtes, mais attention à ne pas couper vos gains trop tôt.";
            }
            else if (wr < 35 && pf > 1.5)
            {
                TxtRiskProfile.Text = "Profil 'Trend Follower' : Faible précision mais gros gains. Vous devez avoir un mental d'acier pour supporter les longues séries de pertes.";
            }
            else
            {
                TxtRiskProfile.Text = "Profil 'Équilibré' : Statistiques saines. Risque modéré et distribution normale des gains.";
            }

            // --- 2. SCALABILITÉ ---
            if (pf > 2.0 && total > 30)
            {
                TxtScalability.Text = "Excellente. Le système est mathématiquement prêt à supporter une augmentation de capital significative.";
            }
            else if (pf > 1.2)
            {
                TxtScalability.Text = "Modérée. Le système est rentable, mais une augmentation de capital trop brutale pourrait être dangereuse sans plus de données.";
            }
            else
            {
                TxtScalability.Text = "Faible. Le système est trop proche du point mort (Break-even). Ne pas augmenter les enjeux pour l'instant.";
            }

            // --- 3. VERDICT FINAL ---
            if (exp <= 0)
            {
                TxtFinalVerdict.Text = "❌ SYSTÈME À ÉCARTER : L'espérance de gain est négative. Chaque trade vous rapproche de la ruine.";
            }
            else if (exp < 0.2)
            {
                TxtFinalVerdict.Text = "⚠️ À OPTIMISER : Vous gagnez de l'argent mais les frais de courtage pourraient annuler vos profits. Filtrez vos entrées.";
            }
            else if (exp >= 0.5)
            {
                TxtFinalVerdict.Text = "🚀 PÉPITE DÉTECTÉE : Avantage statistique massif. Ce système est une véritable machine à cash s'il est suivi avec discipline.";
            }
            else
            {
                TxtFinalVerdict.Text = "✅ SYSTÈME SOLIDE : Stratégie viable pour un trading professionnel régulier.";
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