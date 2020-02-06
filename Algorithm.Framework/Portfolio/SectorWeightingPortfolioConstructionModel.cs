﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    /// <summary>
    /// Provides an implementation of <see cref="IPortfolioConstructionModel"/> that generates percent targets based on the
    /// <see cref="CompanyReference.IndustryTemplateCode"/>. 
    /// The target percent holdings of each sector is 1/S where S is the number of sectors and
    /// the target percent holdings of each security is 1/N where N is the number of securities of each sector.
    /// For insights of direction <see cref="InsightDirection.Up"/>, long targets are returned and for insights of direction
    /// <see cref="InsightDirection.Down"/>, short targets are returned.
    /// It will ignore <see cref="Insight"/> that have no <see cref="CompanyReference.IndustryTemplateCode"/> value.
    /// </summary>
    public class SectorWeightingPortfolioConstructionModel : EqualWeightingPortfolioConstructionModel
    {
        private readonly Dictionary<Symbol, string> _sectorCodeBySymbol = new Dictionary<Symbol, string>();

        /// <summary>
        /// Initialize a new instance of <see cref="SectorWeightingPortfolioConstructionModel"/>
        /// </summary>
        /// <param name="rebalancingFunc">For a given algorithm UTC DateTime returns the next expected rebalance time</param>
        public SectorWeightingPortfolioConstructionModel(Func<DateTime, DateTime> rebalancingFunc)
            : base(rebalancingFunc)
        {
        }

        /// <summary>
        /// Initialize a new instance of <see cref="SectorWeightingPortfolioConstructionModel"/>
        /// </summary>
        /// <param name="timeSpan">Rebalancing frequency</param>
        public SectorWeightingPortfolioConstructionModel(TimeSpan timeSpan)
            : base(timeSpan)
        {
        }

        /// <summary>
        /// Initialize a new instance of <see cref="SectorWeightingPortfolioConstructionModel"/>
        /// </summary>
        /// <param name="resolution">Rebalancing frequency</param>
        public SectorWeightingPortfolioConstructionModel(Resolution resolution = Resolution.Daily)
            : base(resolution)
        {
        }

        /// <summary>
        /// Method that will determine if the portfolio construction model should create a
        /// target for this insight
        /// </summary>
        /// <param name="insight">The insight to create a target for</param>
        /// <returns>True if the portfolio should create a target for the insight</returns>
        public override bool ShouldCreateTargetForInsight(Insight insight)
        {
            return _sectorCodeBySymbol.ContainsKey(insight.Symbol);
        }

        /// <summary>
        /// Will determine the target percent for each insight
        /// </summary>
        /// <param name="activeInsights">The active insights to generate a target for</param>
        /// <returns>A target percent for each insight</returns>
        public override Dictionary<Insight, double> DetermineTargetPercent(ICollection<Insight> activeInsights)
        {
            var result = new Dictionary<Insight, double>();

            var insightBySectorCode = new Dictionary<string, List<Insight>>();
            var sectorsCount = 0;

            foreach (var insight in activeInsights)
            {
                if (insight.Direction == InsightDirection.Flat)
                {
                    result[insight] = 0;
                    continue;
                }

                List<Insight> insights;
                var sectorCode = _sectorCodeBySymbol[insight.Symbol];
                if (insightBySectorCode.TryGetValue(sectorCode, out insights))
                {
                    insights.Add(insight);
                }
                else
                {
                    insightBySectorCode[sectorCode] = new List<Insight> { insight };
                    sectorsCount++;
                }
            }

            // give equal weighting to each sector
            var sectorPercent = sectorsCount == 0 ? 0 : 1m / sectorsCount;

            foreach (var kvp in insightBySectorCode)
            {
                var insights = kvp.Value;

                // give equal weighting to each security
                var count = insights.Count();
                var percent = count == 0 ? 0 : sectorPercent / count;

                foreach (var insight in insights)
                {
                    result[insight] = (double)((int)insight.Direction * percent);
                }
            }

            return result;
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var security in changes.RemovedSecurities)
            {
                // Removes the symbol from the _sectorCodeBySymbol dictionary
                // since we cannot emit PortfolioTarget for removed securities
                var symbol = security.Symbol;
                if (_sectorCodeBySymbol.ContainsKey(symbol))
                {
                    _sectorCodeBySymbol.Remove(symbol);
                }
            }

            foreach (var security in changes.AddedSecurities)
            {
                var sectorCode = GetSectorCode(security);
                if (!string.IsNullOrEmpty(sectorCode))
                {
                    _sectorCodeBySymbol[security.Symbol] = sectorCode;
                }
            }
            base.OnSecuritiesChanged(algorithm, changes);
        }

        /// <summary>
        /// Gets the sector code
        /// </summary>
        /// <param name="security">The security to create a sector code for</param>
        /// <returns>The value of the sector code for the security</returns>
        /// <remarks>Other sectors can be defined using <see cref="AssetClassification"/></remarks>
        protected virtual string GetSectorCode(Security security)
        {
            return security.Fundamentals?.CompanyReference?.IndustryTemplateCode;
        }
    }
}