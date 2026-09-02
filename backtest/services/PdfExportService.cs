using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace backtest
{
    public static class PdfExportService
    {
        private const string Dark = "#0B1220";
        private const string DarkSoft = "#111C2C";
        private const string Accent = "#00C2FF";
        private const string Accent2 = "#7C3AED";
        private const string TextDark = "#101828";
        private const string TextGray = "#475467";
        private const string CardBorder = "#D9E2EC";
        private const string Green = "#10B981";
        private const string Red = "#F04452";
        private const string Amber = "#F59E0B";
        private const string Blue = "#3B82F6";
        private const string Purple = "#8B5CF6";
        private const string Gray = "#94A3B8";
        private const string White = "#FFFFFF";

        private static readonly CultureInfo Fr = GetFrenchCulture();

        static PdfExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private static CultureInfo GetFrenchCulture()
        {
            try { return CultureInfo.GetCultureInfo("fr-FR"); }
            catch { return CultureInfo.InvariantCulture; }
        }

        public static string ExportStatisticsPdf(Strategie strategie)
        {
            if (strategie == null)
                throw new ArgumentNullException(nameof(strategie));

            strategie.CalculateStatistics();

            var trades = (strategie.GetTrades() ?? new List<Trade>()).OrderBy(t => t.DateEntree).ToList();
            var stats = strategie.GetStatistics() ?? new Dictionary<string, object>();
            var advanced = strategie.RetrieveStats() ?? new AdvancedStats();

            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataEdge", "Rapports");
            Directory.CreateDirectory(directory);

            string safeName = SanitizeFileName(strategie.Nom ?? "strategie");
            string filePath = Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.7f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(TextDark));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                    {
                        header.Background(Dark).Padding(12).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("DATAEDGE").FontSize(8).Bold().FontColor(Accent);
                                col.Item().PaddingTop(2).Text(strategie.Nom.ToUpperInvariant()).FontSize(18).Bold().FontColor(White);
                            });
                            row.RelativeItem().AlignRight().Column(col =>
                            {
                                col.Item().AlignRight().Text("RAPPORT DE PERFORMANCE").FontSize(8).Bold().FontColor(White);
                                col.Item().AlignRight().PaddingTop(2).Text(DateTime.Now.ToString("dd/MM/yyyy à HH:mm", Fr)).FontSize(8).FontColor("#B7C5D3");
                            });
                        });
                    });

                    page.Content().Element(content =>
                    {
                        content.Column(col =>
                        {
                            col.Item().PaddingTop(12).Element(StatsSummary(strategie, stats, advanced, trades));
                            col.Item().PaddingTop(14).Element(SectionHeader("1", "SYNTHÈSE STRATÉGIE"));
                            col.Item().PaddingTop(8).Element(StrategyCard(strategie, trades));
                            col.Item().PaddingTop(8).Element(KpiGrid(stats, advanced, trades));
                            col.Item().PaddingTop(12).Element(SectionHeader("2", "ANALYSE DU RETURN"));
                            col.Item().PaddingTop(8).Element(ReturnBreakdown(trades));
                            col.Item().PaddingTop(12).Element(SectionHeader("3", "PERFORMANCE AVANCÉE"));
                            col.Item().PaddingTop(8).Element(AdvancedBreakdown(stats, advanced));
                            col.Item().PaddingTop(12).Element(SectionHeader("4", "HISTORIQUE DES TRADES"));
                            col.Item().PaddingTop(8).Element(TradeTable(trades));
                        });
                    });

                    page.Footer().Element(footer =>
                    {
                        footer.Column(col =>
                        {
                            col.Item().LineHorizontal(0.5f).LineColor(CardBorder);
                            col.Item().PaddingTop(6).Row(row =>
                            {
                                row.RelativeItem().Text("Généré par DataEdge — Trading Analytics").FontSize(7).FontColor(TextGray);
                                row.RelativeItem().AlignRight().Text(t =>
                                {
                                    t.Span("Page ").FontSize(7).FontColor(TextGray);
                                    t.CurrentPageNumber().FontSize(7).Bold().FontColor(TextGray);
                                    t.Span(" / ").FontSize(7).FontColor(TextGray);
                                    t.TotalPages().FontSize(7).Bold().FontColor(TextGray);
                                });
                            });
                        });
                    });
                });
            }).GeneratePdf(filePath);

            return filePath;
        }

        public static void ExportStatisticsPdf(string filePath, Strategie strategie,
            Dictionary<string, object> stats, AdvancedStats advanced,
            string statName, List<string> customFields, List<string> customStatFields)
        {
            if (strategie == null) throw new ArgumentNullException(nameof(strategie));

            var trades = (strategie.GetTrades() ?? new List<Trade>()).OrderBy(t => t.DateEntree).ToList();
            var finalStats = stats ?? strategie.GetStatistics() ?? new Dictionary<string, object>();
            var finalAdvanced = advanced ?? strategie.RetrieveStats() ?? new AdvancedStats();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(TextDark));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                    {
                        header.Background(Dark).Padding(12).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("DATAEDGE").FontSize(8).Bold().FontColor(Accent);
                                col.Item().PaddingTop(2).Text((statName ?? strategie.Nom ?? "STRATÉGIE").ToUpperInvariant()).FontSize(18).Bold().FontColor(White);
                            });
                            row.RelativeItem().AlignRight().Column(col =>
                            {
                                col.Item().AlignRight().Text("RAPPORT DE PERFORMANCE").FontSize(8).Bold().FontColor(White);
                                col.Item().AlignRight().PaddingTop(2).Text(DateTime.Now.ToString("dd/MM/yyyy à HH:mm", Fr)).FontSize(8).FontColor("#B7C5D3");
                            });
                        });
                    });

                    page.Content().Element(content =>
                    {
                        content.Column(col =>
                        {
                            col.Item().PaddingTop(8).Element(StatsSummary(strategie, finalStats, finalAdvanced, trades));
                            col.Item().PaddingTop(12).Element(StrategyCard(strategie, trades));
                            col.Item().PaddingTop(12).Element(KpiGrid(finalStats, finalAdvanced, trades));
                            col.Item().PaddingTop(12).Element(ReturnBreakdown(trades));
                            col.Item().PaddingTop(12).Element(AdvancedBreakdown(finalStats, finalAdvanced));
                            col.Item().PaddingTop(12).Element(TradeTable(trades));
                        });
                    });
                });
            }).GeneratePdf(filePath);
        }

        private static Action<IContainer> StatsSummary(Strategie strategie, Dictionary<string, object> stats, AdvancedStats advanced, List<Trade> trades)
        {
            return c =>
            {
                c.Background(Dark).Padding(14).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("PORTFOLIO / STRATÉGIE").FontSize(8).Bold().FontColor(Accent);
                        col.Item().PaddingTop(2).Text(strategie?.Nom ?? "STRATÉGIE").FontSize(17).Bold().FontColor(White);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        var winrate = TryDouble(stats, "Winrate");
                        col.Item().AlignRight().Text(t =>
                        {
                            t.Span("Trades : ").FontSize(8).FontColor("#A9B9C7");
                            t.Span((trades?.Count ?? 0).ToString("N0", Fr)).FontSize(10).Bold().FontColor(White);
                        });
                        col.Item().AlignRight().PaddingTop(2).Text(t =>
                        {
                            t.Span("Winrate : ").FontSize(8).FontColor("#A9B9C7");
                            t.Span(FormatPercent(winrate)).FontSize(10).Bold().FontColor(winrate >= 50 ? Green : Red);
                        });
                    });
                });
            };
        }

        private static Action<IContainer> SectionHeader(string number, string title)
        {
            return c => c.Background(DarkSoft).Border(1).BorderColor(CardBorder).PaddingHorizontal(10).PaddingVertical(6).Row(row =>
            {
                row.AutoItem().Text(number).FontSize(9).Bold().FontColor(Accent);
                row.AutoItem().PaddingLeft(8).Text(title).FontSize(10).Bold().FontColor(White);
                row.RelativeItem().AlignRight().Height(1).Background("#2B3E56");
            });
        }

        private static Action<IContainer> StrategyCard(Strategie strategie, List<Trade> trades)
        {
            return c =>
            {
                c.Border(1).BorderColor(CardBorder).Padding(10).Column(col =>
                {
                    col.Item().Text("FICHE STRATÉGIE").FontSize(8).Bold().FontColor(Accent);
                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem(2).Column(info =>
                        {
                            info.Item().Text(t =>
                            {
                                t.Span("Description : ").FontSize(8.5f).Bold().FontColor(TextGray);
                                t.Span(string.IsNullOrWhiteSpace(strategie?.description) ? "—" : strategie.description.Trim()).FontSize(8.5f).FontColor(TextDark);
                            });

                            if (trades != null && trades.Count > 0)
                            {
                                var first = trades.Min(x => x.DateEntree);
                                var last = trades.Max(x => x.DateEntree);
                                info.Item().PaddingTop(5).Text(t =>
                                {
                                    t.Span("Période : ").FontSize(8.5f).Bold().FontColor(TextGray);
                                    t.Span(first.ToString("dd/MM/yyyy", Fr) + " → " + last.ToString("dd/MM/yyyy", Fr)).FontSize(8.5f).FontColor(TextDark);
                                });
                            }
                        });

                        row.RelativeItem(1).PaddingLeft(10).Column(meta =>
                        {
                            meta.Item().Text(t =>
                            {
                                t.Span("Nombre de trades : ").FontSize(8.5f).Bold().FontColor(TextGray);
                                t.Span((trades?.Count ?? 0).ToString("N0", Fr)).FontSize(9).Bold().FontColor(Accent2);
                            });
                            meta.Item().PaddingTop(5).Text(t =>
                            {
                                t.Span("Résultats TP / SL : ").FontSize(8.5f).Bold().FontColor(TextGray);
                                t.Span((trades?.Count(x => x.Result == Resultat.TP) ?? 0).ToString() + " / " + (trades?.Count(x => x.Result == Resultat.SL) ?? 0).ToString()).FontSize(8.5f).FontColor(TextDark);
                            });
                        });
                    });
                });
            };
        }

        private static Action<IContainer> KpiGrid(Dictionary<string, object> stats, AdvancedStats advanced, List<Trade> trades)
        {
            return c =>
            {
                var totalTrades = trades?.Count ?? 0;
                var winrate = TryDouble(stats, "Winrate");
                var profitFactor = TryDouble(stats, "Profit Factor");
                var expectancy = TryDouble(stats, "Expectancy");
                var netR = trades?.Sum(TradeR) ?? 0;
                var avgRR = trades != null && trades.Count > 0 ? trades.Average(t => (double)t.RR) : 0;
                var maxDrawdown = ComputeMaxDrawdown(trades ?? new List<Trade>());
                var bestTrade = trades != null && trades.Count > 0 ? trades.OrderByDescending(TradeR).FirstOrDefault() : null;
                var worstTrade = trades != null && trades.Count > 0 ? trades.OrderBy(TradeR).FirstOrDefault() : null;
                var bestPair = advanced?.PairStats != null && advanced.PairStats.Count > 0 ? advanced.PairStats.OrderByDescending(x => x.Value?.Expectancy ?? 0).FirstOrDefault() : default;

                c.Grid(grid =>
                {
                    grid.Columns(3);
                    grid.Spacing(8);

                    grid.Item().Element(MetricCard("TOTAL TRADES", totalTrades.ToString("N0", Fr), null, TextDark));
                    grid.Item().Element(MetricCard("WINRATE", FormatPercent(winrate), null, winrate >= 50 ? Green : Red));
                    grid.Item().Element(MetricCard("PROFIT FACTOR", profitFactor > 0 ? profitFactor.ToString("N2", Fr) : "—", null, profitFactor >= 1.2 ? Green : Red));
                    grid.Item().Element(MetricCard("EXPECTANCY", expectancy != 0 ? FormatR(expectancy) : "—", "/ trade", expectancy > 0 ? Green : Red));
                    grid.Item().Element(MetricCard("R NET", FormatR(netR), null, netR >= 0 ? Green : Red));
                    grid.Item().Element(MetricCard("R:R MOYEN", avgRR > 0 ? avgRR.ToString("N2", Fr) + " : 1" : "—", null, Blue));
                    grid.Item().Element(MetricCard("MAX DRAWDOWN", totalTrades > 0 ? "-" + maxDrawdown.ToString("N1", Fr) + "R" : "—", null, Red));
                    grid.Item().Element(MetricCard("MEILLEUR TRADE", bestTrade != null ? FormatR(TradeR(bestTrade)) : "—", bestTrade != null ? bestTrade.Paire : null, Green));
                    grid.Item().Element(MetricCard("PIRE TRADE", worstTrade != null ? FormatR(TradeR(worstTrade)) : "—", worstTrade != null ? worstTrade.Paire : null, Red));
                    grid.Item().Element(MetricCard("MEILLEURE PAIRE", bestPair.Value != null ? bestPair.Key : "—", bestPair.Value != null ? "Expectancy " + FormatR(bestPair.Value.Expectancy) : null, Purple));
                    grid.Item().Element(MetricCard("SESSION", GetMostActiveSession(advanced), null, Accent));
                    grid.Item().Element(MetricCard("STATUT", netR >= 0 ? "POSITIF" : "NÉGATIF", null, netR >= 0 ? Green : Red));
                });
            };
        }

        private static Action<IContainer> ReturnBreakdown(List<Trade> trades)
        {
            return c =>
            {
                c.Border(1).BorderColor(CardBorder).Padding(10).Column(col =>
                {
                    col.Item().Text("RÉPARTITION DES RÉSULTATS").FontSize(8).Bold().FontColor(TextDark);
                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Column(info =>
                        {
                            info.Item().Text("TP : " + (trades?.Count(x => x.Result == Resultat.TP) ?? 0)).FontColor(Green).Bold().FontSize(8.5f);
                            info.Item().PaddingTop(3).Text("SL : " + (trades?.Count(x => x.Result == Resultat.SL) ?? 0)).FontColor(Red).Bold().FontSize(8.5f);
                            info.Item().PaddingTop(3).Text("TR : " + (trades?.Count(x => x.Result == Resultat.TR) ?? 0)).FontColor(Amber).Bold().FontSize(8.5f);
                            info.Item().PaddingTop(3).Text("BE : " + (trades?.Count(x => x.Result == Resultat.BE) ?? 0)).FontColor(Gray).Bold().FontSize(8.5f);
                            info.Item().PaddingTop(3).Text("PARTIEL : " + (trades?.Count(x => x.Result == Resultat.PARTIAL) ?? 0)).FontColor(Blue).Bold().FontSize(8.5f);
                        });

                        row.RelativeItem().AlignRight().Column(summary =>
                        {
                            var net = trades?.Sum(TradeR) ?? 0;
                            summary.Item().Text("R NET CUMULÉ").FontSize(7.5f).Bold().FontColor(TextGray);
                            summary.Item().PaddingTop(2).Text(FormatR(net)).FontSize(16).Bold().FontColor(net >= 0 ? Green : Red);
                            summary.Item().PaddingTop(6).Text("Trades analysés : " + (trades?.Count ?? 0)).FontSize(8).FontColor(TextGray);
                        });
                    });
                });
            };
        }

        private static Action<IContainer> AdvancedBreakdown(Dictionary<string, object> stats, AdvancedStats advanced)
        {
            return c =>
            {
                c.Border(1).BorderColor(CardBorder).Padding(10).Column(col =>
                {
                    col.Item().Text("PERFORMANCE AVANCÉE").FontSize(8).Bold().FontColor(TextDark);

                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("JOUR DE LA SEMAINE").FontSize(7.5f).Bold().FontColor(TextGray);
                            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
                            {
                                var perf = advanced?.DayOfWeekStats != null && advanced.DayOfWeekStats.ContainsKey(day) ? advanced.DayOfWeekStats[day] : null;
                                left.Item().PaddingTop(4).Text(t =>
                                {
                                    t.Span(GetFrenchDay(day) + " : ").FontSize(8).Bold().FontColor(TextDark);
                                    t.Span(perf != null ? FormatR(perf.Expectancy) : "—").FontSize(8).FontColor(perf != null && perf.Expectancy >= 0 ? Green : Red);
                                });
                            }
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Item().Text("SESSION").FontSize(7.5f).Bold().FontColor(TextGray);
                            foreach (var session in new[] { "Tokyo", "Londres", "New York" })
                            {
                                var perf = advanced?.SessionStats != null && advanced.SessionStats.ContainsKey(session) ? advanced.SessionStats[session] : null;
                                right.Item().PaddingTop(4).Text(t =>
                                {
                                    t.Span(session + " : ").FontSize(8).Bold().FontColor(TextDark);
                                    t.Span(perf != null ? FormatR(perf.Expectancy) : "—").FontSize(8).FontColor(perf != null && perf.Expectancy >= 0 ? Green : Red);
                                });
                            }
                        });
                    });

                    if (advanced?.BestConfigs != null && advanced.BestConfigs.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("MEILLEURES SETUPS").FontSize(7.5f).Bold().FontColor(TextGray);
                        col.Item().PaddingTop(4).Table(tbl =>
                        {
                            tbl.ColumnsDefinition(cdef => { cdef.RelativeColumn(2); cdef.RelativeColumn(); cdef.RelativeColumn(); });
                            tbl.Header(h =>
                            {
                                h.Cell().Element(Th).Text("SETUP");
                                h.Cell().Element(Th).Text("TRADES");
                                h.Cell().Element(Th).Text("R");
                            });
                            foreach (var item in advanced.BestConfigs.Take(5))
                            {
                                tbl.Cell().Element(Td).Text(item.NomParametre ?? "—");
                                tbl.Cell().Element(Td).Text(item.NombreTrades.ToString("N0", Fr));
                                tbl.Cell().Element(Td).Text(item.Expectancy.ToString("N2", Fr) + "R");
                            }
                        });
                    }
                });
            };
        }

        private static Action<IContainer> TradeTable(List<Trade> trades)
        {
            return c =>
            {
                c.Border(1).BorderColor(CardBorder).Padding(8).Column(col =>
                {
                    col.Item().Text("HISTORIQUE DES TRADES").FontSize(8).Bold().FontColor(TextDark);

                    var items = trades ?? new List<Trade>();
                    col.Item().PaddingTop(8).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(def =>
                        {
                            def.RelativeColumn(1.2f);
                            def.RelativeColumn();
                            def.RelativeColumn();
                            def.RelativeColumn();
                            def.RelativeColumn();
                            def.RelativeColumn();
                            def.RelativeColumn();
                        });

                        tbl.Header(header =>
                        {
                            header.Cell().Element(Th).Text("PAIRE");
                            header.Cell().Element(Th).Text("RESULT");
                            header.Cell().Element(Th).Text("R:R");
                            header.Cell().Element(Th).Text("DATE");
                            header.Cell().Element(Th).Text("TYPE");
                            header.Cell().Element(Th).Text("PROFIT");
                            header.Cell().Element(Th).Text("SESSION");
                        });

                        foreach (var trade in items)
                        {
                            tbl.Cell().Element(Td).Text(trade.Paire ?? "—");
                            tbl.Cell().Element(Td).Text(ResultLabel(trade.Result));
                            tbl.Cell().Element(Td).Text(trade.RR.ToString("N2", Fr));
                            tbl.Cell().Element(Td).Text(trade.DateEntree.ToString("dd/MM/yy", Fr));
                            tbl.Cell().Element(Td).Text(trade.TypeOrdre.ToString());
                            tbl.Cell().Element(Td).Text(FormatR(TradeR(trade))).FontColor(TradeR(trade) >= 0 ? Green : Red);
                            tbl.Cell().Element(Td).Text(GetSessionLabel(trade.DateEntree.Hour));
                        }
                    });
                });
            };
        }

        private static Action<IContainer> MetricCard(string label, string value, string sub, string color)
        {
            return c => c.Border(1).BorderColor(CardBorder).Background("#FBFCFE").Padding(9).Column(col =>
            {
                col.Item().Text(label).FontSize(6.8f).Bold().FontColor(TextGray);
                col.Item().PaddingTop(2).Text(value).FontSize(13).Bold().FontColor(color);
                if (!string.IsNullOrWhiteSpace(sub))
                    col.Item().PaddingTop(2).Text(sub).FontSize(7).FontColor(Gray);
            });
        }

        private static IContainer Th(IContainer c) => c.Background(DarkSoft).Padding(4).BorderBottom(1).BorderColor(CardBorder);
        private static IContainer Td(IContainer c) => c.Padding(4).BorderBottom(0.5f).BorderColor("#E5E7EB");

        private static double TryDouble(Dictionary<string, object> stats, string key)
        {
            if (stats == null) return 0;
            if (!stats.TryGetValue(key, out var value) || value == null) return 0;
            var s = value.ToString().Replace("%", "").Trim().Replace(",", ".");
            double result;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static string FormatPercent(double value) => value.ToString("N1", Fr) + " %";
        private static string FormatR(double value) => (value >= 0 ? "+" : "") + value.ToString("N2", Fr) + "R";
        private static string ResultLabel(Resultat r)
        {
            switch (r)
            {
                case Resultat.TP: return "TP";
                case Resultat.SL: return "SL";
                case Resultat.TR: return "TR";
                case Resultat.BE: return "BE";
                case Resultat.PARTIAL: return "PARTIEL";
                default: return r.ToString();
            }
        }

        private static double TradeR(Trade trade)
        {
            if (trade == null) return 0;
            if (trade.Result == Resultat.TP) return trade.RR;
            if (trade.Result == Resultat.SL) return -1;
            return 0;
        }

        private static double ComputeMaxDrawdown(List<Trade> trades)
        {
            double cumulative = 0;
            double peak = 0;
            double maxDd = 0;

            foreach (var trade in trades)
            {
                cumulative += TradeR(trade);
                if (cumulative > peak) peak = cumulative;
                var dd = peak - cumulative;
                if (dd > maxDd) maxDd = dd;
            }

            return maxDd;
        }

        private static string GetSessionLabel(int hour)
        {
            if (hour >= 0 && hour < 8) return "TOKYO";
            if (hour >= 8 && hour < 13) return "LONDRES";
            if (hour >= 13 && hour < 20) return "NEW YORK";
            return "HORS SESSION";
        }

        private static string GetMostActiveSession(AdvancedStats advanced)
        {
            if (advanced?.SessionStats == null || advanced.SessionStats.Count == 0) return "—";

            var best = advanced.SessionStats.OrderByDescending(x => x.Value?.Expectancy ?? 0).FirstOrDefault();
            return best.Key != null ? best.Key.ToUpperInvariant() : "—";
        }

        private static string GetFrenchDay(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return "Lundi";
                case DayOfWeek.Tuesday: return "Mardi";
                case DayOfWeek.Wednesday: return "Mercredi";
                case DayOfWeek.Thursday: return "Jeudi";
                case DayOfWeek.Friday: return "Vendredi";
                case DayOfWeek.Saturday: return "Samedi";
                case DayOfWeek.Sunday: return "Dimanche";
                default: return day.ToString();
            }
        }

        private static string SanitizeFileName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = input.Trim();
            foreach (var ch in invalid)
                chars = chars.Replace(ch.ToString(), "_");
            chars = chars.Replace(" ", "_");
            chars = chars.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return string.IsNullOrWhiteSpace(chars) ? "strategie" : chars;
        }
    }
}