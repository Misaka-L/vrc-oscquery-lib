using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace VRC.OSCQuery
{
    public class OSCQueryServiceBuilder
    {
        private readonly OSCQueryService _service = new ();
        public OSCQueryService Build()
        {
            if (!_customStartup)
            {
                WithDefaults();
            }
            return _service;
        }

        // flag to know whether the user has set something custom
        private bool _customStartup = false;

        /// <summary>
        /// Starts HTTP Server, Advertises OSCQuery & OSC, Uses default library for Network Discovery
        /// </summary>
        /// <returns>OSCQueryServiceBuilder for Fluent construction</returns>
        public OSCQueryServiceBuilder WithDefaults()
        {
            _customStartup = true;
            AddHttpServer();
            WithDiscovery(new MeaModDiscovery());
            AdvertiseOSCQuery();
            AdvertiseOSC();
            return this;
        }

        public OSCQueryServiceBuilder WithTcpPort(int port)
        {
            _customStartup = true;
            _service.TcpPort = port;
            return this;
        }

        public OSCQueryServiceBuilder WithUdpPort(int port)
        {
            _customStartup = true;
            _service.OscPort = port;
            return this;
        }

        public OSCQueryServiceBuilder WithHostIP(IPAddress address)
        {
            _customStartup = true;
            if (!_service.HostIP.Contains(address))
                _service.HostIP.Add(address);

            return this;
        }

        public OSCQueryServiceBuilder WithHostIPs(IEnumerable<IPAddress> address)
        {
            _customStartup = true;
            _service.HostIP.AddRange(address.Distinct().Where(ip => !_service.HostIP.Exists(hostIp => Equals(hostIp, ip))));

            return this;
        }

        public OSCQueryServiceBuilder WithOscIP(IPAddress address)
        {
            _customStartup = true;
            if (!_service.OscIP.Contains(address))
                _service.OscIP.Add(address);

            return this;
        }

        public OSCQueryServiceBuilder WithOscIPs(IEnumerable<IPAddress> addresses)
        {
            _customStartup = true;
            _service.OscIP.AddRange(addresses);

            return this;
        }

        public OSCQueryServiceBuilder WithDynamicOscIp(bool useDynamicOscIp = true)
        {
            _customStartup = true;
            _service.UseDynamicOscIp = useDynamicOscIp;
            return this;
        }

        public OSCQueryServiceBuilder WithListenAnyHost(bool listenAnyHost = true)
        {
            _customStartup = true;
            _service.ListenAnyHost = listenAnyHost;
            return this;
        }

        public OSCQueryServiceBuilder AddHttpServer(ILoggerFactory loggerFactory = null)
        {
            _customStartup = true;
            _service.UseHttpServer(loggerFactory);
            return this;
        }

        public OSCQueryServiceBuilder WithServiceName(string name)
        {
            _customStartup = true;
            _service.ServerName = name;
            return this;
        }

        public OSCQueryServiceBuilder WithLogger(ILogger<OSCQueryService> logger)
        {
            _customStartup = true;
            OSCQueryService.Logger = logger;
            return this;
        }

        public OSCQueryServiceBuilder WithMiddleware(Func<HttpContext, Action, Task> middleware)
        {
            _customStartup = true;
            _service.AddMiddleware(middleware);
            return this;
        }

        public OSCQueryServiceBuilder WithDiscovery(IDiscovery d)
        {
            _customStartup = true;
            _service.SetDiscovery(d);
            return this;
        }

        public OSCQueryServiceBuilder AddListenerForServiceType(Action<OSCQueryServiceProfile> listener, OSCQueryServiceProfile.ProfileServiceType type)
        {
            _customStartup = true;
            switch (type)
            {
                case OSCQueryServiceProfile.ProfileServiceType.OSC:
                    _service.OnOscServiceAdded += listener;
                    break;
                case OSCQueryServiceProfile.ProfileServiceType.OSCQuery:
                    _service.OnOscQueryServiceAdded += listener;
                    break;
            }
            return this;
        }

        public OSCQueryServiceBuilder AdvertiseOSC()
        {
            _customStartup = true;
            _service.AdvertiseOSCService(_service.ServerName, _service.OscPort);
            return this;
        }

        public OSCQueryServiceBuilder AdvertiseOSCQuery()
        {
            _customStartup = true;
            _service.AdvertiseOSCQueryService(_service.ServerName, _service.TcpPort);
            return this;
        }
    }
}
