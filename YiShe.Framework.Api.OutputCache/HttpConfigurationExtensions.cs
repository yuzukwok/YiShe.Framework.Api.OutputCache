using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace YiShe.Framework.Api.OutputCache
{
    public static class HttpConfigurationExtensions
    {
        public static CacheOutputConfiguration CacheOutputConfiguration(this HttpConfiguration config)
        {
            return new CacheOutputConfiguration(config);
        }
    }
}
