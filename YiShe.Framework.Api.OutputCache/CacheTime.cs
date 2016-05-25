using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YiShe.Framework.Api.OutputCache
{
    public class CacheTime
    {
        // client cache length in seconds
        public TimeSpan ClientTimeSpan { get; set; }

        public DateTimeOffset AbsoluteExpiration { get; set; }
    }
    public class ShortTime : IModelQuery<DateTime, CacheTime>
    {
        private readonly int serverTimeInSeconds;
        private readonly int clientTimeInSeconds;

        public ShortTime(int serverTimeInSeconds, int clientTimeInSeconds)
        {
            if (serverTimeInSeconds < 0)
                serverTimeInSeconds = 0;

            this.serverTimeInSeconds = serverTimeInSeconds;

            if (clientTimeInSeconds < 0)
                clientTimeInSeconds = 0;

            this.clientTimeInSeconds = clientTimeInSeconds;
        }

        public CacheTime Execute(DateTime model)
        {
            var cacheTime = new CacheTime
            {
                AbsoluteExpiration = model.AddSeconds(serverTimeInSeconds),
                ClientTimeSpan = TimeSpan.FromSeconds(clientTimeInSeconds)
            };

            return cacheTime;
        }
    }
}
