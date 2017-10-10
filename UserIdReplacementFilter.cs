namespace Elli.EPPS.Service.Filters
{
    using System.Web.Http.Controllers;
    using System.Web.Http.Filters;
    using Elli.EPPS.Service.Models;
    using System;
    using Elli.EPPS.Integration;
    public class UserIdReplacementFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            object argumentValue;

            // This filter assumes that the name of the parameter that accepts inputs from the url will be 'path'.
            if (actionContext.ActionArguments.TryGetValue("filter", out argumentValue))
            {
                if (argumentValue is IUserIdInput && ((IUserIdInput)argumentValue).SSOUserId.Equals("me", StringComparison.OrdinalIgnoreCase))
                {
                    var auth = (IApiUser)actionContext.ControllerContext.Configuration.DependencyResolver.GetService(typeof(IApiUser));
                    var recipientId = auth.PlatformIdentity;
                            
                    ((IUserIdInput)argumentValue).SSOUserId = recipientId;
                }
            }
        }
    }
}