﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Prime.Common;

namespace Prime.TestConsole.Tools
{
    public class ProviderInfo
    {
        private List<Type> _supportedInterfaces = new List<Type>()
        {
            typeof(IPublicPriceStatistics),
            typeof(IDepositProvider),
            typeof(IOrderBookProvider),
            typeof(IBalanceProvider),
            typeof(IOhlcProvider),
            typeof(IPublicPricesProvider),
            typeof(IPublicPriceProvider),
            typeof(IAssetPairsProvider),
            typeof(IAssetPairVolumeProvider),
        };

        public ProviderInfo(INetworkProvider baseProvider)
        {
            NetworkProvider = baseProvider;
        }

        public void PrintReadableInfo()
        {
            var info = String.Empty;
            foreach (var type in _supportedInterfaces)
            {
                var sType = NetworkProvider.GetType();
                if (sType.GetInterfaces().Contains(type))
                    info += type.Name + " ";
            }

            Console.WriteLine($"'{NetworkProvider.Network.Name}': {info}");
        }

        public INetworkProvider NetworkProvider { get; set; }

        public bool ProviderSupports<TInterface>()
        {
            return NetworkProvider is TInterface;
        }
    }
}
