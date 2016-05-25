using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace YiShe.Framework.Api.OutputCache
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CacheOutputAttribute : ActionFilterAttribute
    {
        private const string CurrentRequestMediaType = "CacheOutput:CurrentRequestMediaType";
        protected static MediaTypeHeaderValue DefaultMediaType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.HeaderName };

        /// <summary>
        /// Cache enabled only for requests when Thread.CurrentPrincipal is not set
        /// </summary>
        public bool AnonymousOnly { get; set; }

        /// <summary>
        /// Corresponds to MustRevalidate HTTP header - indicates whether the origin server requires revalidation of a cache entry on any subsequent use when the cache entry becomes stale
        /// </summary>
        public bool MustRevalidate { get; set; }
        /// <summary>
        /// 默认的分页参数
        /// </summary>
        public string PageIndexArgName { get; set; } = "PageIndex";

        /// <summary>
        /// Do not vary cache by querystring values
        /// </summary>
        public bool ExcludeQueryStringFromCacheKey { get; set; }

        /// <summary>
        /// How long response should be cached on the server side (in seconds)
        /// </summary>
        public int ServerTimeSpan { get; set; }

        /// <summary>
        /// Corresponds to CacheControl MaxAge HTTP header (in seconds)
        /// </summary>
        public int ClientTimeSpan { get; set; }

        /// <summary>
        /// Corresponds to CacheControl NoCache HTTP header
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// Corresponds to CacheControl Private HTTP header. Response can be cached by browser but not by intermediary cache
        /// </summary>
        public bool Private { get; set; }

        /// <summary>
        /// Class used to generate caching keys
        /// </summary>
        public Type CacheKeyGenerator { get; set; }
        /// <summary>
        /// 是否预加载下一页
        /// </summary>
        public bool PreloadNextPage { get; set; }

        /// <summary>
        /// Cache配置的配置文件
        /// </summary>
        public string CacheProfile { get; set; }

        // cache repository
        private IApiOutputCache _webApiCache;

        private ProfileElement getWebApiCacheProfile()
        {
            var webApiCacheConfig = WebApiCacheConfigSection.GetConfig();
            ProfileElement webApiProfileElement = null;
            if (webApiCacheConfig != null && webApiCacheConfig.Profiles != null && webApiCacheConfig.Profiles.Count > 0)
            {
                webApiProfileElement = webApiCacheConfig.Profiles[CacheProfile];
            }
            return webApiProfileElement;
        }

        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            _webApiCache = config.CacheOutputConfiguration().GetCacheOutputProvider(req);
        }

        internal IModelQuery<DateTime, CacheTime> CacheTimeQuery;

        protected virtual bool IsCachingAllowed(HttpActionContext actionContext, bool anonymousOnly)
        {
            //缓存匿名
            if (anonymousOnly)
            {
                if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                {
                    return false;
                }
            }
            //是否忽略标签
            if (actionContext.ActionDescriptor.GetCustomAttributes<IgnoreCacheOutputAttribute>().Any())
            {
                return false;
            }
            //是否GET
            return actionContext.Request.Method == HttpMethod.Get;
        }

        protected virtual bool PreloadAllowed(HttpActionContext actionContext)
        {
            //假设当前参数包含PageIndex，Header头不包含Preload的请求
            //触发下一页的请求
            if (!PreloadNextPage)
            {
                return false;
            }
            if (ExcludeQueryStringFromCacheKey)
            {
                return false;
            }

            //
            if (!actionContext.ActionArguments.ContainsKey("PageIndex")
                 || actionContext.Request.Headers.Contains("Preload"))
            {
                return false;
            }

            return true;

        }

        protected virtual void EnsureCacheTimeQuery()
        {
            if (CacheTimeQuery == null) ResetCacheTimeQuery();
        }

        protected void ResetCacheTimeQuery()
        {
            CacheTimeQuery = new ShortTime(ServerTimeSpan, ClientTimeSpan);
        }

        protected virtual MediaTypeHeaderValue GetExpectedMediaType(HttpConfiguration config, HttpActionContext actionContext)
        {
            MediaTypeHeaderValue responseMediaType = null;

            var negotiator = config.Services.GetService(typeof(IContentNegotiator)) as IContentNegotiator;
            var returnType = actionContext.ActionDescriptor.ReturnType;

            if (negotiator != null && returnType != typeof(HttpResponseMessage) && (returnType != typeof(IHttpActionResult) || typeof(IHttpActionResult).IsAssignableFrom(returnType)))
            {
                var negotiatedResult = negotiator.Negotiate(returnType, actionContext.Request, config.Formatters);

                if (negotiatedResult == null)
                {
                    return DefaultMediaType;
                }

                responseMediaType = negotiatedResult.MediaType;
                if (string.IsNullOrWhiteSpace(responseMediaType.CharSet))
                {
                    responseMediaType.CharSet = Encoding.UTF8.HeaderName;
                }
            }
            else
            {
                if (actionContext.Request.Headers.Accept != null)
                {
                    responseMediaType = actionContext.Request.Headers.Accept.FirstOrDefault();
                    if (responseMediaType == null || !config.Formatters.Any(x => x.SupportedMediaTypes.Contains(responseMediaType)))
                    {
                        return DefaultMediaType;
                    }
                }
            }

            return responseMediaType;
        }

        public async override Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            if (actionContext == null) throw new ArgumentNullException("actionContext");

            ProfileElement webApiProfileElement = null;
            webApiProfileElement = getWebApiCacheProfile();
            if (webApiProfileElement != null && webApiProfileElement.Enable)
            {
                this.ClientTimeSpan = webApiProfileElement.ClientTimeSpan;
                this.ServerTimeSpan = webApiProfileElement.ServerTimeSpan;
                this.MustRevalidate = webApiProfileElement.MustRevalidate;
                this.ExcludeQueryStringFromCacheKey = webApiProfileElement.ExcludeQueryStringFromCacheKey;
                this.AnonymousOnly = webApiProfileElement.AnonymousOnly;
            }
            //是否允许缓存
            if (!IsCachingAllowed(actionContext, AnonymousOnly)) return;

            var config = actionContext.Request.GetConfiguration();

            EnsureCacheTimeQuery();
            EnsureCache(config, actionContext.Request);
            //Key
            var cacheKeyGenerator = config.CacheOutputConfiguration().GetCacheKeyGenerator(actionContext.Request, CacheKeyGenerator);
            //返回值类型
            var responseMediaType = GetExpectedMediaType(config, actionContext);
            actionContext.Request.Properties[CurrentRequestMediaType] = responseMediaType;
            //生成Key
            var cachekey = cacheKeyGenerator.MakeCacheKey(actionContext, responseMediaType, ExcludeQueryStringFromCacheKey);

            if (!_webApiCache.Contains(cachekey))
            {                
                return;

            }
            //ETag判断
            if (actionContext.Request.Headers.IfNoneMatch != null)
            {
                var etag = _webApiCache.Get<string>(cachekey + Constants.EtagKey);
                if (etag != null)
                {
                    if (actionContext.Request.Headers.IfNoneMatch.Any(x => x.Tag == etag))
                    {
                        var time = CacheTimeQuery.Execute(DateTime.Now);
                        var quickResponse = actionContext.Request.CreateResponse(HttpStatusCode.NotModified);
                        ApplyCacheHeaders(quickResponse, time);
                        //输出
                        actionContext.Response = quickResponse;
                        return;
                    }
                }
            }
            //直接从缓存返回
            var val = _webApiCache.Get<byte[]>(cachekey);
            if (val == null) return;

            var contenttype = _webApiCache.Get<MediaTypeHeaderValue>(cachekey + Constants.ContentTypeKey) ?? new MediaTypeHeaderValue(cachekey.Split(new[] { ':' }, 2)[1].Split(';')[0]);

            actionContext.Response = actionContext.Request.CreateResponse();
            actionContext.Response.Content = new ByteArrayContent(val);

            actionContext.Response.Content.Headers.ContentType = contenttype;
            var responseEtag = _webApiCache.Get<string>(cachekey + Constants.EtagKey);
            if (responseEtag != null) SetEtag(actionContext.Response, responseEtag);

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            ApplyCacheHeaders(actionContext.Response, cacheTime);

            //判断是否需要预读
            if (PreloadAllowed(actionContext))
            {
                await PreloadNextPageByHttpClient(actionContext);
            }
        }

        public override async Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            //执行成功判断
            if (actionExecutedContext.ActionContext.Response == null || !actionExecutedContext.ActionContext.Response.IsSuccessStatusCode) return;
            //是否允许缓存
            if (!IsCachingAllowed(actionExecutedContext.ActionContext, AnonymousOnly)) return;
            //生成绝对过期时间
            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            if (cacheTime.AbsoluteExpiration > DateTime.Now)
            {
                var httpConfig = actionExecutedContext.Request.GetConfiguration();
                var config = httpConfig.CacheOutputConfiguration();
                var cacheKeyGenerator = config.GetCacheKeyGenerator(actionExecutedContext.Request, CacheKeyGenerator);

                var responseMediaType = actionExecutedContext.Request.Properties[CurrentRequestMediaType] as MediaTypeHeaderValue ?? GetExpectedMediaType(httpConfig, actionExecutedContext.ActionContext);
                //生成Key
                var cachekey = cacheKeyGenerator.MakeCacheKey(actionExecutedContext.ActionContext, responseMediaType, ExcludeQueryStringFromCacheKey);

                if (!string.IsNullOrWhiteSpace(cachekey) && !(_webApiCache.Contains(cachekey)))
                {
                    //设置Etag
                    SetEtag(actionExecutedContext.Response, CreateEtag(actionExecutedContext, cachekey, cacheTime));

                    var responseContent = actionExecutedContext.Response.Content;
                    //缓存值
                    if (responseContent != null)
                    {
                        //preload next page
                        if (PreloadAllowed(actionExecutedContext.ActionContext))
                        {
                            await PreloadNextPageByHttpClient(actionExecutedContext.ActionContext);

                        }


                        var baseKey = config.MakeBaseCachekey(actionExecutedContext.ActionContext.ControllerContext.ControllerDescriptor.ControllerType.FullName, actionExecutedContext.ActionContext.ActionDescriptor.ActionName,Thread.CurrentPrincipal.Identity.Name);
                        var contentType = responseContent.Headers.ContentType;
                        string etag = actionExecutedContext.Response.Headers.ETag.Tag;
                        //ConfigureAwait false to avoid deadlocks
                        var content = await responseContent.ReadAsByteArrayAsync().ConfigureAwait(false);

                        responseContent.Headers.Remove("Content-Length");

                        _webApiCache.Add(baseKey, string.Empty, cacheTime.AbsoluteExpiration);
                        _webApiCache.Add(cachekey, content, cacheTime.AbsoluteExpiration, baseKey);


                        _webApiCache.Add(cachekey + Constants.ContentTypeKey,
                                        contentType,
                                        cacheTime.AbsoluteExpiration, baseKey);


                        _webApiCache.Add(cachekey + Constants.EtagKey,
                                        etag,
                                        cacheTime.AbsoluteExpiration, baseKey);
                    }
                }
            }

            ApplyCacheHeaders(actionExecutedContext.ActionContext.Response, cacheTime);
        }

        private async Task PreloadNextPageByHttpClient(HttpActionContext actionContext)
        {
            var url = actionContext.Request.RequestUri.ToString();
            //替换
            var index = int.Parse(actionContext.ActionArguments[PageIndexArgName].ToString());
            url = url.Replace(PageIndexArgName + "=" + index, PageIndexArgName + "= " + (index + 1));
           
            await Task.Factory.StartNew(() =>
            {
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("Preload", "api cache");
                client.GetAsync(url);
            });
        }

        protected virtual void ApplyCacheHeaders(HttpResponseMessage response, CacheTime cacheTime)
        {
            if (cacheTime.ClientTimeSpan > TimeSpan.Zero || MustRevalidate || Private)
            {
                var cachecontrol = new CacheControlHeaderValue
                {
                    MaxAge = cacheTime.ClientTimeSpan,
                    MustRevalidate = MustRevalidate,
                    Private = Private
                };

                response.Headers.CacheControl = cachecontrol;
            }
            else if (NoCache)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                response.Headers.Add("Pragma", "no-cache");
            }
        }

        protected virtual string CreateEtag(HttpActionExecutedContext actionExecutedContext, string cachekey, CacheTime cacheTime)
        {
            return Guid.NewGuid().ToString();
        }

        private static void SetEtag(HttpResponseMessage message, string etag)
        {
            if (etag != null)
            {
                var eTag = new EntityTagHeaderValue(@"""" + etag.Replace("\"", string.Empty) + @"""");
                message.Headers.ETag = eTag;
            }
        }
    }
}
