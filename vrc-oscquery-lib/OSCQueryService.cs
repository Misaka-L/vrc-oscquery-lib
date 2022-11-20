using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VRC.OSCQuery
{
    public class OSCQueryService : IDisposable
    {
        #region Fluent Pattern Implementation

        public int TcpPort { get; set; } = DefaultPortHttp;
        public int OscPort { get; set; } = DefaultPortOsc;
        public string ServerName { get; set; } = DefaultServerName;
        public IPAddress HostIP { get; set; } = IPAddress.Loopback;
        
        public static ILogger<OSCQueryService> Logger { get; set; } = new NullLogger<OSCQueryService>();

        private HostInfo HostInfo
        {
            get
            {
                if (_hostInfo == null)
                {
                    BuildHostInfo();
                }
                return _hostInfo;
            }
        }

        private OSCQueryRootNode RootNode
        {
            get
            {
                if (_rootNode == null)
                {
                    BuildRootNode();
                }

                return _rootNode;
            }
        }

        private void BuildHostInfo()
        {
            // Create HostInfo object
            _hostInfo = new HostInfo()
            {
                name = ServerName,
                oscPort = OscPort,
                oscIP = IPAddress.Loopback.ToString()
            };
        }
        
        
        
        public void StartHttpServer()
        {
            // Create and start HTTPListener
            _listener = new HttpListener();

            string prefix = $"http://{HostIP}:{TcpPort}/";
            _listener.Prefixes.Add($"http://{HostIP}:{TcpPort}/");
            _preMiddleware = new List<Func<HttpListenerContext, Action, Task>>
            {
                HostInfoMiddleware
            };
            _postMiddleware = new List<Func<HttpListenerContext, Action, Task>>
            {
                FaviconMiddleware,
                ExplorerMiddleware,
                RootNodeMiddleware
            };
            _listener.Start();
            _listener.BeginGetContext(HttpListenerLoop, _listener);
            _shouldProcessHttp = true;
        }

        public void AddMiddleware(Func<HttpListenerContext, Action, Task> middleware)
        {
            _middleware.Add(middleware);
        }

        #endregion
        // Constants
        public const int DefaultPortHttp = 8080;
        public const int DefaultPortOsc = 9000;
        public const string DefaultServerName = "OSCQueryService";

        // Services
        public static readonly string _localOscUdpServiceName = $"{Attributes.SERVICE_OSC_UDP}.local";
        public static readonly string _localOscJsonServiceName = $"{Attributes.SERVICE_OSCJSON_TCP}.local";
        
        public static readonly HashSet<string> MatchedNames = new HashSet<string>() { 
            _localOscUdpServiceName, _localOscJsonServiceName
        };

        private MeaModDiscovery _discovery;

        #region Wrapped Calls for Discovery Service

        public event Action<OSCQueryServiceProfile> OnOscServiceAdded;
        public event Action<OSCQueryServiceProfile> OnOscQueryServiceAdded;
        public HashSet<OSCQueryServiceProfile> GetOSCQueryServices() => _discovery.GetOSCQueryServices();
        public HashSet<OSCQueryServiceProfile> GetOSCServices() => _discovery.GetOSCServices();

        #endregion

        // HTTP Server
        private HttpListener _listener;
        private bool _shouldProcessHttp;
        
        // HTTP Middleware
        private List<Func<HttpListenerContext, Action, Task>> _preMiddleware;
        private List<Func<HttpListenerContext, Action, Task>> _middleware = new List<Func<HttpListenerContext, Action, Task>>(); // constructed here to ensure it exists even if empty
        private List<Func<HttpListenerContext, Action, Task>> _postMiddleware;
        
        // Misc
        private OSCQueryRootNode _rootNode;
        private HostInfo _hostInfo;
        
        /// <summary>
        /// Creates an OSCQueryService which can track OSC endpoints in the enclosing program as well as find other OSCQuery-compatible services on the link-local network
        /// </summary>
        /// <param name="serverName">Server name to use, default is "OSCQueryService"</param>
        /// <param name="httpPort">TCP port on which to serve OSCQuery info, default is 8080</param>
        /// <param name="oscPort">UDP Port at which the OSC Server can be reached, default is 9000</param>
        /// <param name="logger">Optional logger which will be used for logs generated within this class. Will log to Null if not set.</param>
        /// <param name="middleware">Optional set of middleware to be injected into the HTTP server. Middleware will be executed in the order they are passed in.</param>
        [Obsolete("Use the Fluent Interface so we can remove this constructor", false)]
        public OSCQueryService(string serverName = DefaultServerName, int httpPort = DefaultPortHttp, int oscPort = DefaultPortOsc, ILogger<OSCQueryService> logger = null, params Func<HttpListenerContext, Action, Task>[] middleware)
        {
            if (logger != null) Logger = logger;
            
            Initialize(serverName);
            StartOSCQueryService(serverName, httpPort, middleware);
            if (oscPort > 0)
            {
                AdvertiseOSCService(serverName, oscPort);
            }
            RefreshServices();
        }

        [Obsolete("Use the Fluent Interface so we can remove this function", false)]
        public void Initialize(string serverName = DefaultServerName)
        {
            // Create HostInfo object
            _hostInfo = new HostInfo()
            {
                name = serverName,
            };
            
            // Pass along events from Discovery
            _discovery = new MeaModDiscovery(Logger);
            _discovery.OnOscQueryServiceAdded += profile => OnOscQueryServiceAdded?.Invoke(profile);
            _discovery.OnOscServiceAdded += profile => OnOscServiceAdded?.Invoke(profile);
        }
        
        [Obsolete("Use the Fluent Interface instead of this combo function", false)]
        public void StartOSCQueryService(string serverName, int httpPort = -1, params Func<HttpListenerContext, Action, Task>[] middleware)
        {
            BuildRootNode();
            ServerName = serverName;
            
            // Use the provided port or grab a new one
            httpPort = httpPort == -1 ? Extensions.GetAvailableTcpPort() : httpPort;

            // Add all provided middleware
            if (middleware != null)
            {
                foreach (var newMiddleware in middleware)
                {
                    AddMiddleware(newMiddleware);
                }
            }
            
            AdvertiseOSCQueryService(serverName, httpPort);
            StartHttpServer();
        }
        
        public void AdvertiseOSCQueryService(string serviceName, int port = DefaultPortHttp)
        {
            _discovery.Advertise(new OSCQueryServiceProfile(serviceName, HostIP, port, OSCQueryServiceProfile.ServiceType.OSCQuery));
        }

        public void AdvertiseOSCService(string serviceName, int port = DefaultPortOsc)
        {
            _hostInfo.oscPort = port;
            _discovery.Advertise(new OSCQueryServiceProfile(serviceName, HostIP, port, OSCQueryServiceProfile.ServiceType.OSC));
        }

        public void RefreshServices()
        {
            _discovery.RefreshServices();
        }
        
        public void SetValue(string address, string value)
        {
            var target = RootNode.GetNodeWithPath(address);
            if (target == null)
            {
                // add this node
                target = RootNode.AddNode(new OSCQueryNode(address));
            }
            
            target.Value = value;
        }

        /// <summary>
        /// Process and responds to incoming HTTP queries
        /// </summary>
        private void HttpListenerLoop(IAsyncResult result)
        {
            if (!_shouldProcessHttp) return;
            
            var context = _listener.EndGetContext(result);
            _listener.BeginGetContext(HttpListenerLoop, _listener);
            Task.Run(async () =>
            {
                // Pre middleware
                foreach (var middleware in _preMiddleware)
                {
                    var move = false;
                    await middleware(context, () => move = true);
                    if (!move) return;
                }
                
                // User middleware
                foreach (var middleware in _middleware)
                {
                    var move = false;
                    await middleware(context, () => move = true);
                    if (!move) return;
                }
                
                // Post middleware
                foreach (var middleware in _postMiddleware)
                {
                    var move = false;
                    await middleware(context, () => move = true);
                    if (!move) return;
                }
            }).ConfigureAwait(false);
        }

        private async Task HostInfoMiddleware(HttpListenerContext context, Action next)
        {
            if (!context.Request.RawUrl.Contains(Attributes.HOST_INFO))
            {
                next();
                return;
            }
            
            try
            {
                // Serve Host Info for requests with "HOST_INFO" in them
                var hostInfoString = HostInfo.ToString();
                        
                // Send Response
                context.Response.Headers.Add("pragma:no-cache");
                
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = hostInfoString.Length;
                using (var sw = new StreamWriter(context.Response.OutputStream))
                {
                    await sw.WriteAsync(hostInfoString);
                    await sw.FlushAsync();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Could not construct and send Host Info: {e.Message}");
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
                    _pathToResources = Path.Combine(new DirectoryInfo(dllLocation).Parent?.FullName ?? string.Empty, "Resources");
                }
                return _pathToResources;
            }
        }
        private async Task ExplorerMiddleware(HttpListenerContext context, Action next)
        {
            if (!context.Request.Url.Query.Contains(Attributes.EXPLORER))
            {
                next();
                return;
            }

            var path = Path.Combine(PathToResources, "OSCQueryExplorer.html");
            if (!File.Exists(path))
            {
                Logger.LogError($"Cannot find file at {path} to serve.");
                next();
                return;
            }
            await Extensions.ServeStaticFile(path, "text/html", context);
        }

        private async Task FaviconMiddleware(HttpListenerContext context, Action next)
        {
            if (!context.Request.RawUrl.Contains("favicon.ico"))
            {
                next();
                return;
            }
            
            var path = Path.Combine(PathToResources, "favicon.ico");
            if (!File.Exists(path))
            {
                Logger.LogError($"Cannot find file at {path} to serve.");
                next();
                return;
            }
            
            await Extensions.ServeStaticFile(path, "image/x-icon", context);
        }

        private async Task RootNodeMiddleware(HttpListenerContext context, Action next)
        {
            var path = context.Request.Url.LocalPath;
            var matchedNode = RootNode.GetNodeWithPath(path);
            if (matchedNode == null)
            {
                const string err = "OSC Path not found";

                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.ContentLength64 = err.Length;
                using (var sw = new StreamWriter(context.Response.OutputStream))
                {
                    await sw.WriteAsync(err);
                    await sw.FlushAsync();
                }

                return;
            }

            var stringResponse = matchedNode.ToString();
                    
            // Send Response
            context.Response.Headers.Add("pragma:no-cache");
                
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = stringResponse.Length;
            using (var sw = new StreamWriter(context.Response.OutputStream))
            {
                await sw.WriteAsync(stringResponse);
                await sw.FlushAsync();
            }
        }

        /// <summary>
        /// Registers the info for an OSC path.
        /// </summary>
        /// <param name="path">Full OSC path to entry</param>
        /// <param name="oscTypeString">String representation of OSC type(s)</param>
        /// <param name="accessValues">Enum - 0: NoValue, 1: ReadOnly 2:WriteOnly 3:ReadWrite</param>
        /// <param name="initialValue">Starting value for param in string form</param>
        /// <param name="description">Optional longer string to use when displaying a label for the entry</param>
        /// <returns></returns>
        public bool AddEndpoint(string path, string oscTypeString, Attributes.AccessValues accessValues, string initialValue = null,
            string description = "")
        {
            // Exit early if path does not start with slash
            if (!path.StartsWith("/"))
            {
                Logger.LogError($"An OSC path must start with a '/', your path {path} does not.");
                return false;
            }
            
            if (RootNode.GetNodeWithPath(path) != null)
            {
                Logger.LogWarning($"Path already exists, skipping: {path}");
                return false;
            }
            
            RootNode.AddNode(new OSCQueryNode(path)
            {
                Access = accessValues,
                Description = description,
                OscType = oscTypeString,
                Value = initialValue
            });
            
            return true;
        }
        
        public bool AddEndpoint<T>(string path, Attributes.AccessValues accessValues, string initialValue = null, string description = "")
        {
            var typeExists = Attributes.OSCTypeFor(typeof(T), out string oscType);

            if (typeExists) return AddEndpoint(path, oscType, accessValues, initialValue, description);
            
            Logger.LogError($"Could not add {path} to OSCQueryService because type {typeof(T)} is not supported.");
            return false;
        }

        /// <summary>
        /// Removes the data for a given OSC path, including its value getter if it has one
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool RemoveEndpoint(string path)
        {
            // Exit early if no matching path is found
            if (RootNode.GetNodeWithPath(path) == null)
            {
                Logger.LogWarning($"No endpoint found for {path}");
                return false;
            }

            RootNode.RemoveNode(path);

            return true;
        }
        
        /// <summary>
        /// Constructs the response the server will use for HOST_INFO queries
        /// </summary>
        private void BuildRootNode()
        {
            _rootNode = new OSCQueryRootNode()
            {
                Access = Attributes.AccessValues.NoValue,
                Description = "root node",
                FullPath = "/",
            };
        }

        public void Dispose()
        {
            _shouldProcessHttp = false;
            
            // HttpListener teardown
            if (_listener != null)
            {
                if (_listener.IsListening)
                    _listener.Stop();
                
                _listener.Close();
            }
            
            // Service Teardown
            _discovery?.Dispose();

            GC.SuppressFinalize(this);
        }

        ~OSCQueryService()
        {
           Dispose();
        }
    }

}