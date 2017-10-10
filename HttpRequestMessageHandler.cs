using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using System.Web.Http.Filters;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.Remoting.Messaging;

namespace Elli.EPPS.Service.Handlers
{
    public class HttpRequestMessageHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CurrentMessage = request;
            return base.SendAsync(request, cancellationToken);  
        }

        public static HttpRequestMessage CurrentMessage
        {
            get { return (HttpRequestMessage)CallContext.LogicalGetData("RequestMessage"); }
            private set { CallContext.LogicalSetData("RequestMessage", value); }
        }
    }    
}