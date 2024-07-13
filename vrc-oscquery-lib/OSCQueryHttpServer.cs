using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VRC.OSCQuery
{
    public class OSCQueryHttpServer : IDisposable
    {
        private bool _shouldProcessHttp;

        // HTTP Middleware
        private readonly List<Func<HttpContext, Action, Task>> _preMiddleware;

        private readonly List<Func<HttpContext, Action, Task>>
            _middleware = []; // constructed here to ensure it exists even if empty

        private readonly List<Func<HttpContext, Action, Task>> _postMiddleware;

        private readonly ILogger<OSCQueryHttpServer> _logger;

        private readonly OSCQueryService _oscQuery;

        private readonly KestrelServer _kestrelServer;
        private readonly OSCQueryHttpApplication _oscQueryHttpApplication;

        public OSCQueryHttpServer(OSCQueryService oscQueryService, ILoggerFactory loggerFactory)
        {
            _oscQuery = oscQueryService;
            _logger = loggerFactory.CreateLogger<OSCQueryHttpServer>();

            _preMiddleware = new List<Func<HttpContext, Action, Task>>
            {
                HostInfoMiddleware
            };
            _postMiddleware = new List<Func<HttpContext, Action, Task>>
            {
                FaviconMiddleware,
                ExplorerMiddleware,
                RootNodeMiddleware
            };

            _shouldProcessHttp = true;

            var kestrelServerOptions = new KestrelServerOptions();

            if (_oscQuery.ListenAnyHost)
            {
                kestrelServerOptions.ListenAnyIP(_oscQuery.TcpPort);
            }
            else
            {
                _oscQuery.HostIP.ForEach(ip =>
                {
                    kestrelServerOptions.Listen(ip, _oscQuery.TcpPort);
                });
            }

            var transportOptions = new SocketTransportOptions();
            var transportFactory = new SocketTransportFactory(
                new OptionsWrapper<SocketTransportOptions>(transportOptions), loggerFactory);

            _kestrelServer = new KestrelServer(
                new OptionsWrapper<KestrelServerOptions>(kestrelServerOptions), transportFactory, loggerFactory);
            _oscQueryHttpApplication = new OSCQueryHttpApplication(HandleRequest);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await _kestrelServer.StartAsync(_oscQueryHttpApplication, cancellationToken);
        }

        private async Task HandleRequest(HttpContext httpContext)
        {
            foreach (var middleware in _preMiddleware)
            {
                var move = false;
                await middleware(httpContext, () => move = true);
                if (!move) return;
            }

            // User middleware
            foreach (var middleware in _middleware)
            {
                var move = false;
                await middleware(httpContext, () => move = true);
                if (!move) return;
            }

            // Post middleware
            foreach (var middleware in _postMiddleware)
            {
                var move = false;
                await middleware(httpContext, () => move = true);
                if (!move) return;
            }
        }

        public void AddMiddleware(Func<HttpContext, Action, Task> middleware)
        {
            _middleware.Add(middleware);
        }

        #region Middlewares

        private async Task HostInfoMiddleware(HttpContext context, Action next)
        {
            if (!context.Request.Query.ContainsKey(Attributes.HOST_INFO))
            {
                next();
                return;
            }

            try
            {
                var oscIp = context.Connection.RemoteIpAddress?.ToString() ?? _oscQuery.OscIP.ToString();
                var hostInfo = !_oscQuery.UseDynamicOscIp
                    ? _oscQuery.HostInfo
                    : new HostInfo
                    {
                        name = _oscQuery.HostInfo.name,
                        oscPort = _oscQuery.HostInfo.oscPort,
                        oscIP = oscIp,
                        oscTransport = _oscQuery.HostInfo.oscTransport,
                        extensions = _oscQuery.HostInfo.extensions
                    };

                // Serve Host Info for requests with "HOST_INFO" in them
                var hostInfoString = hostInfo.ToString();

                // Send Response
                context.Response.Headers.Pragma = "no-cache";
                context.Response.ContentType = "application/json";

                await using var streamWrite = new StreamWriter(context.Response.Body);

                await streamWrite.WriteAsync(hostInfoString);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not construct and send Host Info");
            }
        }

        private static string _pathToResources;

        private static string PathToResources
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_pathToResources))
                {
                    var dllLocation = Path.Combine(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    _pathToResources = Path.Combine(new DirectoryInfo(dllLocation).Parent?.FullName ?? string.Empty,
                        "Resources");
                }

                return _pathToResources;
            }
        }

        private async Task ExplorerMiddleware(HttpContext context, Action next)
        {
            if (!context.Request.Query.ContainsKey(Attributes.EXPLORER))
            {
                next();
                return;
            }

            var path = Path.Combine(PathToResources, "OSCQueryExplorer.html");
            if (!File.Exists(path))
            {
                _logger.LogError("Cannot find file at {Path} to serve", path);
                next();
                return;
            }

            await Extensions.ServeStaticFile(path, "text/html", context);
        }

        private async Task FaviconMiddleware(HttpContext context, Action next)
        {
            if (context.Request.Path.Value != null && !context.Request.Path.Value.EndsWith("/favicon.ico"))
            {
                next();
                return;
            }

            var path = Path.Combine(PathToResources, "favicon.ico");
            if (!File.Exists(path))
            {
                _logger.LogError("Cannot find file at {Path} to serve", path);
                next();
                return;
            }

            await Extensions.ServeStaticFile(path, "image/x-icon", context);
        }

        private async Task RootNodeMiddleware(HttpContext context, Action next)
        {
            var path = context.Request.Path.Value ?? "/";
            var matchedNode = _oscQuery.RootNode.GetNodeWithPath(path);

            await using var streamWriter = new StreamWriter(context.Response.Body);
            if (matchedNode == null)
            {
                const string err = "OSC Path not found";

                context.Response.StatusCode = (int)HttpStatusCode.NotFound;

                await streamWriter.WriteAsync(err);

                return;
            }

            var stringResponse = "";
            try
            {
                stringResponse = matchedNode.ToString();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not serialize node {MatchedNode}", matchedNode.FullPath);
            }

            // Send Response
            context.Response.Headers.Pragma = "no-cache";
            context.Response.ContentType = "application/json";

            await streamWriter.WriteAsync(stringResponse);
        }

        #endregion

        public void Dispose()
        {
            _shouldProcessHttp = false;

            GC.SuppressFinalize(this);
        }
    }
}
