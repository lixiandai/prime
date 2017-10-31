﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Prime.Common;
using Prime.Utility;

namespace Prime.Core
{
    public class AssetPairDiscoveryProvider
    {
        private readonly AssetPairDiscoveryRequestMessage _requestMessage;
        private readonly AssetPair _pair;
        private const string IntermediariesCsv = "USD,BTC,EUR,LTC,USDT,XRP,ETH,ETC,BCC,BCH";
        private static readonly List<Asset> Intermediaries = IntermediariesCsv.ToCsv().Select(x => x.ToAssetRaw()).ToList();
        public bool IsFinished;
        private int _requests = 0;
        private readonly object _lock = new object();
        private AssetPairProviders _providers;

        internal AssetPairDiscoveryProvider(AssetPairDiscoveryRequestMessage requestMessage)
        {
            _requestMessage = requestMessage;
            _pair = requestMessage.Pair;
            new Task(Discover).Start();
        }

        internal void Discover()
        {
            if (_pair == null)
                return;

            Discover(_pair);

            lock (_lock)
                IsFinished = true;

            SendMessage();
        }

        internal void SendMessage()
        {
            lock (_lock)
            {
                if (!IsFinished)
                    return;
            }

            DefaultMessenger.I.Default.SendAsync(new AssetPairDiscoveryResultMessage(_requestMessage, _providers));
        }

        private void Discover(AssetPair pair)
        {
            _providers = DiscoverSpecified(pair) ??
                             Discover(pair, _requestMessage.ReversalEnabled) ??
                             DiscoverPegged(pair, _requestMessage.ReversalEnabled, _requestMessage.PeggedEnabled) ??
                             DiscoverIntermediary(pair);
        }

        private AssetPairProviders DiscoverSpecified(AssetPair pair)
        {
            if (_requestMessage.Network == null)
                return null;

            return DiscoverSpecified(pair, _requestMessage.Network, false) ?? (_requestMessage.ReversalEnabled ? DiscoverSpecified(pair.Reverse(), _requestMessage.Network, true) : null);
        }

        private static AssetPairProviders DiscoverSpecified(AssetPair pair, Network network, bool isReversed)
        {
            var provs = GetProviders(pair).Where(x => x.Network.Equals(network)).ToList();
            return provs.Any() ? new AssetPairProviders(pair, provs, isReversed) : null;
        }

        private static AssetPairProviders Discover(AssetPair pair, bool canReverse)
        {
            return DiscoverReversable(pair, false) ?? (canReverse ? DiscoverReversable(pair.Reverse(), true) : null);
        }

        private static AssetPairProviders DiscoverReversable(AssetPair pair, bool isReversed)
        {
            var provs = GetProviders(pair);
            return provs.Any() ? new AssetPairProviders(pair, provs, isReversed) : null;
        }

        private static AssetPairProviders DiscoverPegged(AssetPair pair, bool canReverse, bool canPeg)
        {
            if (!canPeg)
                return null;

            return DiscoverPeggedReversable(pair, false) ?? (canReverse ? DiscoverPeggedReversable(pair.Reverse(), true) : null);
        }

        private static AssetPairProviders DiscoverPeggedReversable(AssetPair pair, bool isReversed)
        {
            // Try alternate / pegged variation

            foreach (var ap in pair.PeggedPairs)
            {
                var provs = GetProviders(ap);
                if (provs.Any())
                    return new AssetPairProviders(ap, provs, isReversed) {IsPegged = true};
            }

            return null;
        }
        
        private AssetPairProviders DiscoverIntermediary(AssetPair pair)
        {
            if (!_requestMessage.ConversionEnabled)
                return null;

            return DiscoverIntermediaries(pair, _requestMessage.PeggedEnabled);
        }

        private static AssetPairProviders DiscoverIntermediaries(AssetPair pair, bool canPeg)
        {
            return Intermediaries.Select(intermediary => DiscoverFromIntermediary(pair, intermediary, canPeg)).FirstOrDefault(provs => provs != null);
        }

        private static AssetPairProviders DiscoverFromIntermediary(AssetPair originalPair, Asset intermediary, bool canPeg)
        {
            var pair = new AssetPair(originalPair.Asset1, intermediary);

            var provs1 = Discover(pair, true) ?? DiscoverPegged(pair, true, canPeg);

            if (provs1 == null)
                return null;

            //we have a possible path, find the other side.

            var npair1 = new AssetPair(originalPair.Asset2, intermediary);

            var provs2 = Discover(npair1, true) ?? DiscoverPegged(npair1, true, canPeg);

            if (provs2 == null)
                return null;

            provs1.Via = provs2;
            return provs1;
        }

        private static List<IPublicPriceProvider> GetProviders(AssetPair pair)
        {
            return AssetPairProvider.I.GetProvidersFromPrivate(pair).OfType<IPublicPriceProvider>().ToList();

            /*var apd = PublicContext.I.PubData.GetAggAssetPairData(pair);
            var provs = apd.Exchanges.Count == 0 ? new List<IPublicPriceProvider>() : apd.AllProviders.OfType<IPublicPriceProvider>().DistinctBy(x => x.Id).ToList();
            if (provs.Any())
                return provs;*/
        }
    }
}