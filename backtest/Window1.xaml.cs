using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace backtest
{
    public partial class Window1 : Window
    {
        private Strategie _strategie;
        private AdvancedStats stats;

        public Window1(Strategie strategie)
        {
            InitializeComponent();
            _strategie = strategie;
            stats = _strategie.RetrieveStats();
            LoadDayPerformanceChart();
            LoadSessionPerformanceCharts(); // Charger les stats des sessions de trading
            LoadPairPerformanceCharts(); // Charger les stats des paires de trading
            LoadOrderTypePerformanceCharts(); // Charger les stats par type d'ordre
            LoadDynamicFieldCharts();

        }

        private void LoadDayPerformanceChart()
        {
            var dayOfWeekStats = stats.DayOfWeekStats;

            var model = new PlotModel
            {
                Title = "Performance par jour de la semaine",
                TextColor = OxyColors.White,
                PlotAreaBorderColor = OxyColors.White,
                Background = OxyColors.Black
            };

            model.LegendPlacement = LegendPlacement.Outside;
            model.LegendPosition = LegendPosition.BottomCenter;
            model.LegendOrientation = LegendOrientation.Horizontal;
            model.LegendTextColor = OxyColors.White;

            var orderedDays = new List<DayOfWeek>
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                TextColor = OxyColors.White,
                Title = "Jours de la semaine",
                TitleColor = OxyColors.White
            };

            foreach (var day in orderedDays)
            {
                categoryAxis.Labels.Add(TranslateDayToFrench(day.ToString()));
            }

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                Maximum = 100,
                Title = "Pourcentage (%)",
                TitleColor = OxyColors.White,
                TextColor = OxyColors.White,
                MajorGridlineColor = OxyColor.FromArgb(50, 255, 255, 255),
                MinorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255)
            };

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);

            var tpSeries = new ColumnSeries { Title = "TP (%)", FillColor = OxyColor.FromAColor(180, OxyColors.LimeGreen) };
            var slSeries = new ColumnSeries { Title = "SL (%)", FillColor = OxyColor.FromAColor(180, OxyColors.Red) };

            for (int i = 0; i < orderedDays.Count; i++)
            {
                var dayEnum = orderedDays[i];

                if (dayOfWeekStats.TryGetValue(dayEnum, out var stat))
                {
                    tpSeries.Items.Add(new ColumnItem(stat.PercentTP, i));
                    slSeries.Items.Add(new ColumnItem(stat.PercentSL, i));
                }
                else
                {
                    tpSeries.Items.Add(new ColumnItem(0, i));
                    slSeries.Items.Add(new ColumnItem(0, i));
                }
            }

            model.Series.Add(tpSeries);
            model.Series.Add(slSeries);

            DayPerformancePlot.Model = model;
        }

        private void LoadSessionPerformanceCharts()
        {
            var sessionStats = stats.SessionStats;

            double tokyoTP = sessionStats.ContainsKey("Tokyo") ? sessionStats["Tokyo"].PercentTP : 0;
            double tokyoSL = sessionStats.ContainsKey("Tokyo") ? sessionStats["Tokyo"].PercentSL : 0;

            double londonTP = sessionStats.ContainsKey("Londres") ? sessionStats["Londres"].PercentTP : 0;
            double londonSL = sessionStats.ContainsKey("Londres") ? sessionStats["Londres"].PercentSL : 0;

            double newyorkTP = sessionStats.ContainsKey("New York") ? sessionStats["New York"].PercentTP : 0;
            double newyorkSL = sessionStats.ContainsKey("New York") ? sessionStats["New York"].PercentSL : 0;

            AsianSessionPlot.Model = CreatePieChart("Session Asiatique (Tokyo)", tokyoTP, tokyoSL, "#FF95C610");
            EuropeanSessionPlot.Model = CreatePieChart("Session Européenne (Londres)", londonTP, londonSL, "#FF21A1EA");
            AmericanSessionPlot.Model = CreatePieChart("Session Américaine (New York)", newyorkTP, newyorkSL, "#FFBE29FF");
        }

        private void LoadPairPerformanceCharts()
        {
            var pairStats = stats.PairStats;
            var pairModels = new Dictionary<string, PlotModel>();

            foreach (var pair in pairStats)
            {
                var model = CreatePieChart(pair.Key, pair.Value.PercentTP, pair.Value.PercentSL, "#FF4B71FE", "#FF494949");
                pairModels.Add(pair.Key, model);
            }

            PairStatsContainer.ItemsSource = pairModels;
        }
      
        private PlotModel CreatePieChart(string title, double tpPercentage, double slPercentage,string couleurTp= "#FF1DC951", string couleurSl= "#FFED5858", string couleur = "#FF1E1E2F")
        {
            
            var model = new PlotModel
            {
               // Title = title,
                TextColor = OxyColors.White,
                PlotAreaBorderColor = OxyColors.Transparent,
                Background = OxyColor.Parse(couleur)
            };

            var pieSeries = new PieSeries
            {
                StrokeThickness = 2,
                InsideLabelColor = OxyColors.White,
                AngleSpan = 360,
                StartAngle = 0
            };
            pieSeries.Slices.Add(new PieSlice("TP", tpPercentage) { Fill = OxyColor.Parse(couleurTp) });
            pieSeries.Slices.Add(new PieSlice("SL", slPercentage) { Fill = OxyColor.Parse(couleurSl) });
            
            
            model.Series.Add(pieSeries);

            return model;
        }
        private void LoadOrderTypePerformanceCharts()
        {
            var orderTypeStats = stats.TypeOrdreStats;
            var orderModels = new Dictionary<string, PlotModel>();

            foreach (var order in orderTypeStats)
            {
                var model = CreatePieChart(order.Key.ToString(), order.Value.PercentTP, order.Value.PercentSL);
                orderModels.Add(order.Key.ToString(), model);
            }

            OrderTypeStatsContainer.ItemsSource = orderModels;
        }
        private void LoadDynamicFieldCharts()
        {
            var dynamicModels = new Dictionary<string, PlotModel>();

            foreach (var field in stats.PerformanceStats)
            {
                string fieldName = field.Key;
                var fieldValues = field.Value; // Dictionnaire <valeur, PerformanceStat>

                if (fieldValues.Keys.All(IsNumeric))
                {
                    // Histogramme pour les valeurs numériques
                    dynamicModels.Add(fieldName, CreateBarChart(fieldName, fieldValues));
                }
                else
                {
                    // Pie Charts pour les valeurs textuelles (global et individuel)
                    var pieCharts = CreatePieChartsForTextValues(fieldName, fieldValues);
                    foreach (var chart in pieCharts)
                    {
                        dynamicModels.Add(chart.Key, chart.Value);
                    }
                }
            }

            DynamicStatsContainer.ItemsSource = dynamicModels;
        }
        private bool IsNumeric(string value)
        {
            return double.TryParse(value, out _);
        }
        private PlotModel CreateBarChart(string title, Dictionary<string, PerformanceStat> data)
        {
            var model = new PlotModel
            {
                Title = title,
                TextColor = OxyColors.White,
                PlotAreaBorderColor = OxyColors.White,
                Background = OxyColors.Black
            };

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                TextColor = OxyColors.White
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Performance (%)",
                TextColor = OxyColors.White
            };

            var tpSeries = new ColumnSeries { Title = "TP (%)", FillColor = OxyColors.LimeGreen };
            var slSeries = new ColumnSeries { Title = "SL (%)", FillColor = OxyColors.Red };

            foreach (var entry in data)
            {
                categoryAxis.Labels.Add(entry.Key); // Valeur numérique
                tpSeries.Items.Add(new ColumnItem(entry.Value.PercentTP));
                slSeries.Items.Add(new ColumnItem(entry.Value.PercentSL));
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(tpSeries);
            model.Series.Add(slSeries);

            return model;
        }
        private Dictionary<string, PlotModel> CreatePieChartsForTextValues(string title, Dictionary<string, PerformanceStat> data)
        {
            var models = new Dictionary<string, PlotModel>();

            // 📌 Pie Chart Global - Répartition des valeurs
            var globalModel = new PlotModel
            {
                Title = $"Répartition globale - {title}",
                TextColor = OxyColors.White,
                PlotAreaBorderColor = OxyColors.Transparent,
                Background = OxyColors.Black
            };

            var globalPieSeries = new PieSeries
            {
                StrokeThickness = 2,
                InsideLabelColor = OxyColors.White,
                AngleSpan = 360,
                StartAngle = 0
            };

            // Dictionnaire pour stocker les modèles individuels
            foreach (var entry in data)
            {
                string fieldValue = entry.Key;
                double totalPercent = entry.Value.PercentTP + entry.Value.PercentSL;

                // Ajout au pie chart global
                globalPieSeries.Slices.Add(new PieSlice(fieldValue, totalPercent) { Fill = OxyColors.CornflowerBlue });

                // 📌 Pie Chart Individuel - TP vs SL pour une valeur donnée
                var individualModel = new PlotModel
                {
                    Title = $"{title} - {fieldValue}",
                    TextColor = OxyColors.White,
                    PlotAreaBorderColor = OxyColors.Transparent,
                    Background = OxyColors.Black
                };

                var individualPieSeries = new PieSeries
                {
                    StrokeThickness = 2,
                    InsideLabelColor = OxyColors.White,
                    AngleSpan = 360,
                    StartAngle = 0
                };

                individualPieSeries.Slices.Add(new PieSlice("TP", entry.Value.PercentTP) { Fill = OxyColors.LimeGreen });
                individualPieSeries.Slices.Add(new PieSlice("SL", entry.Value.PercentSL) { Fill = OxyColors.Red });

                individualModel.Series.Add(individualPieSeries);
                models.Add(fieldValue, individualModel);
            }

            globalModel.Series.Add(globalPieSeries);
            models.Add($"Répartition globale - {title}", globalModel);

            return models;
        }

        private string TranslateDayToFrench(string day)
        {
            return day switch
            {
                "Monday" => "Lundi",
                "Tuesday" => "Mardi",
                "Wednesday" => "Mercredi",
                "Thursday" => "Jeudi",
                "Friday" => "Vendredi",
                "Saturday" => "Samedi",
                "Sunday" => "Dimanche",
                _ => "Inconnu"
            };
        }
    }
}
