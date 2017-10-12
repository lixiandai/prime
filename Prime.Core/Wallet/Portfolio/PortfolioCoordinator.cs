using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using System.Web.UI;
using System.Windows;
using System.Windows.Threading;
using Nito.AsyncEx;
using Prime.Core.Exchange.Rates;
using Prime.Utility;

namespace Prime.Core.Wallet
{
    public class PortfolioCoordinator : CoordinatorBase
    {
        public PortfolioCoordinator(UserContext context)
        {
            TimerInterval = 15000;
            Context = context;
            _scanners = new List<PortfolioProvider>();
            _messenger.Register<QuoteAssetChangedMessage>(this, BaseAssetChanged);
            _dispatcher = Dispatcher.CurrentDispatcher;
            _debouncer = new Debouncer(_dispatcher);
        }

        private readonly Debouncer _debouncer;
        private readonly Dispatcher _dispatcher;
        private readonly List<PortfolioProvider> _scanners;
        private readonly UniqueList<LatestPriceRequest> _requests = new UniqueList<LatestPriceRequest>();
        public readonly UserContext Context;
        private readonly object _lock = new object();

        public UniqueList<PortfolioLineItem> Items { get; } = new UniqueList<PortfolioLineItem>();
        public List<IWalletService> FailingProviders { get; } = new List<IWalletService>();
        public List<IWalletService> WorkingProviders { get; } = new List<IWalletService>();
        public List<IWalletService> QueryingProviders { get; } = new List<IWalletService>();
        public UniqueList<PortfolioInfoItem> PortfolioInfoItems { get; } = new UniqueList<PortfolioInfoItem>();
        public DateTime UtcLastUpdated { get; private set; }

        private void BaseAssetChanged(QuoteAssetChangedMessage m)
        {
            lock (_lock)
                Restart(Context);
        }

        protected override void OnSubscribersChanged()
        {
            if (SubscribersCount == 0)
                Stop();
            else
                Start(UserContext.Current);
        }

        protected override void OnStart(UserContext context)
        {
            lock (_lock)
            {
                var providers = Networks.I.WalletProviders.WithApi();
                _scanners.Clear();
                _scanners.AddRange(providers.Select(x => new PortfolioProvider(new PortfolioProviderContext(this.Context, x, context.QuoteAsset, TimerInterval))).ToList());
                _scanners.ForEach(x => x.OnChanged += ScannerChanged);
            }
        }

        protected override void OnStop()
        {
            lock (_lock)
            {
                _scanners.ForEach(delegate(PortfolioProvider x)
                {
                    x.OnChanged -= ScannerChanged;
                    x.Dispose();
                });

                Items.Clear();
                PortfolioInfoItems.Clear();
                FailingProviders.Clear();
                WorkingProviders.Clear();
                QueryingProviders.Clear();
                UtcLastUpdated = DateTime.MinValue;
                var lpc = LatestPriceCoordinator.I;

                foreach (var r in _requests)
                    lpc.RemoveRequest(this, r);

                SendChangedMessageDebounced();
            }
        }

        private void ScannerChanged(object sender, EventArgs e)
        {
            lock (_lock)
            {
                var ev = e as PortfolioProvider.PortfolioChangedLineEvent;
                var li = ev?.Item;
                var scanner = sender as PortfolioProvider;

                UpdateScanningStatuses();

                var prov = scanner.Provider;
                UtcLastUpdated = scanner.UtcLastUpdated;

                if (li != null)
                {
                    Items.RemoveAll(x => Equals(x.Network, prov.Network) && Equals(x.Asset, li.Asset));
                    Items.Add(li);
                }
                else
                {
                    Items.RemoveAll(x => Equals(x.Network, prov.Network));
                    foreach (var i in scanner.Items)
                        Items.Add(i);
                }

                PortfolioInfoItems.Add(scanner.Info, true);

                DoFinal();

                SendChangedMessageDebounced();
            }
        }

        private void SendChangedMessageDebounced()
        {
            _debouncer.Debounce(25, _ => SendChangedMessage());
        }

        private void SendChangedMessage()
        {
            _messenger.Send(new PortfolioChangedMessage());
        }

        private void DoFinal()
        {
            Items.RemoveAll(x => x.IsTotalLine);

            var lpc = LatestPriceCoordinator.I;
            var pairs = new List<AssetPair>();

            foreach (var i in Items)
            {
                if (Equals(i.Asset, UserContext.Current.QuoteAsset))
                {
                    i.Converted = i.Total;
                    continue;
                }

                var pair = new AssetPair(i.Asset, UserContext.Current.QuoteAsset);

                lpc.Messenger.Register<LatestPriceResult>(i, m =>
                {
                    if (!m.Pair.Equals(pair))
                        return;
                    if (DoConversion(i, m, pair))
                        SendChangedMessageDebounced();
                });

                var r = lpc.GetResult(pair);
                if (r != null)
                    DoConversion(i, r, pair);

                if (pairs.Contains(pair))
                    continue;

                pairs.Add(pair);
                _requests.Add(lpc.AddRequest(this, pair));
            }

            if (Items.Any())
                Items.Add(new PortfolioTotalLineItem() { Items = Items });
        }

        private static bool DoConversion(PortfolioLineItem i, LatestPriceResult m, AssetPair pair)
        {
            var mn = new Money((decimal) i.AvailableBalance * (decimal) m.Price, pair.Asset2);
            if (i.Converted != null && mn.ToDecimalValue() == i.Converted.Value.ToDecimalValue())
                return false;

            i.Converted = mn;
            i.ConversionFailed = false;
            return true;
        }

        private void UpdateScanningStatuses()
        {
            WorkingProviders.Clear();
            WorkingProviders.AddRange(_scanners.Where(x => x.IsConnected).Select(x => x.Provider));

            FailingProviders.Clear();
            FailingProviders.AddRange(_scanners.Where(x => x.IsFailing).Select(x => x.Provider));

            QueryingProviders.Clear();
            QueryingProviders.AddRange(_scanners.Where(x => x.IsQuerying).Select(x => x.Provider));
        }
    }
}