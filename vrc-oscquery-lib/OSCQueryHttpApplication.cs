using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace VRC.OSCQuery;

public class Context(IFeatureCollection features)
{
    public readonly IFeatureCollection Features = features;
}

public class OSCQueryHttpApplication(Func<HttpContext, Task> handleRequest) : IHttpApplication<Context>
{
    public Context CreateContext(IFeatureCollection contextFeatures)
    {
        return new Context(contextFeatures);
    }

    public async Task ProcessRequestAsync(Context context)
    {
        await handleRequest(new DefaultHttpContext(context.Features));
    }

    public void DisposeContext(Context context, Exception exception)
    {
    }
}
