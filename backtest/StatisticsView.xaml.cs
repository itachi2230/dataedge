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
            _stats = strategie.RetrieveStats() ?? new AdvancedStats(); // Sécurité si RetrieveStats est null

            // 1. Charger toutes les stats de base
            RefreshUI();

            // 2. Lancer l'audit profond
            GenerateDeepAudit();

            // 3. Lancer la simulation monétaire
            UpdateMoneyEquity();
        }

        public void RefreshUI()
        {
            try 
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
            catch (Exception ex) 
            {
                Console.WriteLine("Erreur RefreshUI: " + ex.Message);
            }
        }

        private void LoadHeaderStats()
        {
            var basic = _strategie.GetStatistics();
            if (basic == null) return;

            TxtWinrate.Text = $"{GetSafeDouble(basic.ContainsKey("Winrate") ? basic["Winrate"] : 0):F1}%";
            TxtExpectancy.Text = $"{GetSafeDouble(basic.ContainsKey("Expectancy") ? basic["Expectancy"] : 0):F2} R";
            TxtProfitFactor.Text = $"{GetSafeDouble(basic.ContainsKey("Profit Factor") ? basic["Profit Factor"] : 0):F2}";
        }

        private void LoadEquityCurve()
        {
            var model = CreateBaseModel("COURBE DE CROISSANCE (R)");
            var series = new AreaSeries
            {
                Color = OxyColor.FromRgb(34, 211, 238),
                Fill = OxyColor.FromAColor(30, OxyColor.FromRgb(34, 211, 238)),
                StrokeThickness = 2
            };

            double cumulativeR = 0;
            double maxR = 0;
            double maxDD = 0;
            
            var trades = _strategie.LoadData()?.Trades?.OrderBy(t => t.DateEntree).ToList() ?? new List<Trade>();

            series.Points.Add(new DataPoint(0, 0));

            if (trades.Any())
            {
                for (int i = 0; i < trades.Count; i++)
                {
                    cumulativeR += (trades[i].Result == Resultat.TP) ? trades[i].RR : -1;
                    series.Points.Add(new DataPoint(i + 1, cumulativeR));
                    if (cumulativeR > maxR) maxR = cumulativeR;
                    maxDD = Math.Max(maxDD, maxR - cumulativeR);
                }
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
                
                double val = 0;
                if (_stats.DayOfWeekStats != null && _stats.DayOfWeekStats.TryGetValue(day, out var s))
                {
                    val = s.Expectancy;
                }
                
                axis.Labels.Add(day.ToString().Substring(0, 3).ToUpper());
                series.Items.Add(new ColumnItem(val));
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
            if (_stats.PairStats != null)
            {
                foreach (var pair in _stats.PairStats)
                    dict.Add(pair.Key, CreateDonutModel(pair.Key, pair.Key));
            }
            PairStatsContainer.ItemsSource = dict;
        }

        private void LoadOrderTypeCharts()
        {
            var dict = new Dictionary<string, PlotModel>();
            if (_stats.TypeOrdreStats != null)
            {
                if (_stats.TypeOrdreStats.ContainsKey(TypeOrdre.BUY))
                    dict.Add("ACHATS (BUY)", CreateDonutModel("BUY", "BUY"));
                if (_stats.TypeOrdreStats.ContainsKey(TypeOrdre.SELL))
                    dict.Add("VENTES (SELL)", CreateDonutModel("SELL", "SELL"));
            }
            OrderTypeContainer.ItemsSource = dict;
        }

        private void UpdateMoneyEquity()
        {
            double initialCap = GetSafeDouble(InputCapital.Text);
            if (initialCap <= 0) initialCap = 10000; // Valeur par défaut si vide

            double riskPercent = GetSafeDouble(InputRisk.Text) / 100.0;

            var model = new PlotModel { Title = "PROJECTION MONÉTAIRE", TitleColor = OxyColors.White, TitleFontSize = 12, DefaultFont = "Segoe UI" };

            var series = new AreaSeries
            {
                Color = OxyColor.FromRgb(34, 197, 94),
                Fill = OxyColor.FromAColor(40, OxyColor.FromRgb(34, 197, 94)),
                StrokeThickness = 2,
                MarkerType = MarkerType.Circle,
                TrackerFormatString = "Trade: {2:0}\nSolde: {4:N2} €"
            };

            double currentCap = initialCap;
            series.Points.Add(new DataPoint(0, currentCap));

            var trades = _strategie.GetTrades()?.OrderBy(t => t.DateEntree).ToList() ?? new List<Trade>();
            
            foreach (var t in trades)
            {
                double riskAmount = currentCap * riskPercent;
                double resultR = (t.Result == Resultat.TP) ? t.RR : -1;
                currentCap += (riskAmount * resultR);
                series.Points.Add(new DataPoint(series.Points.Count, currentCap));
            }

            model.Series.Add(series);
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, TextColor = OxyColors.Gray, StringFormat = "N0", Unit = "€" });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, TextColor = OxyColors.Gray, Title = "Nombre de Trades" });

            PlotMoneyEquity.Model = model;
            PlotMoneyEquity.InvalidatePlot(true);
        }

        private void GenerateDeepAudit()
        {
            var basicStats = _strategie.GetStatistics();
            var trades = _strategie.GetTrades()?.OrderBy(t => t.DateEntree).ToList() ?? new List<Trade>();

            if (!trades.Any())
            {
                TxtExecutiveSummary.Text = "En attente de données pour l'audit...";
                return;
            }

            double pf = GetSafeDouble(basicStats["Profit Factor"]);
            double wr = GetSafeDouble(basicStats["Winrate"]);
            double exp = GetSafeDouble(basicStats["Expectancy"]);
            int total = trades.Count;

            double cumulativeR = 0, peakR = 0, maxDD = 0;
            foreach (var t in trades)
            {
                cumulativeR += (t.Result == Resultat.TP) ? t.RR : -1;
                if (cumulativeR > peakR) peakR = cumulativeR;
                maxDD = Math.Max(maxDD, peakR - cumulativeR);
            }

            // Audit Exécutif
            TxtExecutiveSummary.Text = $"Analyse sur {total} trades. Espérance : {exp:F2}R. " + 
                                       (maxDD > 10 ? "Drawdown élevé détecté." : "Courbe stable.");

            // Profil de risque
            if (total < 20) TxtRiskProfile.Text = "Échantillon trop faible pour un profil fiable.";
            else if (wr > 65) TxtRiskProfile.Text = $"Profil 'Sniper' ({wr:F0}%). Séries de pertes courtes.";
            else TxtRiskProfile.Text = $"Profil 'Équilibré'. DD de {maxDD:F1}R.";

            // Scalabilité
            TxtScalability.Text = (pf > 1.5 && maxDD < 10) ? "EXCELLENTE. Prêt pour augmenter le risque." : "MODÉRÉE à FAIBLE.";

            // Verdict
            if (exp <= 0) TxtFinalVerdict.Text = "❌ SYSTÈME À ÉCARTER";
            else if (exp >= 0.5) TxtFinalVerdict.Text = "🚀 PÉPITE DÉTECTÉE";
            else TxtFinalVerdict.Text = "✅ SYSTÈME SOLIDE";
        }

        private void LoadDynamicCharts()
        {
            var dict = new Dictionary<string, PlotModel>();
            if (_stats.PerformanceStats == null) return;

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

            if (_stats.SessionStats != null && _stats.SessionStats.ContainsKey(key)) s = _stats.SessionStats[key];
            else if (_stats.PairStats != null && _stats.PairStats.ContainsKey(key)) s = _stats.PairStats[key];
            else if (_stats.TypeOrdreStats != null)
            {
                if (key == "BUY" && _stats.TypeOrdreStats.ContainsKey(TypeOrdre.BUY)) s = _stats.TypeOrdreStats[TypeOrdre.BUY];
                else if (key == "SELL" && _stats.TypeOrdreStats.ContainsKey(TypeOrdre.SELL)) s = _stats.TypeOrdreStats[TypeOrdre.SELL];
            }

            var series = new PieSeries { InnerDiameter = 0.6, InsideLabelFormat = "" };
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
            var best = _stats.BestConfigs?.FirstOrDefault();
            var worst = _stats.WorstConfigs?.FirstOrDefault();
            
            if (best == null && worst == null)
            {
                TxtKeyAdvice.Text = "💡 CONSEIL : Commencez à saisir des trades pour recevoir des conseils.";
                return;
            }

            TxtKeyAdvice.Text = (worst != null) ? $"💡 CONSEIL : Filtrez les setups '{worst.NomParametre}'." : "💡 CONSEIL : Continuez l'exécution.";
        }

        private PlotModel CreateBaseModel(string title) => new PlotModel
        {
            Title = title,
            TitleColor = OxyColors.White,
            TitleFontSize = 10,
            Background = OxyColors.Transparent,
            PlotAreaBorderColor = OxyColors.Transparent
        };

        private double GetSafeDouble(object value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString())) return 0;
            string s = value.ToString().Replace(",", ".");
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double res);
            return res;
        }

        private void BtnSimulate_Click(object sender, RoutedEventArgs e) => UpdateMoneyEquity();

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