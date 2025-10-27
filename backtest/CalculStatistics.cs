using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace backtest
{
    public class Statistics
    {
        public double TotalProfit { get; set; }
        public double TotalLoss { get; set; }
        public string BestPair { get; set; }
        public string WorstPair { get; set; }
        public double SuccessRateBuy { get; set; }
        public double SuccessRateSell { get; set; }
        public Dictionary<string, double> StrategyPerformance { get; set; }
        public Dictionary<DayOfWeek, double> WeeklyPerformance { get; set; }
        public Trade BestTrade { get; set; }

    }


}
