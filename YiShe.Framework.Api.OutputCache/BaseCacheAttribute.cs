using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Filters;

namespace YiShe.Framework.Api.OutputCache
{
    public abstract class BaseCacheAttribute : ActionFilterAttribute
    {
        // cache repository
        protected IApiOutputCache WebApiCache;

        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            WebApiCache = config.CacheOutputConfiguration().GetCacheOutputProvider(req);
        }
    }
}
