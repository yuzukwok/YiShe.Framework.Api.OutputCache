using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using YiShe.Framework.Api.OutputCache.Logging;

namespace YiShe.Framework.Api.OutputCache
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class InvalidateCacheOutputAttribute : BaseCacheAttribute
    {
        private string _controller;
        private readonly string _methodName;
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        public InvalidateCacheOutputAttribute(string methodName)
            : this(methodName, null)
        {
        }

        public InvalidateCacheOutputAttribute(string methodName, Type type = null)
        {
            _controller = type != null ? type.FullName : null;
            _methodName = methodName;
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
           
            if (actionExecutedContext.Response != null && !actionExecutedContext.Response.IsSuccessStatusCode) return;
            _controller = _controller ?? actionExecutedContext.ActionContext.ControllerContext.ControllerDescriptor.ControllerType.FullName;

            var config = actionExecutedContext.Request.GetConfiguration();
            EnsureCache(config, actionExecutedContext.Request);

            var key = actionExecutedContext.Request.GetConfiguration().CacheOutputConfiguration().MakeBaseCachekey(_controller, _methodName,Thread.CurrentPrincipal.Identity.Name);
          
            if (WebApiCache.Contains(key))
            {
                Logger.Debug("删除缓存key完成" + key);
                WebApiCache.RemoveStartsWith(key);
            }
        }
    }
}
