﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prime.Common
{
    public interface IDepositProvider : IDescribesAssets
    {
        bool CanGenerateDepositAddress { get; }

        bool CanPeekDepositAddress { get; }

        Task<WalletAddresses> GetAddressesForAssetAsync(WalletAddressAssetContext context);

        Task<WalletAddresses> GetAddressesAsync(WalletAddressContext context);

        Task<List<Asset>> GetSuspendedDepositAssetsAsync();
    }
}