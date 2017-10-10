
namespace Elli.EPPS.Service.Models.Security
{

    using System;
    using System.Security.Claims;
    using System.Web.Http.Controllers;
    using Elli.EPPS.Integration;
    public class EllieMaeAuthorization : ICustomerInstance, IApiUser, ICustomerClientId
    {
        private string _identity;

        public EllieMaeAuthorization(IRequestMessageProvider requestMessageProvider)
        {
            this.RequestMessageProvider = requestMessageProvider;
        }

        public IRequestMessageProvider RequestMessageProvider { get; set; }

        private ClaimsPrincipal GetUserPrincipal()
        {
            var requestContext = this.RequestMessageProvider.CurrentMessage
                .Properties["MS_RequestContext"] as HttpRequestContext;
            var result = requestContext.Principal as ClaimsPrincipal;
            return result;
        }

        /// <summary>
        /// Retrieves the current principal.
        /// </summary>
        public ClaimsPrincipal Principal
        {
            get
            {
                return this.GetUserPrincipal();
            }
        }

        /// <summary>
        /// Returns the Client Id
        /// </summary>
        string ICustomerClientId.ClientId
        {
            get
            {
                return FirstOrDefaultByRawType(this.GetUserPrincipal(), "elli_cid");
            }
        }

        /// <summary>
        /// The identity of the customers instance, eg. execution environment.
        /// This is always bound to a customer, aka client, but a client may have more than one.
        /// </summary>
        string ICustomerInstance.Id
        {
            get
            {
                return FirstOrDefaultByRawType(this.GetUserPrincipal(), "elli_iid");
            }
        }

        /// <summary>
        /// The unique identity of a user.
        /// </summary>
        string IApiUser.PlatformIdentity
        {
            get
            {
                return FirstOrDefaultByRawType(this.GetUserPrincipal(), "elli_uid");
            }
        }

        /// <summary>
        /// This is a helper function to simplify checking the short cclaim name with the built in claims with schemas.
        /// </summary>
        /// <param name="principal">THe principal to operate on.</param>
        /// <param name="type">The raw, aka short, claim name</param>
        /// <returns>The value of the claim</returns>
        private string FirstOrDefaultByRawType(ClaimsPrincipal principal, string type)
        {
            if (principal == null)
            {
                return null;
            }

            foreach (var claim in principal.Claims)
            {
                if (claim.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                {
                    return claim.Value;
                }
                else if (claim.Properties != null && claim.Properties.Count > 0)
                {

                    foreach (var property in claim.Properties)
                    {
                        if (property.Key.Equals("http://schemas.xmlsoap.org/ws/2005/05/identity/claimproperties/ShortTypeName", StringComparison.OrdinalIgnoreCase))
                        {
                            if (property.Value.Equals(type, StringComparison.OrdinalIgnoreCase))
                            {
                                return claim.Value;
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}